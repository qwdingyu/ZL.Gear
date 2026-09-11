using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Sensing.Abstractions
{
    public interface IDataListener : IAsyncDisposable
    {
        // 当一个完整的数据帧被接收并解析后触发
        event Action<byte[]> FrameReceived;

        // 启动后台监听任务
        Task StartAsync(CancellationToken token = default);

        // 停止后台监听任务
        Task StopAsync();
    }
    /// <summary>
    /// 专门为噪声仪实现的监听器。
    /// 它假定噪声仪主动上传的数据帧以回车换行符（CRLF, \r\n）结尾。
    /// </summary>

}
