using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 步骤延迟中间件。
    /// 在步骤执行前/后添加延迟，用于硬件 settling time。
    /// 
    /// 使用方式：在步骤的 Parameters 中配置
    /// - DelayBeforeMs: 执行前延迟（毫秒），默认 0
    /// - DelayAfterMs: 执行后延迟（毫秒），默认 0
    /// - RandomDelayRange: 随机延迟范围，格式 "min-max"，用于模拟真实场景
    /// 
    /// 优点：替代在每个步骤配置 Wait，简化配置
    /// </summary>
    public class StepDelayMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;
        private readonly Random _random = new Random();

        public StepDelayMiddleware(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 获取延迟配置
            int delayBeforeMs = GetParameter(step, "DelayBeforeMs", 0);
            int delayAfterMs = GetParameter(step, "DelayAfterMs", 0);
            string randomDelayRange = GetParameterString(step, "RandomDelayRange", "");

            // 执行前延迟
            if (delayBeforeMs > 0 || !string.IsNullOrEmpty(randomDelayRange))
            {
                int actualDelayBefore = CalculateDelay(delayBeforeMs, randomDelayRange);
                if (actualDelayBefore > 0)
                {
                    _log($"[Delay] 步骤 '{step.StepName}' 执行前等待 {actualDelayBefore}ms");
                    await Task.Delay(actualDelayBefore, context.CancellationToken).ConfigureAwait(false);
                }
            }

            // 执行步骤
            var result = await next(step, context).ConfigureAwait(false);

            // 执行后延迟
            if (delayAfterMs > 0)
            {
                _log($"[Delay] 步骤 '{step.StepName}' 执行后等待 {delayAfterMs}ms");
                await Task.Delay(delayAfterMs, context.CancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        private int CalculateDelay(int baseDelay, string randomRange)
        {
            if (baseDelay > 0 && string.IsNullOrEmpty(randomRange))
            {
                return baseDelay;
            }

            if (!string.IsNullOrEmpty(randomRange))
            {
                var parts = randomRange.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out var min) && int.TryParse(parts[1], out var max))
                {
                    var randomDelay = _random.Next(min, max + 1);
                    _log($"[Delay] 随机延迟: {randomDelay}ms (范围: {randomRange})");
                    return randomDelay;
                }
            }

            return 0;
        }

        private int GetParameter(StepConfig step, string key, int defaultValue)
        {
            if (step.TryGetParameter(key, out var obj))
            {
                if (int.TryParse(obj?.ToString(), out var value))
                {
                    return value;
                }
            }
            return defaultValue;
        }

        private string GetParameterString(StepConfig step, string key, string defaultValue)
        {
            if (step.TryGetParameter(key, out var obj))
            {
                return obj?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }
    }
}
