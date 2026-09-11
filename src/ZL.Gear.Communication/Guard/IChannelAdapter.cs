using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Communication.Guard
{
    /// <summary>
    /// 通道适配器接口
    /// </summary>
    public interface IChannelAdapter : IDisposable
    {
        string ChannelId { get; }
        bool IsConnected { get; }
        event Action<byte[]> OnDataReceived;
        Task OpenAsync(CancellationToken token = default);
        Task CloseAsync();
        Task SendAsync(byte[] data, CancellationToken token = default);
    }
}
