using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Devices
{
    /// <summary>
    /// 设备资源锁管理器。
    /// 在多工位并行执行时，防止多个工位同时操作同一个物理通道或设备。
    /// 这是 ATE 测试系统中确保数据准确性和硬件安全的关键“护城河”。
    /// </summary>
    public class DeviceLockManager
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _deviceLocks = new();

        /// <summary>
        /// 获取或创建一个设备的信号量锁。
        /// </summary>
        public static SemaphoreSlim GetLock(string deviceCode)
        {
            return _deviceLocks.GetOrAdd(deviceCode, _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>
        /// 尝试进入设备锁。
        /// </summary>
        public static async Task<IDisposable> LockAsync(string deviceCode, int timeoutMs, CancellationToken token)
        {
            var semaphore = GetLock(deviceCode);
            bool acquired = await semaphore.WaitAsync(timeoutMs, token);
            
            if (!acquired)
            {
                throw new TimeoutException($"无法在 {timeoutMs}ms 内获取设备 '{deviceCode}' 的排他锁。");
            }

            return new Releaser(semaphore);
        }

        private class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
            public void Dispose() => _semaphore.Release();
        }
    }
}
