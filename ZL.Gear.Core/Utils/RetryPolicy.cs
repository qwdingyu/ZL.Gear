using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Utils
{
    /// <summary>
    /// 一个简单的重试策略工具类。
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>
        /// 执行一个带指数退避重试策略的异步操作。
        /// </summary>
        /// <param name="action">要执行的异步操作，返回true表示成功，false表示失败需要重试。</param>
        /// <param name="maxRetries">最大重试次数。</param>
        /// <param name="initialDelayMs">初始延迟（毫秒）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>如果操作最终成功，返回true；否则返回false。</returns>
        public static async Task<bool> ExecuteAsync(Func<Task<bool>> action, int maxRetries, int initialDelayMs, CancellationToken cancellationToken)
        {
            int delay = initialDelayMs;
            for (int i = 0; i < maxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await action())
                {
                    return true; // 成功，立即返回
                }

                // 如果是最后一次尝试，失败后不再延迟
                if (i == maxRetries - 1) break;
                await Task.Delay(delay, cancellationToken);
                delay *= 2; // 指数增加延迟
            }
            return false; // 所有重试都失败
        }
    }
}
