using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 宿主 Bootstrap 注册通道采样请求器；Handler 通过通道名请求 <see cref="ISamplingSession{T}"/>。
    /// 宿主 Bootstrap 注册通道示例：如 <c>SessionRequesters["Noise"] = …</c>。
    /// </summary>
    public class SampleEvents
    {
        public static readonly Dictionary<string, Func<Task<object>>> SessionRequesters =
            new Dictionary<string, Func<Task<object>>>();

        public static Task<ISamplingSession<T>> RequestSessionAsync<T>(string channel) where T : IComparable<T>
        {
            if (SessionRequesters.TryGetValue(channel, out var requester))
            {
                var tcs = new TaskCompletionSource<ISamplingSession<T>>();

                requester().ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        tcs.SetException(task.Exception!.InnerExceptions);
                    }
                    else if (task.IsCanceled)
                    {
                        tcs.SetCanceled();
                    }
                    else
                    {
                        tcs.SetResult((ISamplingSession<T>)task.Result);
                    }
                });

                return tcs.Task;
            }

            return Task.FromException<ISamplingSession<T>>(
                new KeyNotFoundException($"没有为通道 '{channel}' 注册采样请求器。请检查宿主 Bootstrap 初始化。")
            );
        }
    }
}
