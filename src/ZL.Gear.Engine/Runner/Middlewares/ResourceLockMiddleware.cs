using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 设备资源锁中间件。
    /// 自动拦截带有 Target 的步骤，确保在执行期间持有该设备的物理排他锁。
    /// 这解决了 ATE 多工位模式下共享仪表冲突的痛点。
    /// </summary>
    public class ResourceLockMiddleware : IStepMiddleware
    {
        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(StepConfig step, StepContext context, Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 如果步骤没有目标设备，或者显式标记为不加锁，则直接跳过
            if (string.IsNullOrEmpty(step.Target) ||
                (step.TryGetParameter("SkipLock", out var sl) && sl.ToString().ToLower() == "true"))
            {
                return await next(step, context).ConfigureAwait(false);
            }

            // 默认等待锁超时 10 秒
            int lockTimeout = 10000;
            if (step.TryGetParameter("LockTimeoutMs", out var ltObj))
            {
                int.TryParse(ltObj.ToString(), out lockTimeout);
            }

            try
            {
                // 获取设备排他锁
                using (await DeviceLockManager.LockAsync(step.Target, lockTimeout, context.CancellationToken).ConfigureAwait(false))
                {
                    context.Log($"[Lock] 已获得设备 '{step.Target}' 的排他锁。");
                    return await next(step, context).ConfigureAwait(false);
                }
            }
            catch (TimeoutException ex)
            {
                context.Log($"[Lock 失败] {ex.Message}");
                return ExecutionResult<List<Measurement>>.Failed(ex.Message);
            }
            catch (OperationCanceledException)
            {
                return ExecutionResult<List<Measurement>>.Failed("获取设备锁时被取消。");
            }
        }
    }
}
