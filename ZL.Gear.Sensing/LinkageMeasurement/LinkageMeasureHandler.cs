using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Sensing.Samplers;

namespace ZL.Gear.Sensing.LinkageMeasurement
{
    /// <summary>
    /// 主从联动测量处理器。
    /// 支持两种模式：
    /// 1. 主步骤 (IsMaster=true): 监控条件，满足时发送开始信号，结束时发送停止信号
    /// 2. 从步骤 (DependsOn=xxx): 等待主步骤信号，收到后执行测量
    /// </summary>
    public class LinkageMeasureHandler : IStepHandler
    {
        private readonly Action<string> _log;

        public LinkageMeasureHandler(Action<string> log = null)
        {
            _log = log ?? SensingLog.Default;
        }

        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            var isMaster = GetParameter<bool>(step.Parameters, "IsMaster", false);
            var dependsOn = GetParameter<string>(step.Parameters, "DependsOn", "");

            if (isMaster)
            {
                return await ExecuteAsMasterAsync(step, context).ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(dependsOn))
            {
                return await ExecuteAsDependentAsync(step, context).ConfigureAwait(false);
            }

            return ExecutionResult.Failed("LinkageMeasure 需要指定 IsMaster=true 或 DependsOn=主步骤Key");
        }

        private T GetParameter<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is T t) return t;
                if (value != null)
                {
                    var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                    if (targetType == typeof(double)) return (T)(object)Convert.ToDouble(value);
                    if (targetType == typeof(int)) return (T)(object)Convert.ToInt32(value);
                    if (targetType == typeof(bool)) return (T)(object)Convert.ToBoolean(value);
                    try { return (T)Convert.ChangeType(value, targetType); }
                    catch { return defaultValue; }
                }
            }
            return defaultValue;
        }

        private SamplingConfigModel ParseSamplingConfig(Dictionary<string, object> parameters)
        {
            var config = new SamplingConfigModel
            {
                Mode = "FixedCount",
                SampleCount = 10,
                SampleIntervalMs = 100,
                TimeoutMs = 5000,
                Calculator = "Average"
            };

            if (parameters.TryGetValue("Sampling", out var samplingObj) && samplingObj is Dictionary<string, object> samplingDict)
            {
                if (samplingDict.TryGetValue("Mode", out var modeObj) && modeObj is string mode)
                    config.Mode = mode;
                
                if (samplingDict.TryGetValue("SampleCount", out var countObj))
                    config.SampleCount = Convert.ToInt32(countObj);
                
                if (samplingDict.TryGetValue("SampleIntervalMs", out var intervalObj))
                    config.SampleIntervalMs = Convert.ToInt32(intervalObj);
                
                if (samplingDict.TryGetValue("TimeoutMs", out var timeoutObj))
                    config.TimeoutMs = Convert.ToInt32(timeoutObj);
                
                if (samplingDict.TryGetValue("Calculator", out var calcObj) && calcObj is string calc)
                    config.Calculator = calc;

                if (samplingDict.TryGetValue("Trigger", out var triggerObj) && triggerObj is Dictionary<string, object> triggerDict)
                {
                    config.Trigger = new TriggerConfigModel
                    {
                        Type = triggerDict.ContainsKey("Type") ? triggerDict["Type"] as string ?? "Immediate" : "Immediate",
                        StartCondition = triggerDict.ContainsKey("StartCondition") ? triggerDict["StartCondition"] as string ?? "" : "",
                        StopCondition = triggerDict.ContainsKey("StopCondition") ? triggerDict["StopCondition"] as string ?? "" : ""
                    };
                }
            }

            return config;
        }

        private async Task<ExecutionResultBase> ExecuteAsMasterAsync(StepConfig step, StepContext context)
        {
            var stepName = step.StepName;
            _log($"[主步骤] {stepName} 开始执行");

            try
            {
                if (!context.Variables.TryGetSignalPair(step.StepKey, out var signals))
                {
                    return ExecutionResult.Failed($"[主步骤] 未找到信令对象 '{step.StepKey}'");
                }

                var samplingConfig = ParseSamplingConfig(step.Parameters);

                if (string.IsNullOrEmpty(step.Target))
                {
                    return ExecutionResult.Failed("[主步骤] 未指定 Target 设备");
                }
                if (!context.ActiveDevices.TryGetValue(step.Target, out var device))
                {
                    return ExecutionResult.Failed($"[主步骤] 找不到设备 '{step.Target}'");
                }

                var cmd = "READ";
                if (step.Parameters.TryGetValue("Command", out var cmdObj))
                {
                    cmd = cmdObj?.ToString() ?? "READ";
                }

                var startCondition = ParseCondition(samplingConfig.Trigger?.StartCondition ?? "");
                var stopCondition = ParseCondition(samplingConfig.Trigger?.StopCondition ?? "");

                var samples = new List<double>();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                var started = false;
                var sampleIndex = 0;

                using var sampler = new DeviceSampler<double>(
                    stepName,
                    device,
                    cmd,
                    step.Parameters,
                    context,
                    TimeSpan.FromMilliseconds(samplingConfig.SampleIntervalMs)
                );

                // 关键修复：先订阅数据流，再启动采样，避免丢失初始样本
                var subscription = sampler.DataStream.Subscribe(m =>
                {
                    try
                    {
                        if (cts.Token.IsCancellationRequested) return;
                        if (!m.Success || m.Value == null) return;

                        var value = (double)m.Value;
                        samples.Add(value);
                        sampleIndex++;
                        _log($"[主步骤] {stepName} 采样值[{sampleIndex}]: {value}");

                        if (!started && startCondition(value))
                        {
                            started = true;
                            _log($"[主步骤] {stepName} 条件满足，发送信号...");
                            signals.StartSignal.TrySetResult(true);
                            context.Variables.Set(step.MeasurementKey ?? "current_value", value);
                        }

                        if (started && stopCondition(value))
                        {
                            _log($"[主步骤] {stepName} 停止条件满足");
                            signals.EndSignalCts?.Cancel();
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[主步骤] 处理采样数据错误: {ex.Message}");
                    }
                });

                try
                {
                    sampler.Start();
                    await Task.Delay(samplingConfig.SampleCount * samplingConfig.SampleIntervalMs + 500, cts.Token).ConfigureAwait(false);
                    signals.EndSignalCts?.Cancel();
                }
                finally
                {
                    sampler.Stop();
                    subscription.Dispose();
                }

                if (started)
                {
                    var result = Core.Models.Measurement<double>.Create(
                        step.MeasurementKey ?? "value",
                        samples.LastOrDefault(),
                        true,
                        $"主步骤完成，触发信号。样本数: {samples.Count}",
                        "A",
                        samples.Count
                    );
                    return ExecutionResult<Core.Models.Measurement>.Succeeded(result);
                }

                return ExecutionResult.Failed($"主步骤未满足触发条件。样本数: {samples.Count}");
            }
            catch (OperationCanceledException)
            {
                return ExecutionResult.Failed("主步骤操作超时或被取消");
            }
            catch (Exception ex)
            {
                _log($"[主步骤] {stepName} 执行错误: {ex.Message}");
                return ExecutionResult.Failed($"主步骤执行错误: {ex.Message}");
            }
        }

        private async Task<ExecutionResultBase> ExecuteAsDependentAsync(StepConfig step, StepContext context)
        {
            var stepName = step.StepName;
            var masterKey = GetParameter<string>(step.Parameters, "DependsOn", "");
            _log($"[从步骤] {stepName} 开始执行，等待主步骤 '{masterKey}' 的信号");

            try
            {
                if (!context.Variables.TryGetSignalPair(masterKey, out var signals))
                {
                    return ExecutionResult.Failed($"[从步骤] 未找到主步骤 '{masterKey}' 的信令对象");
                }

                var timeoutMs = step.TimeoutMs > 0 ? step.TimeoutMs : 30000;
                var waitTask = signals.StartSignal.Task;
                var delayTask = Task.Delay(timeoutMs, context.CancellationToken);

                var completedTask = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);

                if (completedTask == delayTask)
                {
                    return ExecutionResult.Failed($"[从步骤] 等待主步骤信号超时 ({timeoutMs}ms)");
                }

                if (waitTask.IsFaulted)
                {
                    return ExecutionResult.Failed("[从步骤] 等待主步骤信号时发生内部异常。");
                }

                if (!waitTask.Result)
                {
                    return ExecutionResult.Failed("[从步骤] 收到无效的信号");
                }

                _log($"[从步骤] {stepName} 收到主步骤信号，开始执行测量");

                if (string.IsNullOrEmpty(step.Target))
                {
                    return ExecutionResult.Failed("[从步骤] 未指定 Target 设备");
                }
                if (!context.ActiveDevices.TryGetValue(step.Target, out var device))
                {
                    return ExecutionResult.Failed($"[从步骤] 找不到设备 '{step.Target}'");
                }

                var samplingConfig = ParseSamplingConfig(step.Parameters);

                var cmd = "READ";
                if (step.Parameters.TryGetValue("Command", out var cmdObj))
                {
                    cmd = cmdObj?.ToString() ?? "READ";
                }

                using var sampler = new DeviceSampler<double>(
                    stepName,
                    device,
                    cmd,
                    step.Parameters,
                    context,
                    TimeSpan.FromMilliseconds(samplingConfig.SampleIntervalMs)
                );

                var samples = new List<double>();
                var sampleIndex = 0;

                sampler.Start();

                var subscription = sampler.DataStream.Subscribe(
                    m => {
                        if (context.CancellationToken.IsCancellationRequested) return;
                        if (!m.Success || m.Value == null) return;

                        var value = (double)m.Value;
                        samples.Add(value);
                        sampleIndex++;
                        _log($"[从步骤] {stepName} 采样值[{sampleIndex}]: {value}");
                    },
                    ex => _log($"[从步骤] 采样错误: {ex.Message}")
                );

                try
                {
                    await Task.Delay(samplingConfig.SampleCount * samplingConfig.SampleIntervalMs + 500, context.CancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    sampler.Stop();
                    subscription.Dispose();
                }

                if (samples.Count > 0)
                {
                    var resultValue = samplingConfig.Calculator switch
                    {
                        "Max" => samples.Max(),
                        "Min" => samples.Min(),
                        "Last" => samples.LastOrDefault(),
                        _ => samples.Average()
                    };

                    var measurement = Core.Models.Measurement<double>.Create(
                        step.MeasurementKey ?? "value",
                        resultValue,
                        true,
                        $"从步骤完成，样本数: {samples.Count}",
                        "dB",
                        samples.Count
                    );

                    context.Variables.Set(step.MeasurementKey ?? "noise_value", resultValue);
                    return ExecutionResult<Core.Models.Measurement>.Succeeded(measurement);
                }

                return ExecutionResult.Failed("从步骤未获取到有效样本");
            }
            catch (OperationCanceledException)
            {
                return ExecutionResult.Failed("从步骤操作超时或被取消");
            }
            catch (Exception ex)
            {
                _log($"[从步骤] {stepName} 执行错误: {ex.Message}");
                return ExecutionResult.Failed($"从步骤执行错误: {ex.Message}");
            }
        }

        private static Func<double, bool> ParseCondition(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return _ => true;

            condition = condition.Trim();
            if (condition.StartsWith(">="))
            {
                var val = double.Parse(condition.Substring(2).Trim());
                return v => v >= val;
            }
            if (condition.StartsWith(">"))
            {
                var val = double.Parse(condition.Substring(1).Trim());
                return v => v > val;
            }
            if (condition.StartsWith("<="))
            {
                var val = double.Parse(condition.Substring(2).Trim());
                return v => v <= val;
            }
            if (condition.StartsWith("<"))
            {
                var val = double.Parse(condition.Substring(1).Trim());
                return v => v < val;
            }
            if (condition.StartsWith("=="))
            {
                var val = double.Parse(condition.Substring(2).Trim());
                return v => v == val;
            }

            return _ => true;
        }
    }
}
