using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Devices.Abstractions
{
    /// <summary>
    /// 定义了基础数据传输通道的行为。
    /// 重点在于原始字节的发送和接收。
    /// </summary>
    public interface ITransport : IAsyncDisposable, IDisposable
    {
        bool IsConnected { get; }
        
        /// <summary>
        /// 获取底层的双向数据流。
        /// 在调用 ConnectAsync 成功后可用。
        /// </summary>
        Stream DataStream { get; }
        
        Task ConnectAsync(CancellationToken token = default);
        Task DisconnectAsync(CancellationToken token = default);

        /// <summary>
        /// 异步发送原始字节。
        /// </summary>
        Task SendAsync(byte[] data, CancellationToken token = default);

        /// <summary>
        /// 异步接收原始字节到缓冲区。
        /// </summary>
        /// <returns>实际读取的字节数。</returns>
        Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default);
        
        /// <summary>
        /// 异步接收数据并返回新的字节数组（便捷方法）。
        /// </summary>
        Task<byte[]> ReceiveAsync(CancellationToken token = default);

        /// <summary>
        /// 检查传输通道当前是否健康。
        /// </summary>
        bool IsHealthy();

        /// <summary>
        /// 清除输入/输出缓冲区中的残留数据。
        /// </summary>
        void ClearBuffers();
    }
}
