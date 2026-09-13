using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Events;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Planning;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Evaluation;
using ZL.Gear.Engine.Planning;
using ZL.Gear.Engine;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 序列执行引擎 (The Heart of ZL.Gear)
    /// 职责：
    /// 1. 资源编排：根据测试需求动态租用(Lease)物理设备。
    /// 2. 流程步进：支持顺序执行、并行执行以及基于条件的逻辑分支。
    /// 3. 上下文隔离：为每一次测试执行提供独立的运行空间，是支持 Multi-Site 的核心基础。
    /// 4. 生命周期管理：负责测试从开始、执行、判定到资源归还的全生命周期维护。
    /// </summary>
    public class SequenceExecutor : IDisposable
    {
        private readonly IDeviceService _deviceService;
        private readonly ILogger _logger;
        private readonly IGearProfileService _profileService;
        private readonly IServiceProvider _serviceProvider;
        private TestRunSession _activeSession;
        private bool _disposed;
        /// <summary>
        /// 事件总线实例
        /// </summary>
        private readonly IEventBus _eventBus;
        /// <summary>
        /// 结果评估器，用于自定义评估逻辑。
        /// 如果为 null，则使用默认的 <see cref="ResultEvaluator"/>。
        /// </summary>
        private readonly IResultEvaluator _resultEvaluator;

        /// <summary>
        /// 局部设备角色映射表（Site-Specific Roles）
        /// 支持逻辑设备名（如 "Scanner"）到物理设备名（如 "Keyence_Fixed_01"）的映射。
        /// 在 Multi-Site 场景下，不同工位的相同角色会映射到不同的物理硬件。
        /// </summary>
        private System.Collections.Concurrent.ConcurrentDictionary<string, object> deviceRoles = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        
        /// <summary>
        /// 测试步骤之间的强制最小延迟，用于保护物理触点或等待 PLC 扫描周期。
        /// </summary>
        private readonly int _defaultStepIntervalMs;
        /// <summary>
        /// 同实例并发执行护栏：0=空闲，1=运行中（Interlocked 原子切换，single-flight）。
        /// </summary>
        private int _isExecuting;

        /// <summary>
        /// 当前活跃 Run 的 Id；无 Run 时为 null。用于 <see cref="Stop(Guid)"/> 与追溯。
        /// </summary>
        public Guid? ActiveRunId => _activeSession?.RunId;

        private void _log(string msg) => _logger?.LogInformation(msg);

        /// <summary>
        /// 构造函数，通过依赖注入接收其所有依赖项。
        /// </summary>
        public SequenceExecutor(
            IDeviceService deviceService,
            IGearProfileService profileService,
            IEventBus eventBus,
            ILogger<SequenceExecutor> logger,
            IResultEvaluator resultEvaluator = null,
            int testStepInterval = 500,
            IServiceProvider serviceProvider = null)
        {
            _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _eventBus = eventBus ?? Core.Infrastructure.DefaultEventBus.Instance; // 兼容缺省注入
            _logger = logger;
            _resultEvaluator = resultEvaluator;
            _defaultStepIntervalMs = testStepInterval;
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(
                nameof(serviceProvider),
                "SequenceExecutor 必须注入独立 IServiceProvider。请通过 SequenceExecutorBuilder.Build() 创建。");
            var roles = _profileService.LoadDeviceRoles();
            deviceRoles = roles != null
                ? new System.Collections.Concurrent.ConcurrentDictionary<string, object>(roles)
                : new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        }

        /// <summary>
        /// 本 Runtime 专属服务容器（每个 Builder.Build 独立实例，对标 OpenTAP 插件/DI 隔离）。
        /// </summary>
        private IServiceProvider ResolveServices() => _serviceProvider;

        private void Log(string message)
        {
            _logger?.LogInformation(message);
        }

        /// <summary>
        /// 尝试对指定步骤执行 Handler 健康检查。
        /// 通过本 Runtime 的 <see cref="IServiceProvider"/> 解析 <see cref="StepDispatcher"/> 并执行检查。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>健康检查结果；若无法解析 <see cref="StepDispatcher"/> 则返回 null。</returns>
        public async Task<HealthCheckResult?> TryCheckStepHealthAsync(StepConfig step, CancellationToken cancellationToken = default)
        {
            var dispatcher = ResolveServices().GetService(typeof(StepDispatcher)) as StepDispatcher;
            if (dispatcher == null)
            {
                return null;
            }

            return await dispatcher.TryCheckHealthAsync(step, cancellationToken);
        }

        /// <summary>
        /// 本 Runtime 已注册的步骤命令快照（扩展装载自检 / 运维诊断）。
        /// </summary>
        public IReadOnlyCollection<string> GetRegisteredStepCommands()
        {
            var registry = ResolveServices().GetService(typeof(IStepHandlerRegistry)) as IStepHandlerRegistry;
            if (registry == null)
            {
                return Array.Empty<string>();
            }

            return registry.GetRegisteredCommands()?.ToList() ?? new List<string>();
        }

        /// <summary>
        /// 后台计时器循环（当前为保留实现，暂未对外广播总耗时事件）。
        /// </summary>
        /// <param name="token">取消令牌。</param>
        private async Task RunTimerLoopAsync(CancellationToken token)
        {
            // 每秒更新一次
            const int updateIntervalMs = 1000;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 触发事件，将当前总耗时广播出去
                    // GlobalEvents.OnTestTotalTimeChanged?.Invoke(_totalSw.Elapsed);
                    // 等待一秒或直到被取消
                    await Task.Delay(updateIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 执行完整测试序列。
        /// </summary>
        /// <param name="steps">顶层步骤配置列表。</param>
        /// <param name="model">产品型号。</param>
        /// <param name="barcode">条码；为空时按手动单项测试处理。</param>
        /// <param name="globalContext">全局上下文变量。</param>
        /// <param name="token">取消令牌。</param>
        /// <param name="progress">步骤进度报告。</param>
        /// <returns>测试运行结果。</returns>
        /// <exception cref="ObjectDisposedException">执行器已释放。</exception>
        public async Task<TestRunResult> ExecuteAsync(
            List<StepConfig> steps,
            string model,
            string barcode,
            Dictionary<string, object> globalContext,
            CancellationToken token,
            IProgress<StepRunResult> progress = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SequenceExecutor));

            // P0-2 安全护栏：同一执行器实例禁止并发执行（single-flight）。
            // 并发 Run 会互相 Dispose/覆盖 CTS、进度与 Stopwatch，导致跨 Run 状态污染。
            if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "SequenceExecutor 不支持同实例并发执行：同一执行器实例同时只能运行一个测试 Run。" +
                    "请为每个并发 Run 创建独立的 SequenceExecutor 实例（见 StationManager / RunScope 方案）。");
            }

            try
            {
                return await ExecuteCoreAsync(steps, model, barcode, globalContext, token, progress);
            }
            finally
            {
                // 无论成功、失败还是异常，都必须复位并发标志，保证执行器可被复用。
                Interlocked.Exchange(ref _isExecuting, 0);
            }
        }

        /// <summary>
        /// 执行核心逻辑（由 <see cref="ExecuteAsync"/> 的 single-flight 护栏保护后调用）。
        /// </summary>
        private async Task<TestRunResult> ExecuteCoreAsync(
            List<StepConfig> steps,
            string model,
            string barcode,
            Dictionary<string, object> globalContext,
            CancellationToken token,
            IProgress<StepRunResult> progress)
        {
            // TestRunSession：本 Run 的 CTS / Progress / Stopwatch 作用域（对标 OpenTAP PlanRun）
            using var session = TestRunSession.Start(token, progress, _defaultStepIntervalMs);
            _activeSession = session;
            var cancellationToken = session.CancellationToken;
            var sharedData = new ContextVariableStore();

            try
            {
            // 动态调整间隔 (支持从全局上下文中读取，仅作用于本 Run)
            if (globalContext != null && globalContext.TryGetValue("TestStepInterval", out var intervalObj) && int.TryParse(intervalObj?.ToString(), out var intervalVal))
            {
                session.StepIntervalMs = intervalVal;
                _log($"[配置] 步骤执行间隔已调整为: {session.StepIntervalMs}ms");
            }

            var testWasStoppedByFail = false;
            var runInterrupted = false;

            var runResult = new TestRunResult
            {
                RunId = session.RunId,
                Model = model,
                Barcode = barcode,
                StartTime = DateTime.Now
            };

            // ── 编译门禁（T-P0-04b）：失败则 fail-closed，不租设备、不执行步骤 ──
            var compileResult = CompilePlan(steps);
            PublishPlanCompileEvent(compileResult);

            if (!compileResult.Success)
            {
                runResult.OverallSuccess = false;
                runResult.EndTime = DateTime.Now;
                runResult.RunVerdictKind = StepVerdictKind.Error;
                runResult.CompileErrors = compileResult.Diagnostics
                    .Where(d => d.Level == PlanCompileDiagnosticLevel.Error)
                    .ToList();
                runResult.Summary = "计划编译失败: " + string.Join("; ", compileResult.Errors);
                runResult.QuarantinedDeviceKeys = GetQuarantinedDeviceKeys().ToList();
                _log($"[计划编译] {runResult.Summary}");
                _eventBus.Publish(new RunStateChangedEvent(RunState.Error, runResult.Summary));
                HandleFinalResultsAsync(runResult, steps, model, barcode);
                return runResult;
            }

            runResult.CompileWarnings = compileResult.Diagnostics
                .Where(d => d.Level == PlanCompileDiagnosticLevel.Warning)
                .ToList();
            foreach (var warning in compileResult.Warnings)
            {
                _log($"[计划编译·警告] {warning}");
            }

            var planSnapshot = compileResult.Plan.Steps.ToList();
            runResult.PlanHash = compileResult.Plan.PlanHash;

            MaybeClearQuarantineOnRunStart();

            var activeLeases = new List<IDisposable>();
            _log("==================================================");
            _log($"测试开始: 型号={model}, 条码={barcode}");
            _log("==================================================");

            _eventBus.Publish(new RunStateChangedEvent(RunState.Testing));
            session.Stopwatch.Restart();
            // 启动后台计时器任务，但「不要 await」它，让它在后台运行
            var timerTask = RunTimerLoopAsync(cancellationToken);
            Dictionary<string, IDevice> activeDevices = new();
            bool leaseSuccess = true;
            try
            {
                try
                {
                    // === 1. 资源管理阶段 ===
                    activeDevices = await LeaseRequiredDevicesAsync(planSnapshot, activeLeases, cancellationToken);
                }
                catch (Exception ex)
                {
                    leaseSuccess = false;
                    // 展开 AggregateException，保留每台设备的失败细节，便于快速定位（单设备故障 vs 整批不可用）
                    var detail = ex is AggregateException agg
                        ? string.Join("; ", agg.Flatten().InnerExceptions.Select(e => e.Message))
                        : ex.Message;
                    runResult.Summary = $"未找到该步骤对应的设备，或设备未启用: {detail}";
                    _log($"[严重错误] {runResult.Summary}");
                    _eventBus.Publish(new RunStateChangedEvent(RunState.Error, runResult.Summary));
                }
                
                if (leaseSuccess)
                {
                    // ====================================================================
                    //  在执行任何步骤前，预扫描并创建所有主从信令对象
                    // ============================================================================

                    // 找出所有步骤（包括所有子步骤）
                    var allSteps = planSnapshot.SelectMany(s => StepKit.FlattenSteps(s)).ToList();
                    //如果DependsOn不为空，则为主步骤，需要再加防护（明确的标识出 IsMaster
                    var masterStepKeys = new HashSet<string>(allSteps.Where(step => !string.IsNullOrEmpty(step.DependsOn)).Select(step => step.DependsOn));
                    // 为每一个被识别出的主步骤，预先创建并注册信令对象
                    foreach (var masterKey in masterStepKeys)
                    {
                        _log($"[预分析] 发现主步骤 '{masterKey}'，为其创建信令对象。");
                        sharedData.RegisterSignalPairFor(masterKey);
                    }
                    // 初始化结果树
                    // 为每一个顶层步骤配置创建一个对应的 StepRunResult 实例
                    runResult.StepResults = planSnapshot.Where(s => s.Enable).Select(cfg => new StepRunResult(cfg)).ToList();

                    // 执行阶段：启动递归执行
                    // 之前的 for 循环被这个循环替代，我们遍历顶层的 StepRunResult
                    foreach (var topLevelStepResult in runResult.StepResults)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        string stepKey = topLevelStepResult.StepKey;
                        // 在 stepConfigs 中找到与 topLevelStepResult 对应的 StepConfig
                        var stepConfig = planSnapshot.First(cfg => cfg.StepKey == stepKey);

                        if (!stepConfig.Enable)
                        {
                            topLevelStepResult.Status = StepExecutionStatus.Completed;
                            topLevelStepResult.Outcome = StepOutcome.Skipped;
                            continue;
                        }
                        var context = new StepContext(stepKey, stepConfig, new ReadOnlyDictionary<string, IDevice>(activeDevices), ResolveServices(), cancellationToken,
                            RunTestMode.Auto, sharedData, globalContext, null, _log);

                        await ExecuteStepRecursiveAsync(stepConfig, topLevelStepResult, context);

                        // 检查是否需要提前终止
                        // TimeoutAction=Continue 的步骤记 Failed 防误 PASS，但不因 StopByFail 掐断整线
                        if (topLevelStepResult.Outcome != StepOutcome.Passed
                            && topLevelStepResult.Outcome != StepOutcome.Skipped
                            && stepConfig.StopByFail
                            && !IsTimeoutContinueMessage(topLevelStepResult.Message))
                        {
                            _log($"[警告] 因步骤 '{stepConfig.StepName}' 未通过且设置了 StopByFail，测试提前终止。");
                            testWasStoppedByFail = true;
                            break;
                        }
                        
                        if (session.StepIntervalMs > 0)
                        {
                            await Task.Delay(session.StepIntervalMs, cancellationToken);
                        }
                    }
                    runResult.EndTime = DateTime.Now;
                }
            }
            catch (OperationCanceledException)
            {
                runInterrupted = true;
                runResult.Summary = "测试因超时或用户取消而在准备阶段终止。";
                _log($"[警告] {runResult.Summary}");
            }
            catch (Exception ex)
            {
                runInterrupted = true;
                runResult.Summary = $"测试序列因严重错误而中断: {ex.Message}";
                _log($"[严重错误] {runResult.Summary}");
                _eventBus.Publish(new RunStateChangedEvent(RunState.Error, runResult.Summary));
            }
            finally
            {
                session.RequestStop();
                sharedData?.Dispose();
                _log("正在归还所有已租用的设备...");
                activeLeases.ForEach(l => l.Dispose());
                _log("设备已归还。");

                try { await timerTask; } catch { /* 忽略取消或超时，确保清理完成 */ }
                _activeSession = null;
            }
            // 5. 结果处理阶段：OverallSuccess 仅此单点写入（结局模型，非 catch 补丁）
            session.Stopwatch.Stop();
            runResult.EndTime = DateTime.Now;
            runResult.OverallSuccess = ComputeOverallSuccess(
                leaseSuccess,
                testWasStoppedByFail,
                runInterrupted,
                runResult.StepResults);
            runResult.RunVerdictKind = ComputeRunVerdictKind(
                leaseSuccess,
                testWasStoppedByFail,
                runInterrupted,
                runResult.StepResults);
            runResult.QuarantinedDeviceKeys = GetQuarantinedDeviceKeys().ToList();
            runResult.Summary = BuildSummary(runResult);
            HandleFinalResultsAsync(runResult, planSnapshot, model, barcode);
            _log("==================================================");
            _log("测试总结");
            _log(runResult.Summary);
            _log("==================================================");

            // 输出脚本自诊断报告
            _log(ZL.Gear.Engine.Runner.Middlewares.DiagnosticsMiddleware.GenerateReport());

            return runResult;
            }
            finally
            {
                // 编译失败或异常路径也必须清掉 ActiveRunId，否则 Stop(Guid) 可能误操作下一 Run
                sharedData?.Dispose();
                _activeSession = null;
            }
        }

        /// <summary>
        /// 递归执行单个步骤及其子步骤树。
        /// </summary>
        /// <param name="stepConfig">当前要执行的步骤配置。</param>
        /// <param name="stepResult">与 stepConfig 对应的运行时结果对象。</param>
        /// <param name="context">执行上下文。</param>
        /// <remarks>子步骤执行失败且配置 StopByFail=true 时，会提前中断兄弟步骤。</remarks>
        private async Task ExecuteStepRecursiveAsync(StepConfig stepConfig, StepRunResult stepResult, StepContext context)
        {
            var sw = Stopwatch.StartNew();
            stepResult.StartTime = DateTime.Now;
            stepResult.Status = StepExecutionStatus.Running;
            _eventBus.Publish(new StepProgressEvent(stepResult));
            _log($"[执行] {stepConfig.StepName}...");
            _log($"[诊断] 步骤 {stepConfig.StepKey} 有 {stepConfig.SubSteps?.Count ?? 0} 个子步骤");

            try
            {
                // 1. 如果有子步骤，先执行子步骤
                if (stepConfig.SubSteps != null && stepConfig.SubSteps.Any())
                {
                    await ExecuteSubStepsAsync(stepConfig, stepResult, context);

                    // 关键修复：如果任何子步骤未通过，则立即将当前步骤标记为失败
                    var failedSubSteps = stepResult.SubStepResults
                        .Where(sub => sub.Outcome != StepOutcome.Passed && sub.Outcome != StepOutcome.Skipped)
                        .ToList();

                    if (failedSubSteps.Any())
                    {
                        stepResult.Outcome = StepOutcome.Failed;
                        stepResult.Message = $"以下子步骤未通过: {string.Join(", ", failedSubSteps.Select(f => f.StepName))}";
                        _log($"[子步骤失败] {stepConfig.StepName} 因子步骤失败而终止");
                        return; // 不再执行当前步骤的命令
                    }

                    _log($"[诊断] 步骤 {stepConfig.StepName} 的所有子步骤执行完成");
                }
                if (!string.Equals(stepConfig.StepType, "GROUP", StringComparison.OrdinalIgnoreCase))
                {
                    // 2. 执行本步骤自身的原子命令（仅当所有子步骤都通过时）
                    var dispatcher = context.GetService<StepDispatcher>();
                    var measurementResult = await dispatcher.DispatchSingleAsync(stepConfig, context);

                    // 关键修复：明确将测量数据存储到当前步骤的 StepMeasurements 集合
                    int ValueCount = 0;
                    if (measurementResult.Value != null)
                    {
                        ValueCount = measurementResult.Value.Count;
                        foreach (var measurement in measurementResult.Value)
                        {
                            stepResult.StepMeasurements.Add(measurement);
                        }
                    }
                    // 由 StepConfig.EvaluateResult / StepHandlerCommandAttribute.EvaluateResult 控制是否跳过结果评估
                    stepResult.Message = measurementResult.Message;

                    if (measurementResult.Status == ExecutionStatus.Skipped)
                    {
                        ApplyVerdictFromExecution(stepResult, stepConfig, measurementResult);
                    }
                    else if (stepConfig.EvaluateResult == false)
                    {
                        ApplyVerdictFromExecution(stepResult, stepConfig, measurementResult);
                    }
                    else
                    {
                        _logger?.LogDebug("[诊断] 步骤 {StepName} 产生 {ValueCount} 条测量数据", stepConfig.StepName, ValueCount);

                        stepResult.Status = StepExecutionStatus.Completed;

                        if (!measurementResult.Success)
                        {
                            ApplyVerdictFromExecution(stepResult, stepConfig, measurementResult);
                            _log($"[评估] 步骤 {stepConfig.StepName} 执行失败: {measurementResult.Message}");
                        }
                        else
                        {
                            var evaluationResult = (_resultEvaluator ?? ResultEvaluator.Instance).Evaluate(stepResult, stepConfig);
                            _logger?.LogDebug(
                                "[诊断] 评估详情: ExecutionType={ExecutionType}, ExpectedResults.Count={ExpectedCount}, Status={Status}, Outcome={Outcome}, Success={Success}, Message={Message}",
                                stepConfig.ExecutionType,
                                stepConfig.ExpectedResults?.Count ?? 0,
                                stepResult.Status,
                                stepResult.Outcome,
                                evaluationResult.Success,
                                evaluationResult.Message);

                            stepResult.Outcome = evaluationResult.Success ? StepOutcome.Passed : StepOutcome.Failed;
                            stepResult.VerdictKind = evaluationResult.Success ? StepVerdictKind.Passed : StepVerdictKind.Failed;

                            if (!string.IsNullOrEmpty(evaluationResult.Message))
                            {
                                stepResult.Message = evaluationResult.Message;
                            }

                            _log($"[评估] 步骤 {stepConfig.StepName} 评估结果: {evaluationResult.Success}, 消息: {evaluationResult.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                stepResult.Outcome = StepOutcome.Error;
                stepResult.VerdictKind = context.CancellationToken.IsCancellationRequested
                    ? StepVerdictKind.Cancelled
                    : StepVerdictKind.Aborted;
                stepResult.Message = "步骤超时或被取消。";
                _log($"[取消] 步骤 {stepConfig.StepName} 被取消");
            }
            catch (Exception ex)
            {
                stepResult.Outcome = StepOutcome.Error;
                stepResult.VerdictKind = StepVerdictKind.Error;
                stepResult.Message = $"步骤执行时发生内部错误: {ex.Message}";
                _log($"[异常] 步骤 {stepConfig.StepName} 执行失败: {ex}");
            }
            finally
            {
                sw.Stop();
                stepResult.DurationSeconds = (sw.Elapsed.TotalSeconds).ToString("F2");
                stepResult.EndTime = DateTime.Now;
                stepResult.Status = StepExecutionStatus.Completed;

                var outcomeDisplay = stepResult.Outcome == StepOutcome.Passed ? "PASS" : stepResult.Outcome.ToString().ToUpper();
                _logger?.LogInformation($"[{outcomeDisplay}] {stepConfig.StepName} 耗时={stepResult.DurationSeconds:F2}s, 消息: {stepResult.Message}");
                _activeSession?.Progress?.Report(stepResult);
                _eventBus.Publish(new StepProgressEvent(stepResult));
            }
        }
        /// <summary>
        /// 执行父步骤下的全部子步骤（支持并行/串行模式）。
        /// </summary>
        /// <param name="parentConfig">父步骤配置。</param>
        /// <param name="parentResult">父步骤结果对象。</param>
        /// <param name="parentContext">父步骤执行上下文。</param>
        private async Task ExecuteSubStepsAsync(StepConfig parentConfig, StepRunResult parentResult, StepContext parentContext)
        {
            _log($"[子步骤] 开始执行 {parentConfig.StepName} 的 {parentConfig.SubSteps.Count} 个子步骤，模式: {parentConfig.ExecutionMode}");

            // 修复：预先建立配置映射，避免并行环境中的查找
            var stepMapping = parentConfig.SubSteps.GroupBy(c => c.StepKey).ToDictionary(g => g.Key, g => g.First());
            //主步骤里面的 ExecutionMode 才有效，如果为叶子节点，则无效
            if (parentConfig.ExecutionMode == StepExecutionMode.Parallel)
            {
                try
                {
                    var subTasks = parentResult.SubStepResults.Select(async subResult =>
                    {
                        if (parentContext.CancellationToken.IsCancellationRequested) { _log($"[取消] 子步骤 {subResult.StepName} 因取消而跳过"); return; }

                        if (stepMapping.TryGetValue(subResult.StepKey, out var subConfig))
                        {
                            // 并行路径必须使用独立 clone：Normalize 会就地改写 TargetDict/Parameters，不能共享原配置
                            var clonedConfig = (StepConfig)subConfig.Clone();
                            var subContext = parentContext.CreateChildContext(clonedConfig);
                            // 在并行模式下，如果依赖性为空，且有延迟时间定义才进行延迟；否则，交由 DependsOn对应的主步骤进行 事件通知
                            if (string.IsNullOrEmpty(clonedConfig.DependsOn) && clonedConfig.StartDelayMs > 0)
                            {
                                _log($"[延迟] 子步骤 {clonedConfig.StepName} 延迟 {clonedConfig.StartDelayMs}ms 执行");
                                await Task.Delay(clonedConfig.StartDelayMs, subContext.CancellationToken);
                            }

                            await ExecuteStepRecursiveAsync(clonedConfig, subResult, subContext);
                        }
                        else
                        {
                            _log($"[错误] 未找到子步骤 {subResult.StepKey} 的配置");
                            subResult.Outcome = StepOutcome.Error;
                            subResult.Message = "配置缺失";
                        }
                    });

                    await Task.WhenAll(subTasks);
                }
                finally
                {
                }
                _log($"[子步骤] {parentConfig.StepName} 的所有并行子步骤执行完成");
            }
            else
            {
                foreach (var subResult in parentResult.SubStepResults)
                {
                    if (parentContext.CancellationToken.IsCancellationRequested) { _log($"[取消] 串行子步骤执行因取消而终止"); break; }

                    if (stepMapping.TryGetValue(subResult.StepKey, out var subConfig))
                    {
                        // 串行亦使用 clone，避免 Normalize 污染后续复用/重跑的同一配置树
                        var clonedConfig = (StepConfig)subConfig.Clone();
                        var subContext = parentContext.CreateChildContext(clonedConfig);
                        await ExecuteStepRecursiveAsync(clonedConfig, subResult, subContext);

                        // 如果子步骤失败且设置了StopByFail，则不再继续执行后续的兄弟步骤
                        // [TimeoutContinue] 超时软失败：记 Failed 但不中断兄弟步骤
                        if (subResult.Outcome != StepOutcome.Passed
                            && clonedConfig.StopByFail
                            && !IsTimeoutContinueMessage(subResult.Message))
                        {
                            _log($"[停止] 因步骤 '{clonedConfig.StepName}' 失败且设置了 StopByFail，停止执行后续兄弟步骤");
                            break;
                        }
                    }
                    else
                    {
                        _log($"[错误] 未找到子步骤 {subResult.StepKey} 的配置");
                        subResult.Outcome = StepOutcome.Error;
                        subResult.Message = "配置缺失";

                        // subConfig 在 TryGetValue 失败时不可用；沿用父步骤 StopByFail 策略
                        if (parentConfig.StopByFail)
                        {
                            break;
                        }
                    }
                }

                _log($"[子步骤] {parentConfig.StepName} 的所有串行子步骤执行完成");
            }
        }

        /// <summary>
        /// 并发租用步骤序列所需的物理设备。
        /// </summary>
        /// <param name="steps">步骤配置列表。</param>
        /// <param name="leases">租约集合，成功后会向其中追加释放句柄。</param>
        /// <param name="token">取消令牌。</param>
        /// <returns>逻辑设备名到物理设备实例的映射。</returns>
        private async Task<Dictionary<string, IDevice>> LeaseRequiredDevicesAsync(List<StepConfig> steps, List<IDisposable> leases, CancellationToken token)
        {
            var requiredDeviceKeys = CollectRequiredDevices(steps);
            var activeDevices = new System.Collections.Concurrent.ConcurrentDictionary<string, IDevice>();

            var quarantine = ResolveServices().GetService(typeof(IDeviceQuarantineService)) as IDeviceQuarantineService;

            var leaseTasks = requiredDeviceKeys.Select(async key =>
            {
                if (quarantine != null && quarantine.IsQuarantined(key, out var reason))
                {
                    throw new InvalidOperationException($"设备 '{key}' 处于超时隔离状态，拒绝租约: {reason}");
                }

                var lease = await _deviceService.LeaseAsync<IDevice>(key, token);
                lock (leases) { leases.Add(lease); }
                activeDevices.TryAdd(key, lease.Device);
            });

            await Task.WhenAll(leaseTasks);

            _log("所有设备并发租用及初始化成功。");
            return activeDevices.ToDictionary(k => k.Key, v => v.Value);
        }

        /// <summary>
        /// 处理最终运行结果事件。
        /// </summary>
        /// <param name="runResult">运行结果。</param>
        /// <param name="_originalConfigs">原始配置列表。</param>
        /// <param name="model">产品型号。</param>
        /// <param name="barcode">条码。</param>
        private void HandleFinalResultsAsync(TestRunResult runResult, List<StepConfig> _originalConfigs, string model, string barcode)
        {
            // 如果条码为空，则认定为手动单项测试
            if (!string.IsNullOrEmpty(barcode))
            {
                _eventBus.Publish(new TestRunCompletedEvent(runResult));
                _eventBus.Publish(new RunStateChangedEvent(runResult.OverallSuccess ? RunState.Stopped : RunState.Error));
            }
        }

        /// <summary>
        /// 会话结局单点计算：租赁失败 / StopByFail / 运行中断 / 步骤分支 — 任一否决则整体失败。
        /// </summary>
        /// <remarks>
        /// 禁止在 catch 内抢写 OverallSuccess 再被此处覆盖；中断用 <paramref name="runInterrupted"/> 表达。
        /// </remarks>
        private static bool ComputeOverallSuccess(
            bool leaseSuccess,
            bool stoppedByFail,
            bool runInterrupted,
            IReadOnlyList<StepRunResult> stepResults)
        {
            if (!leaseSuccess || stoppedByFail || runInterrupted)
            {
                return false;
            }

            return stepResults == null
                || stepResults.Count == 0
                || stepResults.All(r => r.IsBranchSuccessful);
        }

        /// <summary>
        /// 构建测试结果摘要字符串。
        /// </summary>
        /// <param name="runResult">运行结果。</param>
        /// <returns>可读的摘要文本。</returns>
        private string BuildSummary(TestRunResult runResult)
        {
            var sb = new StringBuilder();
            sb.AppendLine(runResult.OverallSuccess ? "测试通过 (PASS)" : "测试失败 (FAIL)");
            sb.AppendLine($"RunVerdict: {runResult.RunVerdictKind}");
            if (runResult.QuarantinedDeviceKeys != null && runResult.QuarantinedDeviceKeys.Count > 0)
            {
                sb.AppendLine($"隔离设备: {string.Join(", ", runResult.QuarantinedDeviceKeys)}");
            }

            sb.AppendLine($"总耗时: {runResult.TotalDurationSeconds:F2} 秒.");
            sb.AppendLine("详细步骤摘要:");

            // 递归构建摘要
            Action<StepRunResult, string> buildStepSummary = null;
            buildStepSummary = (stepResult, indent) =>
            {
                sb.AppendLine($"{indent}- {stepResult.StepName}: {stepResult.Outcome} ({stepResult.DurationSeconds:F2}s) - {stepResult.Message}");
                foreach (var subResult in stepResult.SubStepResults)
                {
                    buildStepSummary(subResult, indent + "  ");
                }
            };

            foreach (var topLevelResult in runResult.StepResults)
            {
                buildStepSummary(topLevelResult, "  ");
            }

            return sb.ToString();
        }
        /// <summary>
        /// 扫描整个步骤序列，收集所需设备键。
        /// </summary>
        /// <param name="sequence">顶层步骤列表。</param>
        /// <returns>所需设备键集合。</returns>
        private HashSet<string> CollectRequiredDevices(List<StepConfig> sequence)
        {
            return DeviceKeyResolver.CollectKeysFromStepTree(sequence, SnapshotDeviceRoles());
        }

        private Dictionary<string, object> SnapshotDeviceRoles()
            => deviceRoles.ToDictionary(k => k.Key, v => v.Value);

        private void ApplyVerdictFromExecution(
            StepRunResult stepResult,
            StepConfig stepConfig,
            ExecutionResult<List<Measurement>> measurementResult)
        {
            switch (measurementResult.Status)
            {
                case ExecutionStatus.Skipped:
                    stepResult.Outcome = StepOutcome.Skipped;
                    stepResult.VerdictKind = StepVerdictKind.Skipped;
                    return;
                case ExecutionStatus.TimedOut:
                    stepResult.Outcome = StepOutcome.Failed;
                    stepResult.VerdictKind = StepVerdictKind.TimedOut;
                    QuarantineStepDevices(stepConfig, measurementResult.Message);
                    return;
                case ExecutionStatus.Cancelled:
                    stepResult.Outcome = StepOutcome.Error;
                    stepResult.VerdictKind = StepVerdictKind.Cancelled;
                    return;
            }

            stepResult.Outcome = measurementResult.Success ? StepOutcome.Passed : StepOutcome.Failed;
            stepResult.VerdictKind = measurementResult.Success ? StepVerdictKind.Passed : StepVerdictKind.Failed;
        }

        private void QuarantineStepDevices(StepConfig step, string reason)
        {
            var quarantine = ResolveServices().GetService(typeof(IDeviceQuarantineService)) as IDeviceQuarantineService;
            if (quarantine == null)
            {
                return;
            }

            var runId = _activeSession?.RunId ?? Guid.Empty;
            var keys = DeviceKeyResolver.CollectKeysFromStepTree(new[] { step }, SnapshotDeviceRoles());
            foreach (var key in keys)
            {
                quarantine.Quarantine(key, reason, runId);
                _log($"[隔离] 设备 '{key}' 已标记隔离: {reason}");
            }
        }

        private void MaybeClearQuarantineOnRunStart()
        {
            var options = ResolveServices().GetService(typeof(SequenceExecutorRuntimeOptions)) as SequenceExecutorRuntimeOptions;
            if (options?.ClearDeviceQuarantineOnRunStart == true)
            {
                ClearDeviceQuarantine();
            }
        }

        /// <summary>
        /// 清空本 Runtime 全部设备隔离（运维复位 / 复检工位）。
        /// </summary>
        public void ClearDeviceQuarantine()
        {
            var quarantine = ResolveServices().GetService(typeof(IDeviceQuarantineService)) as IDeviceQuarantineService;
            quarantine?.ClearAll();
            _log("[隔离] 已清空全部设备隔离标记");
        }

        /// <summary>
        /// 解除单台设备隔离。
        /// </summary>
        public void ReleaseDeviceQuarantine(string deviceKey)
        {
            var quarantine = ResolveServices().GetService(typeof(IDeviceQuarantineService)) as IDeviceQuarantineService;
            quarantine?.Release(deviceKey);
        }

        /// <summary>
        /// 当前隔离中的设备键快照。
        /// </summary>
        public IReadOnlyCollection<string> GetQuarantinedDeviceKeys()
        {
            var quarantine = ResolveServices().GetService(typeof(IDeviceQuarantineService)) as IDeviceQuarantineService;
            return quarantine?.GetQuarantinedKeys() ?? Array.Empty<string>();
        }

        private static StepVerdictKind ComputeRunVerdictKind(
            bool leaseSuccess,
            bool stoppedByFail,
            bool runInterrupted,
            IReadOnlyList<StepRunResult> stepResults)
        {
            if (!leaseSuccess)
            {
                return StepVerdictKind.Error;
            }

            if (runInterrupted)
            {
                return StepVerdictKind.Cancelled;
            }

            if (stoppedByFail)
            {
                return StepVerdictKind.Aborted;
            }

            var verdicts = FlattenStepVerdicts(stepResults);
            if (verdicts.Count == 0)
            {
                return StepVerdictKind.Passed;
            }

            if (verdicts.Any(v => v == StepVerdictKind.TimedOut))
            {
                return StepVerdictKind.TimedOut;
            }

            if (verdicts.Any(v => v == StepVerdictKind.Error))
            {
                return StepVerdictKind.Error;
            }

            if (verdicts.Any(v => v == StepVerdictKind.Failed))
            {
                return StepVerdictKind.Failed;
            }

            if (verdicts.Any(v => v == StepVerdictKind.Aborted))
            {
                return StepVerdictKind.Aborted;
            }

            if (verdicts.Any(v => v == StepVerdictKind.Cancelled))
            {
                return StepVerdictKind.Cancelled;
            }

            return StepVerdictKind.Passed;
        }

        private static List<StepVerdictKind> FlattenStepVerdicts(IReadOnlyList<StepRunResult> stepResults)
        {
            var list = new List<StepVerdictKind>();
            if (stepResults == null)
            {
                return list;
            }

            foreach (var step in stepResults)
            {
                CollectVerdicts(step, list);
            }

            return list;
        }

        private static void CollectVerdicts(StepRunResult step, ICollection<StepVerdictKind> sink)
        {
            if (step == null)
            {
                return;
            }

            if (step.VerdictKind != StepVerdictKind.None && step.VerdictKind != StepVerdictKind.Skipped)
            {
                sink.Add(step.VerdictKind);
            }

            if (step.SubStepResults == null)
            {
                return;
            }

            foreach (var sub in step.SubStepResults)
            {
                CollectVerdicts(sub, sink);
            }
        }

        /// <summary>
        /// 将调用方 Plan 编译为运行快照（Clone / Profile / Normalize / 预检）。
        /// </summary>
        /// <remarks>
        /// 优先从本 Runtime 的 DI 解析 <see cref="IPlanCompiler"/>；缺省回退 <see cref="DefaultPlanCompiler"/>。
        /// DeviceRoleMap 做快照，避免编译过程中 ConcurrentDictionary 被并发修改。
        /// </remarks>
        private void PublishPlanCompileEvent(PlanCompileResult compileResult)
        {
            var planHash = compileResult.Plan?.PlanHash ?? string.Empty;
            _eventBus.Publish(new PlanCompileCompletedEvent(
                compileResult.Success,
                planHash,
                compileResult.Diagnostics));
        }

        private PlanCompileResult CompilePlan(IReadOnlyList<StepConfig> sourceSteps)
        {
            var services = ResolveServices();
            var compiler = services.GetService(typeof(IPlanCompiler)) as IPlanCompiler
                ?? new DefaultPlanCompiler();

            var roleSnapshot = deviceRoles.ToDictionary(k => k.Key, v => v.Value);
            var profileStrDict = roleSnapshot.ToDictionary(k => k.Key, v => v.Value?.ToString());
            var compileOptions = services.GetService(typeof(PlanCompileOptions)) as PlanCompileOptions
                ?? new PlanCompileOptions();

            var context = new PlanCompileContext
            {
                DeviceRoleMap = roleSnapshot,
                ProfileMap = profileStrDict,
                HandlerRegistry = services.GetService(typeof(IStepHandlerRegistry)) as IStepHandlerRegistry,
                HandlerLookup = services.GetService(typeof(IStepHandlerLookup)) as IStepHandlerLookup,
                Options = compileOptions,
                WorkflowEvaluator = services.GetService(typeof(IWorkflowEvaluator)) as IWorkflowEvaluator
            };

            return compiler.Compile(sourceSteps, context);
        }

        /// <summary>
        /// 判断是否为 TimeoutAction=Continue 产生的软失败消息（步骤记失败，但不应触发 StopByFail 掐断）。
        /// </summary>
        private static bool IsTimeoutContinueMessage(string message)
        {
            return !string.IsNullOrEmpty(message)
                && message.IndexOf("[TimeoutContinue]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 请求停止当前活跃 Run（若无活跃 Run 则 no-op）。
        /// </summary>
        public void Stop() => Stop(activeRunId: null);

        /// <summary>
        /// 按 RunId 请求停止；RunId 不匹配时不操作（对标 OpenTAP 按 PlanRun 取消）。
        /// </summary>
        public void Stop(Guid runId)
        {
            Stop(activeRunId: runId);
        }

        private void Stop(Guid? activeRunId)
        {
            var session = _activeSession;
            if (session == null)
            {
                return;
            }

            if (activeRunId.HasValue && session.RunId != activeRunId.Value)
            {
                return;
            }

            if (!session.CancellationToken.IsCancellationRequested)
            {
                session.RequestStop();
                _eventBus.Publish(new RunStateChangedEvent(RunState.Stopped, "用户停止"));
                _log($"收到停止请求，RunId={session.RunId}，测试流程取消");
            }
        }

        /// <summary>
        /// 释放执行器资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _activeSession?.Dispose();
            _activeSession = null;
        }
    }
}