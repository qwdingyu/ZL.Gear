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
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Events;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.Services;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Evaluation;

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
        private CancellationTokenSource _cancellationTokenSource;
        private bool _disposed;
        private readonly Stopwatch _totalSw = new();
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
        private int _testStepInterval = 50;
        private IProgress<StepRunResult> _progressReporter;

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
            int testStepInterval = 500)
        {
            _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _eventBus = eventBus ?? Core.Infrastructure.DefaultEventBus.Instance; // 兼容缺省注入
            _logger = logger;
            _resultEvaluator = resultEvaluator;
            _testStepInterval = testStepInterval;
            var roles = _profileService.LoadDeviceRoles();
            deviceRoles = roles != null
                ? new System.Collections.Concurrent.ConcurrentDictionary<string, object>(roles)
                : new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        }

        private void Log(string message)
        {
            _logger?.LogInformation(message);
        }

        /// <summary>
        /// 尝试对指定步骤执行 Handler 健康检查。
        /// 通过 <see cref="WorkflowGlobal.Services"/> 解析 <see cref="StepDispatcher"/> 并执行检查。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>健康检查结果；若无法解析 <see cref="StepDispatcher"/> 则返回 null。</returns>
        public async Task<HealthCheckResult?> TryCheckStepHealthAsync(StepConfig step, CancellationToken cancellationToken = default)
        {
            try
            {
                var dispatcher = WorkflowGlobal.Services.GetService(typeof(StepDispatcher)) as StepDispatcher;
                if (dispatcher == null) return null;
                return await dispatcher.TryCheckHealthAsync(step, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // WorkflowGlobal 尚未初始化，无法解析 StepDispatcher
                return null;
            }
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
            _progressReporter = progress;
            if (_disposed) throw new ObjectDisposedException(nameof(SequenceExecutor));

            // Dispose existing CTS to prevent memory leak
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Dispose();
            }
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            var cancellationToken = _cancellationTokenSource.Token;

            // 动态调整间隔 (支持从全局上下文中读取)
            if (globalContext != null && globalContext.TryGetValue("TestStepInterval", out var intervalObj) && int.TryParse(intervalObj?.ToString(), out var intervalVal))
            {
                _testStepInterval = intervalVal;
                _log($"[配置] 步骤执行间隔已调整为: {_testStepInterval}ms");
            }

            var testWasStoppedByFail = false;
            var runInterrupted = false;
            var sharedData = new ContextVariableStore();

            var runResult = new TestRunResult { Model = model, Barcode = barcode, StartTime = DateTime.Now };
            var activeLeases = new List<IDisposable>();
            _log("==================================================");
            _log($"测试开始: 型号={model}, 条码={barcode}");
            _log("==================================================");

            _eventBus.Publish(new RunStateChangedEvent(RunState.Testing));
            _totalSw.Restart();
            // 启动后台计时器任务，但「不要 await」它，让它在后台运行
            var timerTask = RunTimerLoopAsync(cancellationToken);
            Dictionary<string, IDevice> activeDevices = new();
            bool leaseSuccess = true;
            try
            {
                try
                {
                    // === 1. 资源管理阶段 ===
                    activeDevices = await LeaseRequiredDevicesAsync(steps, activeLeases, cancellationToken);
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
                    var allSteps = steps.SelectMany(s => StepKit.FlattenSteps(s)).ToList();
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
                    runResult.StepResults = steps.Where(s => s.Enable).Select(cfg => new StepRunResult(cfg)).ToList();

                    // 执行阶段：启动递归执行
                    // 之前的 for 循环被这个循环替代，我们遍历顶层的 StepRunResult
                    foreach (var topLevelStepResult in runResult.StepResults)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        string stepKey = topLevelStepResult.StepKey;
                        // 在 stepConfigs 中找到与 topLevelStepResult 对应的 StepConfig
                        var stepConfig = steps.First(cfg => cfg.StepKey == stepKey);

                        if (!stepConfig.Enable)
                        {
                            topLevelStepResult.Status = StepExecutionStatus.Completed;
                            topLevelStepResult.Outcome = StepOutcome.Skipped;
                            continue;
                        }
                        var context = new StepContext(stepKey, stepConfig, new ReadOnlyDictionary<string, IDevice>(activeDevices), WorkflowGlobal.Services, cancellationToken,
                            RunTestMode.Auto, sharedData, globalContext, null, _log);

                        // 注入点：调用 BindProfile 将 Profile 中的映射应用到 Step 配置中
                        // deviceRoles 通常是从 SeatProfile.json 加载的 IDictionary<string, object>
                        // 为了匹配 BindProfile 的签名 IDictionary<string, string>，我们做一下转换
                        var profileStrDict = deviceRoles.ToDictionary(k => k.Key, v => v.Value?.ToString());
                        
                        // 递归应用 BindProfile 到步骤树
                        ApplyProfileToStepTree(stepConfig, profileStrDict);
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
                        
                        if (_testStepInterval > 0)
                        {
                            await Task.Delay(_testStepInterval, cancellationToken);
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
                _cancellationTokenSource.Cancel();
                sharedData?.Dispose();
                _log("正在归还所有已租用的设备...");
                activeLeases.ForEach(l => l.Dispose());
                _log("设备已归还。");

                try { await timerTask; } catch { /* 忽略取消或超时，确保清理完成 */ }
            }
            // 5. 结果处理阶段：OverallSuccess 仅此单点写入（结局模型，非 catch 补丁）
            _totalSw.Stop();
            runResult.EndTime = DateTime.Now;
            runResult.OverallSuccess = ComputeOverallSuccess(
                leaseSuccess,
                testWasStoppedByFail,
                runInterrupted,
                runResult.StepResults);
            runResult.Summary = BuildSummary(runResult);
            HandleFinalResultsAsync(runResult, steps, model, barcode);
            _log("==================================================");
            _log("测试总结");
            _log(runResult.Summary);
            _log("==================================================");

            // 输出脚本自诊断报告
            _log(ZL.Gear.Engine.Runner.Middlewares.DiagnosticsMiddleware.GenerateReport());

            return runResult;
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
            //本项目中仅限于用于PLC在SBR测试中加压负载完成通知 测试步骤测试电流   ？？？？？  不要有并行的主步骤，否则会错乱
            // RunnerEvents.StepStarted?.Invoke(context); // 逐步废弃
            _eventBus.Publish(new StepProgressEvent(stepResult));
            _log($"[执行] {stepConfig.StepName}...");
            _log($"[诊断] 步骤 {stepConfig.StepKey} 有 {stepConfig.SubSteps?.Count ?? 0} 个子步骤");
            // --- UI 更新点 (阶段四) ---
            // progress?.Report(stepResult);
            // TestEvents.StepStarted?.Invoke(stepConfig.StepName); // 旧事件可保留或替换

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
                    StepConfigNormalizer.Normalize(stepConfig, deviceRoles);

                    // 桥接：若 StepConfig.EvaluateResult 仍未显式设置，则按命令名回填 Handler 侧元数据
                    if (!stepConfig.EvaluateResult.HasValue)
                    {
                        var registry = context.GetService<IStepHandlerRegistry>();
                        if (registry != null)
                        {
                            stepConfig.EvaluateResult = registry.GetEvaluateResult(stepConfig.Command);
                        }
                    }

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
                    if (stepConfig.EvaluateResult == false)
                    {
                        stepResult.Outcome = measurementResult.Success ? StepOutcome.Passed : StepOutcome.Failed;
                        stepResult.Message = measurementResult.Message;
                    }
                    else
                    {
                        stepResult.Message = measurementResult.Message;
                        // 详细诊断仅 Debug，避免产线 Info 刷屏；关键评估结果仍用 Info（见下方）
                        _logger?.LogDebug("[诊断] 步骤 {StepName} 产生 {ValueCount} 条测量数据", stepConfig.StepName, ValueCount);

                        stepResult.Status = StepExecutionStatus.Completed;

                        if (!measurementResult.Success)
                        {
                            // 如果 dispatch 阶段就失败了（动作执行失败、通信超时、缺少参数等），直接判断为 Failed。
                            stepResult.Outcome = StepOutcome.Failed;
                            _log($"[评估] 步骤 {stepConfig.StepName} 执行失败: {measurementResult.Message}");
                        }
                        else
                        {
                            // 3. 评估结果（使用重构后的评估器）
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

                            // 使用评估器的详细消息
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
                stepResult.Message = "步骤超时或被取消。";
                _log($"[取消] 步骤 {stepConfig.StepName} 被取消");
            }
            catch (Exception ex)
            {
                stepResult.Outcome = StepOutcome.Error;
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
                _progressReporter?.Report(stepResult);
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

            var leaseTasks = requiredDeviceKeys.Select(async key =>
            {
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
            var keys = new HashSet<string>();
            Action<StepConfig> collect = null;
            collect = (step) =>
            {
                if (!step.Enable) return;
                if (!string.IsNullOrEmpty(step.Target))
                {
                    if (deviceRoles.ContainsKey(step.Target))
                        keys.Add(deviceRoles[step.Target].ToString());
                    else
                        keys.Add(step.Target);
                }
                // 添加所有额外目标设备
                if (step.AdditionalTargets != null)
                {
                    foreach (var additionalTarget in step.AdditionalTargets)
                    {
                        if (!string.IsNullOrEmpty(additionalTarget))
                        {
                             // 资源检查的逻辑也应该统一：如果是逻辑名，映射为物理名；如果是物理名，直接用。
                             // 注意：这里只是收集字符串，用于后续 LeaseAsync。
                             if (deviceRoles.ContainsKey(additionalTarget))
                                 keys.Add(deviceRoles[additionalTarget].ToString());
                             else
                                 keys.Add(additionalTarget); // 兼容直接物理名模式
                        }
                    }
                }
                // 递归处理子步骤
                if (step.SubSteps != null)
                {
                    foreach (var sub in step.SubSteps) collect(sub);
                }

                // 【关键增强】扫描 Parameters 中的 DynamicFlow 定义，找出嵌套设备需求
                if (step.Parameters != null)
                {
                    foreach (var kvp in step.Parameters)
                    {
                        CollectNestedTargets(kvp.Value, keys);
                    }
                }
            };
            // 从顶层步骤开始收集
            foreach (var topLevelStep in sequence) collect(topLevelStep);
            return keys;
        }

        /// <summary>
        /// 深度优先扫描对象（支持 JObject/JArray/Dictionary 混合模式），提取其中的 Target 字段
        /// </summary>
        private void CollectNestedTargets(object obj, HashSet<string> keys)
        {
            if (obj == null) return;

            if (obj is JObject jobj)
            {
                foreach (var prop in jobj.Properties())
                {
                    if (prop.Name == "Target" && (prop.Value.Type == JTokenType.String || prop.Value.Type == JTokenType.Raw))
                    {
                        var target = prop.Value.ToString();
                        if (!string.IsNullOrEmpty(target))
                        {
                            if (deviceRoles.ContainsKey(target))
                                keys.Add(deviceRoles[target]?.ToString() ?? target);
                            else
                                keys.Add(target);
                        }
                    }
                    CollectNestedTargets(prop.Value, keys);
                }
            }
            else if (obj is JArray jarr)
            {
                foreach (var item in jarr) CollectNestedTargets(item, keys);
            }
            else if (obj is IDictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    if (kvp.Key == "Target" && kvp.Value != null)
                    {
                        var target = kvp.Value.ToString();
                        if (!string.IsNullOrEmpty(target))
                        {
                            if (deviceRoles.ContainsKey(target))
                                keys.Add(deviceRoles[target]?.ToString() ?? target);
                            else
                                keys.Add(target);
                        }
                    }
                    CollectNestedTargets(kvp.Value, keys);
                }
            }
            else if (obj is IEnumerable<object> list)
            {
                foreach (var item in list) CollectNestedTargets(item, keys);
            }
        }
        /// <summary>
        /// 递归将 Profile 应用到步骤树。
        /// </summary>
        /// <param name="step">当前步骤。</param>
        /// <param name="profile">Profile 映射表。</param>
        private void ApplyProfileToStepTree(StepConfig step, IDictionary<string, string> profile)
        {
            if (step == null) return;
            step.BindProfile(profile);
            
            if (step.SubSteps != null)
            {
                foreach (var sub in step.SubSteps)
                {
                    ApplyProfileToStepTree(sub, profile);
                }
            }
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
        /// 请求停止当前测试序列。
        /// </summary>
        public void Stop()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                _eventBus.Publish(new RunStateChangedEvent(RunState.Stopped, "用户停止"));
                _log("收到停止请求，测试流程取消");
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
            _cancellationTokenSource?.Dispose();
        }
    }
}