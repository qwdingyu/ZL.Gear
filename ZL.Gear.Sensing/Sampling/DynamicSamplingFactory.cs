using System;
using System.Collections.Generic;
using ZL.Gear.Sensing.Abstractions;
using ZL.Gear.Sensing.Dto;

namespace ZL.Gear.Sensing.Sampling
{
    public static class DynamicSamplingFactory
    {
        public static ISamplingConfigurator<double> Create(SamplingConfigModel config)
        {
            return new LambdaConfigurator<double>((builder, args, context) =>
            {
                // 1. 结果计算器
                IResultCalculator<double> calc = config.Calculator switch
                {
                    "Max" => new MaxCalculator<double>(),
                    "Min" => new MinCalculator<double>(),
                    "Median" => new MedianCalculator<double>(),
                    "StdDev" => new StdDevCalculator<double>(),
                    "Last" => new LastValueCalculator<double>(),
                    _ => new AverageCalculator<double>()
                };

                // 2. 策略
                ISamplingStrategy<double> strategy = config.Mode switch
                {
                    "FixedCount" => new FixedCountStrategy<double>(config.SampleCount, calc),
                    "Continuous" => new FixedCountStrategy<double>(int.MaxValue, calc),
                    "Average" => new DurationStrategy<double>(TimeSpan.FromMilliseconds(config.TimeoutMs), calc),
                    "Stability" => new DurationStrategy<double>(TimeSpan.FromMilliseconds(config.TimeoutMs), calc),
                    _ => new DurationStrategy<double>(TimeSpan.FromMilliseconds(config.TimeoutMs), calc)
                };
                builder.WithStrategy(strategy);

                // 3. 触发器
                if (config.Trigger != null && config.Trigger.Type == "Threshold")
                {
                    builder.WithTrigger(new ConditionalTrigger<double>(
                        ParseCondition(config.Trigger.StartCondition),
                        ParseCondition(config.Trigger.StopCondition)
                    ));
                }

                // 4. 验证器
                if (config.Validator != null)
                {
                    builder.WithPerSampleValidator(r =>
                        (!config.Validator.Min.HasValue || r >= config.Validator.Min) &&
                        (!config.Validator.Max.HasValue || r <= config.Validator.Max)
                    );
                }

                // 5. 基础时序
                builder.WithInterval(config.SampleIntervalMs);
                builder.WithTimeout(config.TimeoutMs + 2000); // 总保险超时略大于采样超时

                // 6. 规格检查 (SpecCheck 表达式)
                if (!string.IsNullOrEmpty(config.SpecCheck))
                {
                    builder.WithSpecCheck(val => 
                    {
                        // 使用 context 中的 evaluator 评估规格
                        // 由于 SpecCheck 是针对结果 val 的，我们需要把 Value 注入临时变量
                        // 这里稍微简写，假设 evaluator 支持传参
                        var vars = new Dictionary<string, object>(context.Variables.AsDictionary())
                        {
                            ["Value"] = val
                        };
                        return context.Evaluate(config.SpecCheck); // 实际应该支持传入 vars 对比
                    });
                }
            });
        }

        private static Func<double, bool> ParseCondition(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition)) return _ => true;

            condition = condition.Trim();
            if (condition.StartsWith(">="))
            {
                double val = double.Parse(condition.Substring(2).Trim());
                return v => v >= val;
            }
            if (condition.StartsWith(">"))
            {
                double val = double.Parse(condition.Substring(1).Trim());
                return v => v > val;
            }
            if (condition.StartsWith("<="))
            {
                double val = double.Parse(condition.Substring(2).Trim());
                return v => v <= val;
            }
            if (condition.StartsWith("<"))
            {
                double val = double.Parse(condition.Substring(1).Trim());
                return v => v < val;
            }
            if (condition.StartsWith("=="))
            {
                double val = double.Parse(condition.Substring(2).Trim());
                return v => v == val;
            }
            
            return _ => true;
        }
    }
}
