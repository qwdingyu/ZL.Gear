using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Protocol;
using ZL.Gear.Core.Events;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Sensing
{
    public class MeasurementKit
    {
        private readonly IProtocolHandler _protocolHandler;
        private readonly Action<string> _log;

        public MeasurementKit(IProtocolHandler protocolHandler, Action<string> log)
        {
            _protocolHandler = protocolHandler ?? throw new ArgumentNullException(nameof(protocolHandler));
            _log = log;
        }

        /// <summary>
        /// 统一执行入口
        /// </summary>
        public async Task<ExecutionResult<T>> SamplingAndAdaptAsync<T>(
            StepContext context,
            string command,
            SamplingConfig<T> baseConfig,
            Func<string, IEnumerable<T>> responseParser,
            CancellationToken token)
        {
            // 0. 净化管道 (防御性编程)
            await _protocolHandler.FlushBuffersAsync(token);

            // 1. 工厂模式：根据 Context 生成最终 Config
            SamplingConfig<T> config = PrepareConfig(context, baseConfig);

            // 2. 注入 Logger 以便 ValidateAndLogConfiguration 能正常工作
            if (config.Logger == null) config.Logger = _log;

            ExeResult<T> internalResult = await PerformSamplingAsync(
                context.StepKey,
                context.StepConfig.StepName,
                context.StepConfig.Target,
                command,
                config,
                responseParser,
                token
            );

            // 3. 结果适配 (保持不变)
            if (internalResult == null) return ExecutionResult<T>.Failed("采样过程返回空结果");

            // 只有成功且规格通过才算 Success
            bool isOverallSuccess = internalResult.IsSuccess && internalResult.SpecPassed;
            string msg = isOverallSuccess ? internalResult.Message :
                         (!internalResult.SpecPassed && internalResult.IsSuccess) ? $"规格不通过: {internalResult.Message}" :
                         $"[{internalResult.Status}] {internalResult.Message}";

            return isOverallSuccess
                ? ExecutionResult<T>.Succeeded(internalResult.Value, internalResult.AllSamples.Count, msg)
                : ExecutionResult<T>.Failed(msg, internalResult.Value, internalResult.AllSamples.Count);
        }

        private SamplingConfig<T> PrepareConfig<T>(StepContext context, SamplingConfig<T> baseConfig)
        {
            // 如果是自动模式，必须提供 config
            if (context.RunTestMode != RunTestMode.Manual && baseConfig == null)
                throw new ArgumentException("Auto mode requires a valid SamplingConfig.");

            // 如果 baseConfig 为 null (手动模式可能发生)，new 一个新的
            var config = baseConfig ?? new SamplingConfig<T>();

            if (context.RunTestMode == RunTestMode.Manual)
            {
                var parameters = context.StepConfig.Parameters ?? new Dictionary<string, object>();

                // [新特性 2] 动态参数调整：注入委托，实时从 Parameters 字典取值
                config.DynamicIntervalProvider = () => parameters.Get<int>("SampleIntervalMs", 100);
                config.SampleIntervalMs = parameters.Get<int>("SampleIntervalMs", 100); // 初始值

                // [新特性 1] 内存流式保护：手动模式不存全量数据
                config.SaveAllSamples = false;

                // 手动模式特有设置：超时无限，或者由 Context 决定，严格依赖 Token 取消
                config.TotalTimeoutMs = Timeout.Infinite;

                // 3. 覆盖策略 (Strategy)
                // 手动模式通常是“一直采集直到停止”，所以使用 DurationStrategy 配合无限时间
                // 注意：这里保留了 AverageCalculator，如果你需要手动模式显示实时值而不是平均值，可以换成 LastValueCalculator
                // 目标：手动模式强制无限时长，但必须保留用户配置的计算逻辑(Max/Min/Last等)

                IResultCalculator<T> originalCalculator = null;

                // 尝试从原有策略中提取计算器
                // (利用模式匹配检查它是否是我们定义的基类)
                if (config.Strategy is SamplingStrategyBase<T> baseStrategy)
                {
                    // 我们需要在 SamplingStrategyBase 中公开 ResultCalculator 属性，
                    // 或者通过反射获取（如果不方便改基类）。
                    // 建议：去修改 SamplingStrategyBase，把 ResultCalculator 属性改为 public 或 internal
                    originalCalculator = baseStrategy.GetCalculator();
                }
                if (config.Strategy is QuickPassStrategy<T>)
                {
                    // 如果是快速通过策略，手动模式下我们也应该保留它！
                    // 因为“快速通过”本质上不依赖固定时长，它依赖数据特征。
                    // 所以：不做任何替换，直接用原 Strategy。
                }
                else
                {
                    // 只有当时长/次数策略时，才强制转为无限时长
                    config.Strategy = new DurationStrategy<T>(TimeSpan.FromMilliseconds(-1), originalCalculator);
                }

                // 4. 覆盖触发器 (Trigger)
                // 关键点：手动模式需要通知子步骤，使用 ImmediateTrigger (立即触发)
                // 如果需要“固定延时启动”，可以在这里换成 new TimeDelayedTrigger<T>(TimeSpan.FromSeconds(1))
                config.Trigger = new ImmediateTrigger<T>();

                // 5. 事件回调保留
                // config.OnTestStarted 等回调保持 baseConfig 原样，这样 UI 层传入的 Action 依然有效
            }
            else
            {
                // 自动模式：强制开启数据保存
                config.SaveAllSamples = true;
                config.DynamicIntervalProvider = null; // 自动模式不动态调整
            }

            return config;
        }
        public async Task<ExeResult<T>> PerformSamplingAsync<T>(
            string stepKey,
            string stepName,
            string target,
            string command,
            SamplingConfig<T> config,
            Func<string, IEnumerable<T>> responseParser,
            CancellationToken token)
        {
            // ==========================
            // 设计目标（闭环、不污染调用者）
            // 1) 不因超时/取消抛异常到上层（吞掉 OCE，内部转成结果）
            // 2) 不管怎样退出，都尽量给出可计算的结果（至少能返回最后有效值）
            // 3) 即使 collectedSamples 为空，也能用 lastSample 兜底计算（只要采到过有效样本）
            // ==========================

            ExeResult<T> finalResult = null;

            // 采样集合：自动模式可能保存全量；手动流式模式可能只保留最后一个或甚至为空（边界情况）
            var collectedSamples = new List<T>();

            // 触发状态（比如 ImmediateTrigger 一触发就激活）
            bool isTestActive = false;

            // lastSample：用于“超时/取消时也能算值”的核心兜底
            T lastSample = default;
            bool hasAnyValidSample = false;

            // UI 节流控制（防止高频刷新卡 UI）
            long lastNotifyTick = 0;
            const int UI_THROTTLE_MS = 100;

            // 取消/超时状态（闭环逻辑的关键）
            bool userCanceled = false;
            bool timedOut = false;

            // CTS：显式区分“无超时”与“有超时”
            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource linkedCts = null;

            try
            {
                // 1) 配置校验与触发器复位
                config.ValidateAndLogConfiguration(stepName);
                config.Trigger.Reset();

                // 2) 构建 loopToken：无超时=仅用外部 token；有超时=token+timeout 联动
                CancellationToken loopToken;
                if (config.TotalTimeoutMs == Timeout.Infinite)
                {
                    // 无超时：严格依赖调用者传入的 token（用户点击停止）
                    loopToken = token;
                }
                else
                {
                    // 有超时：超时触发会 cancel timeoutCts，从而 cancel loopToken
                    timeoutCts = new CancellationTokenSource(config.TotalTimeoutMs);
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                    loopToken = linkedCts.Token;
                }

                var stopwatch = new System.Diagnostics.Stopwatch();
                _log?.Invoke($"[{stepName}] 开始采样... 模式: {config.Strategy.StrategyName}");

                // ================
                // 主循环：只要没取消，就持续采样
                // 重要：取消/超时不一定会抛 OCE，可能以“while 条件自然退出”发生
                // 所以必须在 while 内部和 while 之后都做“结果收口”
                // ================
                while (!loopToken.IsCancellationRequested)
                {
                    stopwatch.Restart();

                    // 每轮动态获取间隔（手动模式可动态调整）
                    int currentInterval = config.DynamicIntervalProvider?.Invoke() ?? config.SampleIntervalMs;

                    // 读取样本：这里可能抛 OCE，也可能返回 default/null（读失败/无数据）
                    T sample = default;
                    try
                    {
                        sample = await ReadSampleAsync(stepName, _protocolHandler, command, responseParser, _log, loopToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // 闭环：不把 OCE 抛出去，而是标记状态并 break
                        userCanceled = token.IsCancellationRequested;
                        timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                        break;
                    }

                    // 读取到 null/默认值：当做“本轮无有效数据”
                    if (sample == null)
                    {
                        // 取消可能发生在两次 await 之间，这里主动检查一次，避免漏掉
                        if (loopToken.IsCancellationRequested)
                        {
                            userCanceled = token.IsCancellationRequested;
                            timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                            break;
                        }

                        // 仍未取消，则按间隔继续下一轮
                        try
                        {
                            await DelayRemaining(stopwatch, currentInterval, loopToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // 闭环：吞掉 OCE，标记并退出
                            userCanceled = token.IsCancellationRequested;
                            timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                            break;
                        }

                        continue;
                    }

                    // 维护 lastSample：只要拿到过有效 sample，就可以保证“超时/取消也能算值”
                    lastSample = sample;
                    hasAnyValidSample = true;

                    // 3) UI 节流刷新
                    long currentTick = Environment.TickCount;
                    if (currentTick - lastNotifyTick > UI_THROTTLE_MS)
                    {
                        NotifyRealTimeValue(stepKey, target, sample);
                        lastNotifyTick = currentTick;
                    }

                    // 4) 触发器激活（首次满足条件才进入“记录/计算”状态）
                    if (!isTestActive && config.Trigger.ShouldStart(sample, isTestActive))
                    {
                        isTestActive = true;
                        _log?.Invoke($"[{stepName}] >>> 触发激活 <<<");

                        // 激活瞬间强制刷新 UI（避免节流导致用户看不到激活点）
                        NotifyRealTimeValue(stepKey, target, sample);
                        lastNotifyTick = currentTick;

                        // 测试开始回调
                        config.OnTestStarted?.Invoke(sample);
                    }

                    // 未激活前：只看数据不记录
                    if (!isTestActive)
                    {
                        // 同样做一次取消检查，避免边界竞态漏掉
                        if (loopToken.IsCancellationRequested)
                        {
                            userCanceled = token.IsCancellationRequested;
                            timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                            break;
                        }

                        try
                        {
                            await DelayRemaining(stopwatch, currentInterval, loopToken);
                        }
                        catch (OperationCanceledException)
                        {
                            userCanceled = token.IsCancellationRequested;
                            timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                            break;
                        }

                        continue;
                    }

                    // 5) 单样本校验（比如过滤异常值/毛刺）
                    if (config.PerSampleValidator != null && !config.PerSampleValidator(sample))
                    {
                        // 校验不通过：不计入策略样本，但仍要维持节奏
                        if (loopToken.IsCancellationRequested)
                        {
                            userCanceled = token.IsCancellationRequested;
                            timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                            break;
                        }

                        try
                        {
                            await DelayRemaining(stopwatch, currentInterval, loopToken);
                        }
                        catch (OperationCanceledException)
                        {
                            userCanceled = token.IsCancellationRequested;
                            timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                            break;
                        }

                        continue;
                    }

                    // 6) 复杂依赖回调：Valid 之后，Strategy 之前（你原来的关键位置）
                    config.OnSampleCollected?.Invoke(sample);

                    // 7) 策略处理：决定“是否完成”以及“是否收集当前样本”
                    var (isDone, shouldCollect) = config.Strategy.ProcessSample(sample, collectedSamples);

                    // 8) 样本收集：自动模式可能全量；流式模式只保留最后一个
                    if (shouldCollect)
                    {
                        if (config.SaveAllSamples)
                        {
                            collectedSamples.Add(sample);
                        }
                        else
                        {
                            // 流式模式：只保留最后一个，避免 OOM
                            collectedSamples.Clear();
                            collectedSamples.Add(sample);
                        }
                    }

                    // 9) 结束条件：策略完成 或 触发器停止
                    if (isDone || config.Trigger.ShouldStop(sample, isTestActive))
                    {
                        string reason = isDone ? "策略完成" : "触发器停止";
                        finalResult = BuildFinalResultClosedLoop(stepName, collectedSamples, config, reason, hasAnyValidSample, lastSample);
                        break;
                    }

                    // 关键：Delay 前再检查一次取消（取消常发生在迭代边界）
                    if (loopToken.IsCancellationRequested)
                    {
                        userCanceled = token.IsCancellationRequested;
                        timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                        break;
                    }

                    // 10) 智能延时：保证整体采样周期（扣除本轮耗时）
                    try
                    {
                        await DelayRemaining(stopwatch, currentInterval, loopToken);
                    }
                    catch (OperationCanceledException)
                    {
                        userCanceled = token.IsCancellationRequested;
                        timedOut = (!userCanceled) && (timeoutCts != null && timeoutCts.IsCancellationRequested);
                        break;
                    }
                }

                // ==========================
                // while 结束后的“闭环收口”
                // 重要：while 可能是“条件自然退出”，不抛异常，也没有在内部给 finalResult 赋值
                // 这里必须补齐：取消/超时/自然结束 都要生成 finalResult
                // ==========================

                // 强制刷新最后一帧 UI：优先 lastSample（最稳）
                if (hasAnyValidSample)
                {
                    NotifyRealTimeValue(stepKey, target, lastSample);
                }

                if (finalResult == null)
                {
                    // 判断退出原因：优先用户取消，其次超时，其它归为自然结束
                    if (!userCanceled && !timedOut)
                    {
                        // 如果 loopToken 被取消但我们没标记，补一次（边界竞态）
                        if (token.IsCancellationRequested) userCanceled = true;
                        else if (timeoutCts != null && timeoutCts.IsCancellationRequested) timedOut = true;
                    }

                    string reason;
                    if (userCanceled)
                    {
                        // 手动模式：停止通常视为“用户完成”
                        // 自动模式：停止通常视为“中止”
                        reason = (config.TotalTimeoutMs == Timeout.Infinite) ? "用户手动完成测试" : "用户中止测试";
                    }
                    else if (timedOut)
                    {
                        // 超时：但我们仍尽量算值（不污染调用者体验）
                        reason = "执行超时";
                    }
                    else
                    {
                        // 极少数情况：循环自然结束但未命中完成条件
                        reason = "循环自然结束(未命中完成条件)";
                    }

                    finalResult = BuildFinalResultClosedLoop(stepName, collectedSamples, config, reason, hasAnyValidSample, lastSample);
                }
            }
            catch (Exception ex)
            {
                // 闭环：任何异常都转成结果，不向外抛，避免污染上层
                _log?.Invoke($"[{stepName}] 致命错误: {ex.Message}");
                finalResult = ExeResult<T>.Failed($"Error: {ex.Message}", collectedSamples.AsReadOnly());
            }
            finally
            {
                // 理论上不会再走到 Unknown termination，但仍保留兜底 + 诊断信息
                finalResult ??= ExeResult<T>.Failed(
                    $"Unknown termination (userCancel={token.IsCancellationRequested}, timeoutMs={config.TotalTimeoutMs}, active={isTestActive}, samples={collectedSamples?.Count ?? 0})"
                );

                _log?.Invoke($"[{stepName}] 结束状态: {finalResult.Status}");

                // 回调：保证一定会触发（闭环）
                try
                {
                    config.OnTestFinished?.Invoke(finalResult);
                }
                catch (Exception cbEx)
                {
                    // 回调异常不影响主流程（避免污染调用者）
                    _log?.Invoke($"[{stepName}] OnTestFinished 回调异常: {cbEx.Message}");
                }

                // 释放资源
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }

            return finalResult;

            // =========================================================
            // 本函数内闭环用的“最终结果构建器”
            // 核心增强点：即使 samples 为空，只要拿到过 lastSample，也能算值
            // =========================================================
            ExeResult<T> BuildFinalResultClosedLoop(
                string sName,
                List<T> samples,
                SamplingConfig<T> cfg,
                string reasonText,
                bool hasLast,
                T last)
            {
                // 1) 选择用于计算的样本集合：优先 samples，其次 lastSample 兜底
                //    - 自动模式：samples 通常不为空
                //    - 手动流式：samples 可能被 Clear/尚未收集到
                //    - 边界：刚采到 lastSample，但策略还没来得及收集就超时/取消
                List<T> calcSamples = null;

                if (samples != null && samples.Count > 0)
                {
                    calcSamples = samples;
                }
                else if (hasLast)
                {
                    // 兜底：用最后一个有效样本构造一个临时集合用于计算
                    calcSamples = new List<T>(capacity: 1) { last };
                }
                else
                {
                    // 真的没有任何有效数据：只能失败（但给出明确原因）
                    return ExeResult<T>.Failed($"无有效数据 ({reasonText})");
                }

                // 2) 计算结果：尽量不抛异常，不污染调用者
                try
                {
                    // 使用策略计算最终值（对不同策略：平均/最大/最小/最后值 等）
                    var val = cfg.Strategy.CalculateResult(calcSamples);

                    // 规格判定：默认通过（没有 checker 就视为通过）
                    bool passed = cfg.SpecChecker?.Invoke(val) ?? true;

                    // 重要约定：
                    // - 执行是否成功（ExeResult.Success）与 规格是否通过（SpecPassed）分离
                    // - 超时/取消：我们仍尽量返回可计算的值，避免“污染调用者体验”
                    // - 但 spec 不通过要体现在 SpecPassed=false（由外层决定是否视为整体失败）
                    return ExeResult<T>.Success(
                        val,
                        // 注意：对外仍返回可读集合。若原 samples 为空，则返回只含 last 的集合
                        calcSamples.AsReadOnly(),
                        passed,
                        $"{reasonText}, 结果:{val}"
                    );
                }
                catch (Exception ex)
                {
                    // 结果计算失败：返回失败，但仍不给外层抛异常
                    return ExeResult<T>.Failed($"结果计算错误: {ex.Message}", calcSamples.AsReadOnly());
                }
            }
        }

        public async Task<ExeResult<T>> PerformSamplingAsyncOld<T>(
            string stepKey,
            string stepName,
            string target,
            string command,
            SamplingConfig<T> config,
            Func<string, IEnumerable<T>> responseParser,
            CancellationToken token)
        {
            ExeResult<T> finalResult = null;
            var collectedSamples = new List<T>();
            bool isTestActive = false;

            // UI 节流控制
            long lastNotifyTick = 0;
            const int UI_THROTTLE_MS = 100;

            try
            {
                // 验证配置 (此处会处理 Infinite 的情况)
                config.ValidateAndLogConfiguration(stepName);
                config.Trigger.Reset();

                // 融合 Token: 用户取消 + 超时 (Infinite = -1)
                using var timeoutCts = new CancellationTokenSource(config.TotalTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                var loopToken = linkedCts.Token;

                var stopwatch = new System.Diagnostics.Stopwatch();
                _log?.Invoke($"[{stepName}] 开始采样... 模式: {config.Strategy.StrategyName}");

                while (!loopToken.IsCancellationRequested)
                {
                    stopwatch.Restart(); // 仅用于 Loop 频率控制

                    // 1. 动态获取间隔
                    int currentInterval = config.DynamicIntervalProvider?.Invoke() ?? config.SampleIntervalMs;

                    // 2. 读取
                    T sample = await ReadSampleAsync(stepName, _protocolHandler, command, responseParser, _log, loopToken);

                    // 容错：空值不处理，直接等待下一轮
                    if (sample == null)
                    {
                        await DelayRemaining(stopwatch, currentInterval, loopToken);
                        continue;
                    }

                    // 3. UI 节流刷新
                    long currentTick = Environment.TickCount;
                    if (currentTick - lastNotifyTick > UI_THROTTLE_MS)
                    {
                        NotifyRealTimeValue(stepKey, target, sample);
                        lastNotifyTick = currentTick;
                    }

                    // 4. 触发器 (ImmediateTrigger 在此处发挥作用)
                    if (!isTestActive && config.Trigger.ShouldStart(sample, isTestActive))
                    {
                        isTestActive = true;
                        _log?.Invoke($"[{stepName}] >>> 触发激活 <<<");
                        NotifyRealTimeValue(stepKey, target, sample); // 激活瞬间强制刷新
                        lastNotifyTick = currentTick;

                        config.OnTestStarted?.Invoke(sample);
                    }

                    if (!isTestActive)
                    {

                        // 未激活状态下，只看数据不记录
                        await DelayRemaining(stopwatch, currentInterval, loopToken);
                        continue;
                    }

                    // 5. 单样本校验
                    if (config.PerSampleValidator != null && !config.PerSampleValidator(sample))
                    {
                        await DelayRemaining(stopwatch, currentInterval, loopToken);
                        continue;
                    }

                    // 6. [新特性 3] 复杂依赖回调 (关键位置：Valid 之后，Strategy 之前)
                    config.OnSampleCollected?.Invoke(sample);

                    // 7. 策略计算与存储
                    var (isDone, shouldCollect) = config.Strategy.ProcessSample(sample, collectedSamples);

                    if (shouldCollect)
                    {
                        if (config.SaveAllSamples)
                        {
                            collectedSamples.Add(sample);
                        }
                        else
                        {
                            // 流式模式：仅保留最后一个用于计算当前值，防止 OOM
                            collectedSamples.Clear();
                            collectedSamples.Add(sample);
                        }
                    }

                    // 8. 结束判断
                    if (isDone || config.Trigger.ShouldStop(sample, isTestActive))
                    {
                        string reason = isDone ? "策略完成" : "触发器停止";
                        finalResult = BuildFinalResult(stepName, collectedSamples, config, reason);
                        break;
                    }

                    // 9. 智能延时
                    await DelayRemaining(stopwatch, currentInterval, loopToken);
                }
                // 【修复：强制刷新最后一帧 UI】
                // 防止因为节流导致最后一次采样数据未显示在界面上
                // 使用 collectedSamples.Last() 或者在循环中记录一个 lastSample 变量
                if (collectedSamples != null && collectedSamples.Any())
                {
                    NotifyRealTimeValue(stepKey, target, collectedSamples.Last());
                }
            }
            catch (OperationCanceledException)
            {
                // [UI体验修复]：区分“异常取消”和“正常停止”
                // 情况 1: 用户手动点击停止 (Token 触发)
                if (token.IsCancellationRequested)
                {
                    // 关键逻辑：如果是手动模式，用户停止 = 成功
                    // 注意：这里我们需要知道当前是不是 Manual 模式。
                    // 由于 Config 里没有存 Mode，我们可以通过 TotalTimeoutMs == -1 (Infinite) 来间接判断，
                    // 或者最简单的：只要是用户主动点的停止，都算作“Succeeded”，只是消息不同。

                    string endMsg = config.TotalTimeoutMs == Timeout.Infinite
                        ? "用户手动完成测试"  // 手动模式文案
                        : "用户中止测试";     // 自动模式文案

                    // 这里调用 BuildFinalResult 时，我们需要一种机制告诉它“不要标记为失败”
                    // 技巧：只要 Strategy 能算出值，BuildFinalResult 默认就会返回 Success (除非规格检查没过)
                    finalResult = BuildFinalResult(stepName, collectedSamples, config, endMsg);

                    // 强行修正状态：如果 BuildFinalResult 因为某些原因判负了，但我们认为手动停止算成功
                    // (取决于你的业务：手动停止时，如果数据超规，算成功还是失败？通常算执行成功，数据不合格)
                    // 此处保持 BuildFinalResult 的逻辑即可，它会根据 specChecker 判断 Success/Failed。
                    // 但如果只是为了让“执行状态”为 Success，可以不做额外操作，只要 BuildFinalResult 不抛异常即可。
                }
                // 情况 2: 真正的超时 (timeoutCts 触发)
                else
                {
                    finalResult = BuildFinalResult(stepName, collectedSamples, config, "执行超时");
                    // 超时通常是 Fail
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[{stepName}] 致命错误: {ex.Message}");
                finalResult = ExeResult<T>.Failed($"Error: {ex.Message}", collectedSamples.AsReadOnly());
            }
            finally
            {
                finalResult ??= ExeResult<T>.Failed("Unknown termination");
                _log?.Invoke($"[{stepName}] 结束状态: {finalResult.Status}");
                config.OnTestFinished?.Invoke(finalResult);
            }

            return finalResult;
        }

        // --- Helpers ---
        private async Task DelayRemaining(System.Diagnostics.Stopwatch sw, int interval, CancellationToken token)
        {
            sw.Stop();
            int delay = interval - (int)sw.ElapsedMilliseconds;
            if (delay > 0) await Task.Delay(delay, token);
        }

        private void NotifyRealTimeValue<T>(string stepKey, string target, T sample)
        {
            // 注意：Events 最好在 UI 层做 Throttling (节流)，防止高频刷新卡死 UI
            if (sample is double d) TestEvents.RealTimeValueChanged?.Invoke(stepKey, target, d);
            else if (sample is IConvertible c) TestEvents.RealTimeValueChanged?.Invoke(stepKey, target, c.ToDouble(null));
        }

        private async Task<T> ReadSampleAsync<T>(string stepName, IProtocolHandler handler, string cmd, Func<string, IEnumerable<T>> parser, Action<string> log, CancellationToken token)
        {
            try
            {
                handler.ResetReceiveBuffer();
                await handler.SendMessageAsync(cmd, token);
                var bytes = await handler.ReceiveMessageAsync(token);
                if (bytes == null || bytes.Length == 0) return default;
                var str = Encoding.ASCII.GetString(bytes).Trim();
                if (string.IsNullOrWhiteSpace(str)) return default;
                return parser(str).FirstOrDefault();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return default; }
        }
        private ExeResult<T> BuildFinalResult<T>(string stepName, List<T> samples, SamplingConfig<T> config, string reason)
        {
            // [修复]：手动模式下点击停止，可能正好 list 是空的（刚启动就停，或者流式处理没存）
            // 这种情况下不应该报 Failed，应该给一个默认成功的 Result
            if (samples == null || !samples.Any())
            {
                // 如果原因是“用户手动完成”，且数据为空，我们返回一个“无数据的成功”
                if (reason.Contains("用户手动"))
                {
                    return ExeResult<T>.Success(default, new List<T>().AsReadOnly(), true, $"{reason} (无数据)");
                }
                return ExeResult<T>.Failed($"无有效数据 ({reason})");
            }

            try
            {
                var val = config.Strategy.CalculateResult(samples);
                bool passed = config.SpecChecker?.Invoke(val) ?? true;

                // 即使 spec 没过，ExeResult 也可以是 Success (代表执行成功)，只是 SpecPassed = false
                return ExeResult<T>.Success(val, samples.AsReadOnly(), passed, $"{reason}, 结果:{val}");
            }
            catch (Exception ex)
            {
                return ExeResult<T>.Failed($"结果计算错误: {ex.Message}");
            }
        }
    }
}