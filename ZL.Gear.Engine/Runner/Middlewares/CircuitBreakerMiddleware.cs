using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 熔断器中间件。
    /// 连续失败指定次数后自动熔断，保护设备不被反复测试。
    /// 
    /// 使用方式：在步骤的 Parameters 中配置
    /// - FailureThreshold: 连续失败次数阈值，默认 3
    /// - RecoveryTimeoutMs: 熔断恢复时间（毫秒），默认 30000
    /// - CircuitBreakAction: 熔断时的动作（Skip/Fail），默认 Skip
    /// 
    /// 特点：
    /// - 按设备（Target）维度统计失败次数
    /// - 成功后自动重置计数器
    /// - 熔断期间跳过测试，节省时间
    /// </summary>
    public class CircuitBreakerMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;
        
        // 按设备统计失败次数
        private static readonly ConcurrentDictionary<string, CircuitBreakerState> _deviceStates = new();
        
        // 默认配置
        private const int DefaultFailureThreshold = 3;
        private const int DefaultRecoveryTimeoutMs = 30000;

        public CircuitBreakerMiddleware(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 获取设备标识（使用 Target 或使用命令作为标识）
            string deviceKey = step.Target ?? step.Command ?? "default";
            
            // 获取配置参数
            int failureThreshold = GetParameter(step, "FailureThreshold", DefaultFailureThreshold);
            int recoveryTimeoutMs = GetParameter(step, "RecoveryTimeoutMs", DefaultRecoveryTimeoutMs);
            string circuitBreakAction = GetParameterString(step, "CircuitBreakAction", "Skip");
            
            var state = _deviceStates.GetOrAdd(deviceKey, _ => new CircuitBreakerState());
            
            // 检查是否处于熔断状态
            if (state.IsOpen)
            {
                // 检查是否超过恢复时间
                if (state.CanAttempt())
                {
                    _log($"[CircuitBreaker] 设备 '{deviceKey}' 熔断恢复，尝试执行步骤 '{step.StepName}'");
                    state.AttemptRecovery();
                }
                else
                {
                    _log($"[CircuitBreaker] 设备 '{deviceKey}' 处于熔断状态 (剩余 {state.RecoveryTimeLeftMs}ms)，跳过步骤 '{step.StepName}'");
                    
                    if (circuitBreakAction.Equals("Fail", StringComparison.OrdinalIgnoreCase))
                    {
                        return ExecutionResult<List<Measurement>>.Failed(
                            $"设备 '{deviceKey}' 处于熔断状态",
                            new List<Measurement>());
                    }
                    
                    return ExecutionResult<List<Measurement>>.Succeeded(
                        new List<Measurement>(),
                        0,
                        "Circuit broken - skipped");
                }
            }

            // 执行步骤
            var result = await next(step, context);

            // 根据结果更新熔断器状态
            if (!result.Success)
            {
                state.RecordFailure(failureThreshold, recoveryTimeoutMs);
                _log($"[CircuitBreaker] 设备 '{deviceKey}' 连续失败 {state.ConsecutiveFailures} 次");
                
                if (state.IsOpen)
                {
                    _log($"[CircuitBreaker] ⚠️ 设备 '{deviceKey}' 已熔断！{recoveryTimeoutMs}ms 内将跳过测试");
                }
            }
            else
            {
                state.RecordSuccess();
                if (state.ConsecutiveFailures == 0)
                {
                    _log($"[CircuitBreaker] 设备 '{deviceKey}' 测试通过，连续失败计数已重置");
                }
            }

            return result;
        }

        private int GetParameter(StepConfig step, string key, int defaultValue)
        {
            if (step.Parameters != null && step.Parameters.TryGetValue(key, out var obj))
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
            if (step.Parameters != null && step.Parameters.TryGetValue(key, out var obj))
            {
                return obj?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// 熔断器状态
        /// </summary>
        private class CircuitBreakerState
        {
            public int ConsecutiveFailures { get; private set; }
            public DateTime? LastFailureTime { get; private set; }
            public DateTime? OpenTime { get; private set; }
            public int RecoveryTimeoutMs { get; private set; }

            public bool IsOpen => OpenTime.HasValue && DateTime.Now < OpenTime.Value.AddMilliseconds(RecoveryTimeoutMs);

            public int RecoveryTimeLeftMs
            {
                get
                {
                    if (!OpenTime.HasValue) return 0;
                    var left = (OpenTime.Value.AddMilliseconds(RecoveryTimeoutMs) - DateTime.Now).TotalMilliseconds;
                    return left > 0 ? (int)left : 0;
                }
            }

            public void RecordFailure(int threshold, int recoveryTimeoutMs)
            {
                ConsecutiveFailures++;
                LastFailureTime = DateTime.Now;
                RecoveryTimeoutMs = recoveryTimeoutMs;

                if (ConsecutiveFailures >= threshold)
                {
                    OpenTime = DateTime.Now;
                }
            }

            public void RecordSuccess()
            {
                ConsecutiveFailures = 0;
                OpenTime = null;
            }

            public bool CanAttempt()
            {
                return !OpenTime.HasValue || (DateTime.Now - OpenTime.Value).TotalMilliseconds >= RecoveryTimeoutMs;
            }

            public void AttemptRecovery()
            {
                // 尝试恢复，重置失败计数但保持打开状态
                ConsecutiveFailures = 0;
            }
        }
    }
}
