using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Utils
{
    public static class TaskExtensions
    {
        /// <summary>
        /// 为旧版 .NET 提供 Task.WaitAsync(TimeSpan) 的功能。
        /// 等待任务在指定的超时时间内完成。
        /// </summary>
        /// <param name="task">要等待的任务。</param>
        /// <param name="timeout">等待的超时时间。</param>
        /// <exception cref="TimeoutException">如果在超时时间内任务未完成，则抛出此异常。</exception>
        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            // 创建一个延时任务，代表超时
            var timeoutTask = Task.Delay(timeout);
            // 使用 Task.WhenAny 等待 task 或 timeoutTask 中的任何一个先完成
            var completedTask = await Task.WhenAny(task, timeoutTask);
            // 如果先完成的是 timeoutTask，说明原始任务超时了
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("The operation has timed out.");
            }
            // 如果先完成的是原始 task，我们需要 `await` 它来传播可能发生的异常。
            // 此时因为它已经完成，所以 await 会立即返回。
            await task;
        }
    }
}
