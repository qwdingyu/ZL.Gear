using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Utils
{
    public class Debouncer : IDisposable
    {
        private readonly int _delayMilliseconds;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 创建一个防抖器。
        /// </summary>
        /// <param name="delayMilliseconds">事件停止后需要等待的毫秒数。</param>
        public Debouncer(int delayMilliseconds = 300)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        /// <summary>
        /// 触发一个需要防抖的操作。如果短时间内多次调用，只有最后一次会真正执行。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        public void Debounce(Action action)
        {
            // 取消上一个待执行的任务
            _cts?.Cancel();
            _cts?.Dispose();

            // 创建新的 CancellationTokenSource
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Delay(_delayMilliseconds, token).ContinueWith(t =>
            {
                // 如果任务没有被取消，则执行操作
                if (!t.IsCanceled)
                {
                    action?.Invoke();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext()); // 确保在UI线程执行
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

}
