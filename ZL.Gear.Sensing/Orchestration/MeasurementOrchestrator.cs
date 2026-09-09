using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;
using ZL.Gear.Sensing.Dto;

namespace ZL.Gear.Sensing.Orchestration
{

    /// <summary>
    /// 负责执行主从测量场景的通用服务。
    /// 这个类是“执行层”的核心，它处理所有运行时的复杂性，如信令、超时和取消。
    /// 它的代码是通用的，不依赖于任何具体的设备或测量类型。
    /// </summary>
    public class MeasurementOrchestrator
    {
        private readonly Action<string> _log;

        public MeasurementOrchestrator(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        /// <summary>
        /// 运行一个已配置好的场景。
        /// </summary>
        public async Task<ExecutionResultBase> RunAsync(ScenarioConfiguration config)
        {
            var stepKey = config.Context.StepKey;
            var stepName = config.Context.StepConfig.StepName;
            var timeoutMs = config.Context.TimeoutMs;

            StepSignalPair signals = null;
            var executionToken = config.Context.CancellationToken;

            // 如果不是独立场景，就需要处理信令
            if (config.Type != ScenarioType.Standalone)
            {
                string signalKey = config.Type == ScenarioType.EventDependent
                    ? config.Args.Get<string>("DependsOn")
                    : stepKey;

                if (string.IsNullOrEmpty(signalKey))
                    return ExecutionResult.Failed($"配置错误: 场景 '{stepName}' (类型: {config.Type}) 未指定信令Key (主步骤的StepKey或DependsOn参数)。");

                if (config.Context.Variables.TryGetSignalPair(signalKey, out var rawSignals))
                {
                    // 找到了信令对，这是标准的主从场景
                    // 使用统一的 StepSignalPair
                    signals = new StepSignalPair(signalKey, rawSignals.StartSignal, rawSignals.EndSignalCts);
                }
                else
                {
                    // 未找到信令对，现在需要判断原因
                    if (config.Type == ScenarioType.EventMaster)
                    {
                        // 这是 "孤儿主帅" 的情况。所有从属步骤可能都已被禁用。
                        // 这不是一个错误。我们只需记录日志，然后让它像一个独立步骤一样继续执行。
                        _log($"[{stepName}] [主步骤] 未找到信令对 (Key: {signalKey})。可能所有从属步骤均已禁用。将作为独立步骤继续执行。");
                        // 关键: `signals` 保持为 null，后续的逻辑会自然地跳过所有信令相关的操作。
                    }
                    else
                    {
                        // 必然是 EventDependent
                        // 这是 "孤儿从属" 的情况。一个从属步骤找不到它的主帅，这是一个明确的配置错误。
                        return ExecutionResult.Failed($"配置错误: 从属步骤 '{stepName}' 未找到其主步骤 '{signalKey}' 的信令对象。请确保主步骤已启用且StepKey正确。");
                    }
                }
            }

            CancellationTokenSource linkedCts = null;
            try
            {
                // --- 从属步骤的特定逻辑 ---
                if (config.Type == ScenarioType.EventDependent)
                {
                    // 如果是 EventDependent，经过上面的检查，signals 绝对不应该为 null。
                    if (signals == null)
                        return ExecutionResult.Failed($"内部逻辑错误: 从属步骤 '{stepName}' 的信令对象为空。");

                    _log($"[{stepName}] 等待主步骤 '{signals.Key}' 的开始信号...");

                    // 等待开始信号，同时受全局取消和步骤超时的影响
                    var waitTask = await Task.WhenAny(signals.StartSignal.Task, Task.Delay(timeoutMs, executionToken)).ConfigureAwait(false);

                    if (executionToken.IsCancellationRequested) return ExecutionResult.Failed("操作被全局取消。");
                    if (waitTask != signals.StartSignal.Task)
                        return ExecutionResult.Failed($"等待主步骤启动信号失败或超时 ({timeoutMs}ms)。");

                    if (signals.StartSignal.Task.IsFaulted)
                        return ExecutionResult.Failed("等待主步骤启动信号时发生内部异常。");

                    if (!signals.StartSignal.Task.Result)
                        return ExecutionResult.Failed($"等待主步骤启动信号失败或超时 ({timeoutMs}ms)。");

                    _log($"[{stepName}] 收到开始信号，测试启动！");
                }

                // 为主从场景创建关联的 CancellationToken
                // 注意: `signals?.` 使得这段代码对 "孤儿主帅" (signals is null) 的情况是安全的
                if (signals?.EndSignalCts != null)
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(executionToken, signals.EndSignalCts.Token);
                    executionToken = linkedCts.Token;
                }

                // --- 执行核心测量任务 ---
                // 无论是哪种场景，最终都会调用 IMeasurable.MeasureAsync
                return await config.Measurable.MeasureAsync(executionToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (signals?.EndSignalCts?.IsCancellationRequested == true && !config.Context.CancellationToken.IsCancellationRequested)
                {
                    return ExecutionResult.Succeeded("同步主步骤正常结束。");
                }
                return ExecutionResult.Failed("操作被取消或超时。");
            }
            catch (Exception ex)
            {
                _log($"执行测量 '{stepName}' 时发生未处理异常: {ex}");
                return ExecutionResult.Failed($"测量操作执行异常: {ex.Message}");
            }
            finally
            {
                linkedCts?.Dispose();

                // --- 主步骤的特定清理逻辑 ---
                // 注意: `&& signals != null` 使得这段代码对 "孤儿主帅" 的情况是安全的
                if (config.Type == ScenarioType.EventMaster && signals != null)
                {
                    if (!signals.EndSignalCts.IsCancellationRequested)
                    {
                        _log($"[{stepName}] [主步骤] 结束，通知所有从属步骤停止。");
                        signals.EndSignalCts.Cancel();
                    }
                    signals.StartSignal.TrySetResult(false);
                }
            }
        }
    }
}
