using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Sensing
{
    /// <summary>
    /// 通用采样引擎。
    /// 负责将“单次读取动作”升级为“策略采样动作”。
    /// </summary>
    public static class SamplingEngine
    {
        public static async Task<ExecutionResult<double>> MeasureAsync(
            Func<CancellationToken, Task<ExecutionResult<double>>> singleReader, 
            SamplingConfig config, 
            CancellationToken token)
        {
            if (config.Mode == SamplingMode.Single)
            {
                return await singleReader(token);
            }

            var samples = new List<double>();
            var startTime = DateTime.Now;
            bool triggerStarted = config.Trigger == null; // 如果没配触发器，默认直接开始

            // --- 采样循环 ---
            while (!token.IsCancellationRequested)
            {
                var reading = await singleReader(token);
                if (!reading.Success) 
                {
                    if (config.IntervalMs > 0) await Task.Delay(config.IntervalMs, token);
                    continue; 
                }

                double val = reading.Value;

                // 1. 检查启动触发条件
                if (!triggerStarted && config.Trigger != null && !string.IsNullOrEmpty(config.Trigger.StartCondition))
                {
                    // 临时字典用于触发判断
                    var triggerVars = new Dictionary<string, object> { { "val", val } };
                    if (ZL.Gear.Core.Infrastructure.Singleton<IWorkflowEvaluator>.Instance.EvaluateCondition(config.Trigger.StartCondition, triggerVars))
                    {
                        triggerStarted = true;
                    }
                }

                if (triggerStarted)
                {
                    // 2. 检查验证/过滤
                    bool valid = true;
                    if (config.Validation != null)
                    {
                        if (config.Validation.MinValue.HasValue && val < config.Validation.MinValue.Value) valid = false;
                        if (config.Validation.MaxValue.HasValue && val > config.Validation.MaxValue.Value) valid = false;
                    }

                    if (valid || (config.Validation != null && !config.Validation.IgnoreInvalid))
                    {
                        samples.Add(val);
                        config.OnSampleCollected?.Invoke(val); // 实时通知
                    }

                    // --- [模式增强：Toggle 检测] ---
                    // 专门针对安全带、卡扣等开关量设备
                    if (config.Mode == SamplingMode.Toggle && samples.Count >= 2)
                    {
                        double prev = samples[samples.Count - 2];
                        // 如果定义了 StartCondition 作为切换判据 (如从 0 变到 1)
                        if (!string.IsNullOrEmpty(config.Trigger.StartCondition))
                        {
                            var toggleVars = new Dictionary<string, object> { { "prev", prev }, { "val", val } };
                            if (ZL.Gear.Core.Infrastructure.Singleton<IWorkflowEvaluator>.Instance.EvaluateCondition(config.Trigger.StartCondition, toggleVars))
                            {
                                break; // 捕捉到瞬间切换，停止采样
                            }
                        }
                    }

                    // --- [模式增强：Stability 监控] ---
                    // 数值必须持续在门限内达到指定时间
                    if (config.Mode == SamplingMode.Stability && !valid)
                    {
                        // 一旦超出范围，重置计时器或清空样本重新开始
                        samples.Clear();
                        startTime = DateTime.Now; 
                    }

                    // 3. 检查停止触发条件
                    if (config.Trigger != null && !string.IsNullOrEmpty(config.Trigger.StopCondition))
                    {
                        var triggerVars = new Dictionary<string, object> { { "val", val } };
                        if (ZL.Gear.Core.Infrastructure.Singleton<IWorkflowEvaluator>.Instance.EvaluateCondition(config.Trigger.StopCondition, triggerVars))
                        {
                            break;
                        }
                    }
                }

                // 4. 判断退出条件 (基于模式)
                if (config.Mode == SamplingMode.FixedCount && samples.Count >= config.Quantity) break;
                if (config.Mode == SamplingMode.Duration && (DateTime.Now - startTime).TotalSeconds >= config.Quantity) break;
                if (config.Mode == SamplingMode.WaitCondition && triggerStarted && samples.Count > 0) break; // 命中一次即退出
                if (config.Mode == SamplingMode.Stability && (DateTime.Now - startTime).TotalSeconds >= config.Quantity && samples.Count > 0) break;

                if (config.IntervalMs > 0)
                {
                    await Task.Delay(config.IntervalMs, token);
                }
            }

            if (samples.Count == 0)
            {
                return ExecutionResult<double>.Failed("采样结束，但未收集到任何有效数据。");
            }

            // --- 数据聚合 ---
            double finalValue = Calculate(samples, config.Calculator);

            return ExecutionResult<double>.Succeeded(
                finalValue, 
                samples.Count, 
                $"采样完成。模式:{config.Mode}, 样本数:{samples.Count}, 算法:{config.Calculator}");
        }

        private static double Calculate(List<double> samples, SamplingCalculator calculator)
        {
            switch (calculator)
            {
                case SamplingCalculator.Max: return samples.Max();
                case SamplingCalculator.Min: return samples.Min();
                case SamplingCalculator.Average: return samples.Average();
                case SamplingCalculator.Sum: return samples.Sum();
                case SamplingCalculator.PkPk: return samples.Max() - samples.Min();
                case SamplingCalculator.Count: return samples.Count;
                case SamplingCalculator.Delta: return samples.Last() - samples.First();
                case SamplingCalculator.Presence: return samples.Count > 0 ? 1.0 : 0.0;
                case SamplingCalculator.First: return samples.FirstOrDefault();
                case SamplingCalculator.Last: 
                default: return samples.LastOrDefault();
            }
        }
    }
}
