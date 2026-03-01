using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Sensing.Abstractions;
using ZL.Gear.Sensing.Dto;

namespace ZL.Gear.Sensing.Orchestration
{
    /// <summary>
    /// 标准测量判定引擎实现。
    /// 基于 Reactive Extensions (Rx) 实现高响应、低耦合的测量逻辑。
    /// </summary>
    public class DefaultMeasurementEngine<T> : IMeasurementEngine<T> where T : IComparable<T>
    {
        private readonly Action<string> _log;

        public DefaultMeasurementEngine(Action<string> log = null)
        {
            _log = log ?? (s => { });
        }

        public async Task<ExeResult<T>> ExecuteAsync(
            IObservable<T> dataStream, 
            SamplingConfig<T> config, 
            CancellationToken token)
        {
            _log($"[Engine] 测量开始, 总超时: {config.TotalTimeoutMs}ms");

            var samples = new List<T>();
            var tcs = new TaskCompletionSource<ExeResult<T>>();
            bool isTriggered = false;

            // 设置总超时
            using var timeoutCts = new CancellationTokenSource(config.TotalTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            
            // 响应式流水线
            var subscription = dataStream
                .Subscribe(
                    sample => 
                    {
                        try 
                        {
                            // 1. 触发逻辑
                            if (!isTriggered)
                            {
                                if (config.Trigger.ShouldStart(sample, false))
                                {
                                    isTriggered = true;
                                    _log($"[Engine] 采集激活, 触发值: {sample}");
                                    config.OnTestStarted?.Invoke(sample);
                                }
                                else return;
                            }

                            // 2. 单样本验证
                            if (config.PerSampleValidator != null && !config.PerSampleValidator(sample))
                            {
                                return;
                            }

                            // 3. 策略处理
                            var (isDone, shouldCollect) = config.Strategy.ProcessSample(sample, samples);
                            if (shouldCollect)
                            {
                                samples.Add(sample);
                                config.OnSampleCollected?.Invoke(sample);
                            }

                            // 4. 判定完成/停止
                            if (isDone || config.Trigger.ShouldStop(sample, true))
                            {
                                var result = BuildResult(samples, config, "采样完成");
                                tcs.TrySetResult(result);
                            }
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    },
                    error => tcs.TrySetException(error),
                    () => tcs.TrySetResult(BuildResult(samples, config, "数据流终止"))
                );

            try
            {
                using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                var status = linkedCts.IsCancellationRequested && !token.IsCancellationRequested 
                    ? ExeStatus.TimedOut 
                    : ExeStatus.Cancelled;
                
                string msg = status == ExeStatus.TimedOut ? "测量判定超时" : "操作取消";
                _log($"[Engine] {msg}");
                
                // 超时或取消也计算当前已有样本的结果（由 ExeResult 约定）
                return BuildResult(samples, config, msg, status);
            }
            finally
            {
                subscription.Dispose();
            }
        }

        private ExeResult<T> BuildResult(List<T> samples, SamplingConfig<T> config, string message, ExeStatus status = ExeStatus.Completed)
        {
            if (samples.Count == 0)
            {
                return ExeResult<T>.Failed($"[{message}] 未采集到任何有效样本", samples.AsReadOnly());
            }

            try
            {
                var finalValue = config.Strategy.CalculateResult(samples);
                bool passed = config.SpecChecker?.Invoke(finalValue) ?? true;
                
                if (status == ExeStatus.Completed)
                {
                    return ExeResult<T>.Success(finalValue, samples.AsReadOnly(), passed, message);
                }
                else if (status == ExeStatus.TimedOut)
                {
                    return ExeResult<T>.TimedOut(samples.AsReadOnly(), message);
                }
                else
                {
                    return ExeResult<T>.Failed(message, samples.AsReadOnly());
                }
            }
            catch (Exception ex)
            {
                return ExeResult<T>.Failed($"结果计算异常: {ex.Message}", samples.AsReadOnly());
            }
        }
    }
}
