using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices.Dto;

namespace ZL.Gear.Core.Devices.Protocol
{
    /// <summary>
    /// 定义了设备通信协议处理器的行为。
    /// </summary>
    public interface IProtocolHandler
    {
        /// <summary>
        /// 根据命令规范，通过指定的传输通道执行一个命令。
        /// </summary>
        /// <param name="transport">用于通信的传输通道。</param>
        /// <param name="spec">描述命令行为的规范。</param>
        /// <param name="args">本次调用的运行时参数。</param>
        /// <param name="log">日志记录委托。</param>
        /// <param name="token">取消令牌。</param>
        /// <returns>命令的执行结果。</returns>
        Task<ExecutionResultBase> ExecuteCommandAsync(CommandSpec spec, Dictionary<string, object> args, Action<string> log, CancellationToken token);
        // 接收一个完整的、去除了协议外壳的消息
        Task<byte[]> ReceiveMessageAsync(CancellationToken token);
        // 发送一个纯净的业务消息，由 Handler 负责添加协议外壳
        Task SendMessageAsync(string message, CancellationToken token);
        Task SendMessageAsync(byte[] data, CancellationToken token);
        /// <summary>
        /// 重置协议处理器的内部接收状态。
        /// </summary>
        void ResetReceiveBuffer();
        Task FlushBuffersAsync(CancellationToken token);
    }
}
