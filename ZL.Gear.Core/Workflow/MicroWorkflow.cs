using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 一个轻量级、健壮的异步微工作流构建器。
    /// 专为构建和执行短暂的、线性的、内存中的异步任务链而设计，非常适用于设备测试步骤的编排。
    /// </summary>
    public sealed class MicroWorkflow : IAsyncDisposable
    {
        private readonly StepConfig _step;
        private StepContext _context;
        private readonly ConcurrentBag<Measurement> _measurements = new ConcurrentBag<Measurement>();
        private Task<ExecutionResultBase> _executionChain;
        private readonly ConcurrentBag<ActionDelegate> _cleanupActions = new ConcurrentBag<ActionDelegate>();
        private Action<string> Log { get; }
        /// <summary>
        /// 标记是否开启严格检查（必须有测量数据）
        /// </summary>
        private bool _requireMeasurements = false;
        /// <summary>
        /// 流程级超时（毫秒），为空表示不启用流程级超时。
        /// </summary>
        private int? _workflowTimeoutMs;
        /// <summary>
        /// 流程级超时使用的 CancellationTokenSource，用于 DisposeAsync 时释放。
        /// </summary>
        private CancellationTokenSource _workflowTimeoutCts;

        private MicroWorkflow(StepConfig step, StepContext context)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            Log = _context.Log ?? (s => { });
            // 初始化执行链，始于一个成功的状态
            _executionChain = Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
        }
        /// <summary>
        /// 启动一个新的微工作流实例。
        /// </summary>
        public static MicroWorkflow Start(StepConfig step, StepContext context)
        {
            return new MicroWorkflow(step, context);
        }

        /// <summary>
        /// 启动一个新的微工作流实例，并显式指定流程级超时时间（毫秒）。
        /// </summary>
        /// <param name="step">步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <param name="workflowTimeoutMs">流程级超时毫秒数，小于等于 0 表示不启用流程级超时。</param>
        /// <returns>微工作流实例。</returns>
        public static MicroWorkflow Start(StepConfig step, StepContext context, int workflowTimeoutMs)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var workflow = new MicroWorkflow(step, context);
            if (workflowTimeoutMs > 0)
            {
                workflow._workflowTimeoutMs = workflowTimeoutMs;
                workflow._context = workflow._context.WithToken(CreateWorkflowTimeoutToken(workflow, context, workflowTimeoutMs));
            }

            return workflow;
        }

        /// <summary>
        /// 为流程级超时创建链接令牌，不覆盖原始上下文令牌。
        /// </summary>
        private static CancellationToken CreateWorkflowTimeoutToken(MicroWorkflow workflow, StepContext context, int workflowTimeoutMs)
        {
            var workflowTimeoutCts = new CancellationTokenSource(workflowTimeoutMs);
            workflow._workflowTimeoutCts = workflowTimeoutCts;
            return CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, workflowTimeoutCts.Token).Token;
        }

        /// <summary>
        /// 创建子流程，继承当前工作流的流程级超时设置（若有）。
        /// </summary>
        private MicroWorkflow CreateChildWorkflow(StepConfig step, StepContext context)
        {
            if (_workflowTimeoutMs.HasValue)
            {
                return Start(step, context, _workflowTimeoutMs.Value);
            }

            return Start(step, context);
        }

        /// <summary>
        /// 创建子流程并显式携带父级超时（供外部辅助类复用，避免重复逻辑）。
        /// </summary>
        /// <param name="step">步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <param name="parentWorkflowTimeoutMs">父级流程级超时，为空则沿用上下文。</param>
        public static MicroWorkflow StartChildWithTimeout(StepConfig step, StepContext context, int? parentWorkflowTimeoutMs)
        {
            if (parentWorkflowTimeoutMs.HasValue && parentWorkflowTimeoutMs.Value > 0)
            {
                return Start(step, context, parentWorkflowTimeoutMs.Value);
            }

            return Start(step, context);
        }
        /// <summary>
        /// 向工作流链中添加一个通用的执行步骤。
        /// 如果前一步失败，此步骤将被跳过。
        /// </summary>
        /// <param name="description">此步骤的可读描述。</param>
        /// <param name="action">要执行的异步操作。</param>
        public MicroWorkflow Then(string description, ActionDelegate action)
        {
            _executionChain = _executionChain.ContinueWith(async prevTask =>
            {
                var prevResult = await prevTask.ConfigureAwait(false);
                // 实现短路逻辑
                if (!prevResult.Success) return prevResult;
                _context.Log($"-> {description}");
                try
                {
                    // 执行动作
                    return await action(_step, _context).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _context.Log($"-- 操作 '{description}' 被取消。");
                    return ExecutionResult.Failed($"操作 '{description}' 被取消。");
                }
                catch (Exception ex)
                {
                    _context.Log($"[异常] 执行 '{description}' 发生错误: {ex.Message}");
                    return ExecutionResult.Failed($"执行 '{description}' 异常: {ex.Message}");
                }
            }, TaskContinuationOptions.NotOnCanceled).Unwrap();
            return this;
        }
        /// <summary>
        /// 向工作流链中添加一个专门用于测量的步骤。
        /// 此方法执行测量，将成功的测量结果存入内部集合，然后向主流程返回一个通用的成功/失败结果用于流程控制。
        /// </summary>
        /// <param name="description">此测量步骤的描述。</param>
        /// <param name="measurementAction">要执行的测量操作。</param>
        public MicroWorkflow ThenMeasure(string description, MeasurementActionDelegate measurementAction)
        {
            _requireMeasurements = true;
            ActionDelegate wrappedAction = async (step, context) =>
            {
                var measurement = await measurementAction(step, context).ConfigureAwait(false);

                // 假设 Measurement 类有 Success 和 Message 属性来判断其自身状态
                if (measurement.Success)
                {
                    _measurements.Add(measurement);
                    // 向主执行链返回【成功】信号，让流程继续
                    return ExecutionResult.Succeeded();
                }
                else
                {
                    // 向主执行链返回【失败】信号，这将导致后续步骤短路
                    return ExecutionResult.Failed(measurement.Message);
                }
            };
            return Then(description, wrappedAction);
        }
        /// <summary>
        /// 根据条件决定是否执行一段工作流分支。
        /// </summary>
        /// <param name="condition">如果为 true，则执行分支。</param>
        /// <param name="conditionalWorkflow">定义了条件分支的工作流配置函数。</param>
        public MicroWorkflow If(bool condition, Func<MicroWorkflow, MicroWorkflow> conditionalWorkflow)
        {
            if (condition) return conditionalWorkflow(this);
            return this;
        }

        /// <summary>
        /// 根据条件决定是否执行一段工作流分支（布尔表达式重载）。
        /// </summary>
        /// <param name="condition">如果为 true，则执行分支。</param>
        /// <param name="conditionalWorkflow">定义了条件分支的工作流配置函数。</param>
        public MicroWorkflow If(Func<StepContext, bool> condition, Func<MicroWorkflow, MicroWorkflow> conditionalWorkflow)
        {
            if (condition(_context)) return conditionalWorkflow(this);
            return this;
        }
        /// <summary>
        /// 注册一个清理操作，该操作将在工作流结束时（无论成功或失败）被执行。
        /// 多个 Finally 块将以注册的相反顺序执行（类似栈的 LIFO）。
        /// </summary>
        /// <param name="description">清理操作的描述。</param>
        /// <param name="cleanupAction">要执行的清理操作。</param>
        public MicroWorkflow Finally(string description, ActionDelegate cleanupAction)
        {
            // 注意：ConcurrentBag 是无序的，如果需要严格的 LIFO 顺序，应使用 ConcurrentStack
            // 但对于独立的清理任务（如停止不同任务），顺序通常不重要。
            _cleanupActions.Add(async (s, c) =>
            {
                c.Log($"-> 清理: {description}");
                await cleanupAction(s, c).ConfigureAwait(false);
                // 清理操作的结果通常不影响最终的工作流结果
                return ExecutionResult.Succeeded();
            });
            return this;
        }
        /// <summary>
        /// 向工作流中添加一个延时步骤。
        /// </summary>
        public MicroWorkflow Delay(int milliseconds, string? description = null)
        {
            var desc = description ?? $"延时 {milliseconds}ms";
            return Then(desc, async (s, c) =>
            {
                await Task.Delay(milliseconds, c.CancellationToken).ConfigureAwait(false);
                return ExecutionResult.Succeeded();
            });
        }

        // --- 主从协调部分 ---
        public MicroWorkflow AsMaster()
        {
            if (!_step.IsMaster) return this;
            return Then("初始化主步骤信令", (s, c) =>
            {
                if (!c.Variables.TryGetSignalPair(_step.StepKey, out var signals))
                {
                    return Task.FromResult<ExecutionResultBase>(
                        ExecutionResult.Failed($"配置错误：主步骤的信令对象未被正确初始化 '{_step.StepKey}'。"));
                }
                Finally("确保信令被处理", (step, context) =>
                {
                    context.Log($"主步骤 {_step.StepName} 结束，通知所有从属步骤停止。");
                    signals.EndSignalCts?.Cancel();
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
                });
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
            });
        }
        public MicroWorkflow SignalStart()
        {
            if (!_step.IsMaster) return this;
            return Then("发送开始信号", (s, c) =>
            {
                if (c.Variables.TryGetSignalPair(_step.StepKey, out var signals))
                {
                    c.Log($"[信令] 通知从属步骤开始...");
                    signals.StartSignal.TrySetResult(true);
                    return Task.FromResult<ExecutionResultBase>(ExecutionResult.Succeeded());
                }
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed("发送开始信号失败：无法找到信令对。"));
            });
        }
        #region --- WaitUntil 轮询等待 ---

        /// <summary>
        /// 【新增】轮询等待，直到条件满足或超时。
        /// 适用于：气缸到位检测、传感器感应、等待PLC信号。
        /// 轮询等待，直到条件函数返回 true。如果超时则流程失败。“阻塞当前流程，死等直到条件为真”。如果超时 -> 步骤失败，整个流程终止（抛出失败结果）。
        /// </summary>
        public MicroWorkflow WaitUntil(string description, Func<StepConfig, StepContext, Task<bool>> condition, int timeoutMs = 5000, int pollIntervalMs = 100)
        {
            return Then(description, async (step, ctx) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    if (ctx.CancellationToken.IsCancellationRequested)
                        return ExecutionResult.Failed("等待被取消");

                    bool isReady = false;
                    try
                    {
                        // 执行条件检查
                        isReady = await condition(step, ctx);
                    }
                    catch (Exception ex)
                    {
                        // 轮询中的异常通常意味着设备还没准备好（如串口未连接），建议记录但不中断
                        // 也可以选择 throw 出来中断，视业务策略而定
                        // ctx.Log($"[Wait] Check error: {ex.Message}");
                    }

                    if (isReady)
                    {
                        return ExecutionResult.Succeeded(); // 只有这里返回成功，流程才会继续
                    }

                    await Task.Delay(pollIntervalMs, ctx.CancellationToken);
                }
                return ExecutionResult.Failed($"等待超时 ({timeoutMs}ms): {description}");
            });
        }

        #endregion

        /// <summary>
        /// 【循环结构】当条件为真时，重复执行某一段子流程。场景：拧紧工位，需要连续拧 4 个螺丝，或者直到扫码成功为止。
        /// </summary>
        public MicroWorkflow While(Func<StepContext, bool> condition, Func<MicroWorkflow, MicroWorkflow> loopBodyBuilder)
        {
            // 这里的实现有点技巧，因为 MicroWorkflow 是线性的。
            // 本质上是把 While 作为一个特殊的 Step，内部递归执行。
            return Then("循环结构", async (step, ctx) =>
            {
                int loopCount = 0;
                while (condition(ctx)) // 检查循环条件
                {
                    loopCount++;
                    if (ctx.CancellationToken.IsCancellationRequested) return ExecutionResult.Failed("循环取消");

                    // 动态构建子流程
                    // 注意：这里需要创建一个新的 MicroWorkflow 实例或者复用机制
                    // 为了简单起见，我们假设 loopBodyBuilder 只是向当前的 flow 追加是行不通的
                    // 我们需要执行一个子 WorkFlow

                    // 使用子上下文隔离环境
                    var subContext = ctx.CreateChildContext(step);
                    await using (var subFlow = CreateChildWorkflow(step, subContext))
                    {
                        loopBodyBuilder(subFlow); // 用户定义循环体
                        var result = await subFlow.GetResultAsync();

                        if (!result.Success) return result; // 如果循环体内部失败，跳出
                    }
                }
                return ExecutionResult.Succeeded($"循环结束，共执行 {loopCount} 次");
            });
        }
        /// <summary>
        /// 【多路分支】根据上下文的值，选择不同的路径执行。场景：同一个工位混线生产，ModelA 测电压，ModelB 测电阻，ModelC 跳过。
        /// </summary>
        public MicroWorkflow Switch<T>(Func<StepContext, T> selector, Action<SwitchBuilder<T>> buildSwitch)
        {
            return Then("分支判断", async (step, ctx) =>
            {
                var value = selector(ctx);
                var builder = new SwitchBuilder<T>(value, step, ctx, _workflowTimeoutMs);
                buildSwitch(builder); // 用户配置 Case

                return await builder.ExecuteSelectedAsync();
            });
        }

        /// <summary>
        /// 【容错执行】尝试执行动作，即使失败也只记录日志，不中断流程。场景：上传辅助日志、蜂鸣器提示。如果失败了，不应该阻断主流程。
        /// </summary>
        public MicroWorkflow BestEffort(string description, string actionName)
        {
            return Then($"{description} (忽略错误)", async (step, ctx) =>
            {
                try
                {
                    var action = ctx.ActionResolver.ResolveAction(actionName);
                    var result = await action(step, ctx);
                    if (!result.Success)
                    {
                        ctx.Log($"[警告] 忽略非关键错误: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    ctx.Log($"[警告] 忽略非关键异常: {ex.Message}");
                }
                // 永远返回成功
                return ExecutionResult.Succeeded();
            });
        }

        /// <summary>
        /// 【新增】显式声明：此流程预期必须产生测量数据。
        /// 如果流程跑完后测量集合为空，将视为失败。
        /// </summary>
        public MicroWorkflow ExpectMeasurements()
        {
            _requireMeasurements = true;
            return this;
        }

        /// <summary>
        /// 为当前工作流设置流程级超时（毫秒）。
        /// 设置后，<see cref="GetResultAsync"/> 会在整体超时时返回失败结果，
        /// 同时子流程也会继承该超时设置。
        /// </summary>
        /// <param name="timeoutMs">超时毫秒数，小于等于 0 表示不启用流程级超时。</param>
        public MicroWorkflow WorkflowTimeout(int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                return this;
            }

            _workflowTimeoutMs = timeoutMs;
            // 替换当前上下文令牌，链接原令牌与流程级超时令牌
            _context = _context.WithToken(CreateWorkflowTimeoutToken(this, _context, timeoutMs));
            return this;
        }

        #region --- Retry 重试机制 ---

        /// <summary>
        /// 【基础核心】重试逻辑实现
        /// </summary>
        public MicroWorkflow Retry(string description, ActionDelegate action, int maxRetries = 3, int delayBetweenRetriesMs = 500)
        {
            return Then(description, async (step, ctx) =>
            {
                int attempt = 0;
                // 循环直到 maxRetries
                while (attempt < maxRetries)
                {
                    attempt++;
                    // 尝试执行
                    var result = await action(step, ctx).ConfigureAwait(false);

                    // 成功则立即返回
                    if (result.Success) return result;

                    // 最后一次尝试失败，直接返回错误
                    if (attempt == maxRetries)
                    {
                        return ExecutionResult.Failed($"[{description}] 重试 {maxRetries} 次后依然失败: {result.Message}");
                    }

                    // 失败但还有机会，记录日志并等待
                    ctx.Log($"[重试] ({attempt}/{maxRetries}) 失败: {result.Message}。等待 {delayBetweenRetriesMs}ms...");

                    // 检查取消信号，防止重试过程中无法退出
                    if (ctx.CancellationToken.IsCancellationRequested)
                        return ExecutionResult.Failed("重试过程被取消");

                    await Task.Delay(delayBetweenRetriesMs, ctx.CancellationToken).ConfigureAwait(false);
                }
                return ExecutionResult.Failed("重试逻辑异常终结");
            });
        }
        #endregion
        /// <summary>
        /// 【新增】并行执行多个任务，等待所有任务完成（WhenAll）。
        /// 只要有一个失败，整体即视为失败。
        /// </summary>
        public MicroWorkflow Parallel(string description, params (string subDesc, ActionDelegate action)[] tasks)
        {
            return Then(description, async (step, ctx) =>
            {
                var runningTasks = tasks.Select(async t =>
                {
                    try
                    {
                        ctx.Log($"[并行] 启动: {t.subDesc}");
                        return await t.action(step, ctx);
                    }
                    catch (Exception ex)
                    {
                        return ExecutionResult.Failed($"并行任务 '{t.subDesc}' 异常: {ex.Message}");
                    }
                }).ToList();

                var results = await Task.WhenAll(runningTasks);

                // 聚合结果
                var failures = results.Where(r => !r.Success).ToList();
                if (failures.Any())
                {
                    return ExecutionResult.Failed($"并行执行中有 {failures.Count} 个任务失败: {failures.First().Message}");
                }

                return ExecutionResult.Succeeded();
            });
        }
        /// <summary>
        /// 【新增】并行执行多个测量任务。
        /// 所有任务同时启动，任何一个失败则整体失败。
        /// 成功的测量结果会自动加入到结果集合中。
        /// </summary>
        /// <param name="description">步骤组描述</param>
        /// <param name="tasks">并行任务列表 (子描述, 测量委托)</param>
        public MicroWorkflow ParallelMeasure(string description, params (string subDesc, MeasurementActionDelegate action)[] tasks)
        {
            // 1. 既然调用了测量，自动开启严格模式（兼容之前的逻辑）
            _requireMeasurements = true;

            return Then(description, async (step, ctx) =>
            {
                ctx.Log($"-> 开始并行测量: {description} ({tasks.Length}个分支)");

                // 2. 包装并启动所有任务
                var runningTasks = tasks.Select(async t =>
                {
                    try
                    {
                        // ctx.Log($"[并行] 启动: {t.subDesc}"); // 可选日志，太多分支可能会刷屏

                        // 执行测量
                        var measurement = await t.action(step, ctx).ConfigureAwait(false);

                        // 3. 处理单个结果
                        if (measurement.Success)
                        {
                            // ConcurrentBag 是线程安全的，直接添加即可
                            _measurements.Add(measurement);
                            return ExecutionResult.Succeeded();
                        }
                        else
                        {
                            // 只要失败，返回失败结果
                            return ExecutionResult.Failed($"[{t.subDesc}] 测量失败: {measurement.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        return ExecutionResult.Failed($"[{t.subDesc}] 异常: {ex.Message}");
                    }
                }).ToList();

                // 4. 等待所有任务完成 (WhenAll)
                // 注意：即使有任务失败，我们也等待所有任务跑完（或者抛出异常），确保资源释放
                var results = await Task.WhenAll(runningTasks).ConfigureAwait(false);

                // 5. 聚合结果
                // 检查是否有任何一个分支返回了 Failed
                var failures = results.Where(r => !r.Success).ToList();
                if (failures.Any())
                {
                    // 拼接所有错误信息
                    var errAction = failures.First().Message;
                    return ExecutionResult.Failed($"并行测量中有 {failures.Count} 个任务失败。首个错误: {errAction}");
                }

                return ExecutionResult.Succeeded();
            });
        }
        /// <summary>
        /// 异步执行整个工作流链，并返回最终结果。
        /// 如果流程成功，将使用收集到的测量数据进行最终的聚合裁决。
        /// checkVal 是否做结果判断
        /// </summary>
        public async Task<ExecutionResultBase> GetResultAsync()
        {
            return await GetResultAsyncInternal().ConfigureAwait(false);
        }

        private async Task<ExecutionResultBase> GetResultAsyncInternal()
        {
            // 流程级超时保护：使用 Task.WhenAny 确保即使执行链内部未观察令牌也能被整体超时兜底
            if (_workflowTimeoutMs.HasValue)
            {
                if (_workflowTimeoutCts == null)
                {
                    _workflowTimeoutCts = new CancellationTokenSource(_workflowTimeoutMs.Value);
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_context.CancellationToken, _workflowTimeoutCts.Token);

                var chainTask = _executionChain;
                var delayTask = Task.Delay(Timeout.Infinite, linkedCts.Token);

                var completed = await Task.WhenAny(chainTask, delayTask).ConfigureAwait(false);
                if (completed == delayTask)
                {
                    return ExecutionResult.Failed($"MicroWorkflow 流程级超时 ({_workflowTimeoutMs.Value}ms)。");
                }

                return await chainTask;
            }

            var finalResult = await _executionChain.ConfigureAwait(false);
            if (!finalResult.Success)
            {
                Log?.Invoke($"步骤【{_step.StepName}】在MicroWorkflow中执行失败 ，{finalResult.Message}");
                return finalResult; // 如果中途失败，直接返回失败结果
            }
            // 流程成功，使用聚合逻辑对收集到的测量结果做最终判断
            var ms = _measurements.ToList();

            // 2. 智能校验
            // 只有当 (自动开启了严格模式 AND 结果为空) 时，才报错
            if (_requireMeasurements && ms.Count == 0)
            {
                // 场景：代码里写了 ThenMeasure，但是因为逻辑错误（如 If 跳过）导致根本没执行，
                // 这里会拦截住，防止误报成功。
                return ExecutionResult.Failed("流程异常：预期的测量步骤未产生任何数据。");
            }

            // 3. 结果裁决
            return Judge(ms);
        }

        public static ExecutionResultBase Judge(List<Measurement> measurements)
        {
            if (measurements == null || measurements.Count == 0) return ExecutionResult.Succeeded();

            // 检查是否有任何一次测量失败
            if (measurements.Any(m => !m.Success))
            {
                var failedMessages = measurements.Where(m => !m.Success).Select(m => m.Message);
                return ExecutionResult<List<Measurement>>.Failed($"一个或多个测量失败: {string.Join("; ", failedMessages)}", measurements);
            }

            // 所有操作都成功
            return ExecutionResult<List<Measurement>>.Succeeded(measurements);
        }
        /// <summary>
        /// 异步释放资源，并确保所有注册的 Finally 清理操作都得到执行。
        /// 这是 `await using` 语法的关键。
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                // 等待主流程结束（无论成功失败）
                await _executionChain.ConfigureAwait(false);
            }
            catch
            {
                // 忽略主流程等待的异常，确保 Finally 能运行
            }
            finally
            {
                // 释放流程级超时使用的 CancellationTokenSource
                if (_workflowTimeoutCts != null)
                {
                    _workflowTimeoutCts.Dispose();
                    _workflowTimeoutCts = null;
                }
            }

            foreach (var cleanupAction in _cleanupActions)
            {
                try
                {
                    // 即使 cleanupAction 内部错误地使用了 context.CancellationToken 也不能让它阻断后续的 cleanupAction
                    await cleanupAction(_step, _context).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 捕获：如果 Action 内部因为 Token 取消而抛出异常，记录警告
                    // 这意味着这个清理动作可能没执行成功，因为代码里写了 ThrowIfCancellationRequested
                    _context.Log($"[严重警告] 清理操作 '{cleanupAction.Method.Name}' 因取消令牌而被跳过，请检查 Action 代码实现！");
                }
                catch (Exception ex)
                {
                    _context.Log($"[警告] 清理操作失败: {ex.Message}");
                }
            }
        }

        #region --- 便利重载 (封装工厂逻辑) ---
        /// <summary>
        /// 通过名称添加一个通用的执行步骤。
        /// 内部会自动调用 StepHandlerFactory.GetAction()。
        /// </summary>
        /// <param name="description">此步骤的可读描述。</param>
        /// <param name="actionName">在 StepHandlerFactory 中注册的操作名称。</param>
        public MicroWorkflow Then(string description, string actionName)
        {
            var actionDelegate = _context.ActionResolver.ResolveAction(actionName);
            // 调用原始的、接受委托的 Then 方法
            return Then(description, actionDelegate);
        }

        /// <summary>
        /// 通过名称查找动作并重试。这是为了支持 .Retry("Desc", "ActionName") 写法
        /// </summary>
        public MicroWorkflow Retry(string description, string actionName, int maxRetries = 3, int delayBetweenRetriesMs = 500)
        {
            // 1. 动态解析动作 (利用 Context 中的 Resolver)
            var actionDelegate = _context.ActionResolver.ResolveAction(actionName);

            // 2. 调用核心逻辑
            return Retry(description, actionDelegate, maxRetries, delayBetweenRetriesMs);
        }

        /// <summary>
        /// 通过名称添加一个专门用于测量的步骤。
        /// 内部会自动调用 StepHandlerFactory.GetMeasurementAction()。
        /// </summary>
        /// <param name="description">此测量步骤的描述。</param>
        /// <param name="actionName">在 StepHandlerFactory 中注册的测量操作名称。</param>
        public MicroWorkflow ThenMeasure(string description, string actionName)
        {
            // 从工厂获取正确的委托
            var measurementDelegate = _context.ActionResolver.ResolveMeasurement(actionName);
            // 调用原始的、接受委托的 ThenMeasure 方法
            return ThenMeasure(description, measurementDelegate);
        }

        /// <summary>
        /// 符串重载 (为了方便调用)Parallel = 多线程做事（不关心返回值，只关心是否报错）。
        /// </summary>
        /// <param name="description"></param>
        /// <param name="tasks"></param>
        /// <returns></returns>
        public MicroWorkflow Parallel(string description, params (string subDesc, string actionName)[] tasks)
        {
            var delegateTasks = tasks.Select(t =>
            {
                // 解析的是普通 Action (无返回值)
                var action = _context.ActionResolver.ResolveAction(t.actionName);
                return (t.subDesc, action);
            }).ToArray();

            return Parallel(description, delegateTasks);
        }

        /// <summary>
        /// 【新增】并行测量的字符串重载版本。ParallelMeasure = 多线程收数据（关心返回值，必须收集到数据）
        /// </summary>
        /// <param name="description">总描述</param>
        /// <param name="tasks">并行任务列表 (子描述, 动作名称)</param>
        public MicroWorkflow ParallelMeasure(string description, params (string subDesc, string actionName)[] tasks)
        {
            // 1. 解析所有字符串为委托
            var delegateTasks = tasks.Select(t =>
            {
                // 利用 Context 中的解析器查找动作
                var action = _context.ActionResolver.ResolveMeasurement(t.actionName);
                return (t.subDesc, action);
            }).ToArray();

            // 2. 调用核心实现
            return ParallelMeasure(description, delegateTasks);
        }

        /// <summary>
        /// 通过名称注册一个清理操作。
        /// 内部会自动调用 StepHandlerFactory.GetAction()。
        /// </summary>
        /// <param name="description">清理操作的描述。</param>
        /// <param name="actionName">在 StepHandlerFactory 中注册的操作名称。</param>
        public MicroWorkflow Finally(string description, string actionName)
        {
            // 从工厂获取正确的委托
            var cleanupDelegate = _context.ActionResolver.ResolveAction(actionName);

            // 调用原始的、接受委托的 Finally 方法
            return Finally(description, cleanupDelegate);
        }
        #endregion
    }
    /// <summary>
    /// 辅助类，用于构建流式 API
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SwitchBuilder<T>
    {
        private readonly T _value;
        private readonly StepConfig _step;
        private readonly StepContext _ctx;
        private readonly int? _parentWorkflowTimeoutMs;
        private Func<Task<ExecutionResultBase>> _matchedAction = null;

        public SwitchBuilder(T value, StepConfig step, StepContext ctx, int? parentWorkflowTimeoutMs)
        {
            _value = value;
            _step = step;
            _ctx = ctx;
            _parentWorkflowTimeoutMs = parentWorkflowTimeoutMs;
        }

        public void Case(T compareValue, Func<MicroWorkflow, MicroWorkflow> branchFlow)
        {
            // 如果已经匹配过，或者当前值不匹配，直接跳过
            if (_matchedAction != null || !EqualityComparer<T>.Default.Equals(_value, compareValue)) return;

            _matchedAction = async () =>
            {
                _ctx.Log($"[Switch] 命中分支: {compareValue}");
                // 执行子流程
                var subContext = _ctx.CreateChildContext(_step);
                await using var subFlow = MicroWorkflow.StartChildWithTimeout(_step, subContext, _parentWorkflowTimeoutMs);
                branchFlow(subFlow);
                return await subFlow.GetResultAsync();
            };
        }

        public void Default(Func<MicroWorkflow, MicroWorkflow> branchFlow)
        {
            if (_matchedAction == null)
            {
                _matchedAction = async () =>
                {
                    _ctx.Log($"[Switch] 进入默认分支");
                    var subContext = _ctx.CreateChildContext(_step);
                    await using var subFlow = MicroWorkflow.StartChildWithTimeout(_step, subContext, _parentWorkflowTimeoutMs);
                    branchFlow(subFlow);
                    return await subFlow.GetResultAsync();
                };
            }
        }

        internal async Task<ExecutionResultBase> ExecuteSelectedAsync()
        {
            if (_matchedAction != null) return await _matchedAction();
            return ExecutionResult.Succeeded("无匹配分支，跳过");
        }
    }
}
/*
 * =================================================================================
 * 使用示例 (Handler 内部):
 * =================================================================================
 *
 * public class MyTestHandler : IStepHandler
 * {
 *     public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
 *     {
 *         // 使用 await using 确保 DisposeAsync() 被调用，从而执行所有 finally 操作。
 *         await using (var workflow = Workflow.Start(step, context))
 *         {
 *             // 通过链式调用来编排测试流程
 *             workflow.AsMaster() // 如果是主步骤
 *                 .Then("第一步：设备上电", StepHandlerKit.PowerOnDevice(device: "PowerSupply-1"))
 *                 .Delay(500, "等待设备稳定")
 *                 .If(step.Parameters.Get<bool>("needsCalibration"), wf => wf
 *                     .Then("执行校准", StepHandlerKit.CalibrateSensor(sensor: "Sensor-A"))
 *                 )
 *                 .SignalStart() // 通知从属步骤
 *                 .Then("第二步：进行测量", StepHandlerKit.TakeMeasurement(sensor: "Sensor-A"))
 *                 .Finally("最后：设备断电", StepHandlerKit.PowerOffDevice(device: "PowerSupply-1"));
 *
 *             // 调用 GetResultAsync() 来等待整个流程完成并获取最终结果。
 *             return await workflow.GetResultAsync();
 *         }
 *     }
 * }
 *
 场景 1：使用 Parallel 进行批量初始化 (无数据)
目的：为了节省时间，我希望气缸复位、清理PLC、打开屏蔽箱这三件事同时做，而不是一件件排队。
await using (var flow = MicroWorkflow.Start(step, ctx))
{
    flow
        // 这里使用 Parallel
        // 只是执行动作，不产生数据，不会触发“严格模式”
        .Parallel("设备初始化",
            ("复位气缸", "ResetCylinder"),
            ("清理PLC寄存器", "ClearPlc"),
            ("打开屏蔽箱", "OpenDoor")
        )
        .Delay(500, "等待机构到位");

    // 结果：Success (因为 Parallel 不强制要求 measurement)
    return await flow.GetResultAsync();
}

场景 2：使用 ParallelMeasure 进行多通道测试 (有数据)
目的：设备就绪了，我要同时读取电压和电流。
await using (var flow = MicroWorkflow.Start(step, ctx))
{
    flow
        .Then("闭合继电器", "RelayOn")
        .Delay(200)
        
        // 这里使用 ParallelMeasure
        // 会产生 Measurement 对象，并自动开启“严格模式”
        .ParallelMeasure("采集板卡数据",
            ("读取电压值", "ReadVoltage"),
            ("读取电流值", "ReadCurrent")
        );

    // 结果：Success (前提是两个测量都产生数据)
    return await flow.GetResultAsync();
}

场景 3：混合使用 (最常见的真实业务)
目的：一边让气缸动作（动作），一边读取传感器看变化（测量）。虽然不建议在一个Parallel里混用，但可以在流程中先后使用。
await using (var flow = MicroWorkflow.Start(step, ctx))
{
    flow
        // 1. 先并行做准备工作 (纯动作)
        .Parallel("准备阶段",
            ("电机归零", "MotorHome"),
            ("打开风扇", "FanOn")
        )
        
        // 2. 延时
        .Delay(1000)

        // 3. 并行读取数据 (测量)
        .ParallelMeasure("测试阶段",
            ("测温", "ReadTemp"),
            ("测速", "ReadSpeed")
        )
        
        // 4. 收尾
        .Finally("停止风扇", "FanOff");

    return await flow.GetResultAsync();
}

 await using (var workflow = MicroWorkflow.Start(step, context))
{
    workflow
        // 普通步骤
        .Then("上电", "PowerOn")
        .Delay(500)
        
        // 【并行测量】
        // 这里会自动将 _requireMeasurements 设为 true  两个动作会同时执行，大大节省时间
        .ParallelMeasure("获取板卡参数", 
            ("读取电压", "ReadVoltage"),
            ("读取电流", "ReadCurrent"),
            ("读取温度", "ReadTemperature")
        )
        
        // 后续步骤
        .Finally("断电", "PowerOff");

    return await workflow.GetResultAsync();
}
动标记：我在 ParallelMeasure 第一行加了 _requireMeasurements = true;。这保证了如果这两个并行任务因为某种原因都没产生数据（极低概率，但逻辑上严谨），GetResultAsync 会报错。
并发容器：你原本定义的 private readonly ConcurrentBag<Measurement> _measurements 是实现并行的关键。Add 操作是原子的，不需要加锁（Lock）。
异常隔离：在 Select 内部包裹了 try-catch。这样如果“读取电压”抛出了空指针异常，不会导致“读取电流”的任务直接崩掉整个程序，而是会被捕获并返回 ExecutionResult.Failed，最后在 WhenAll 之后统一汇报。

 */