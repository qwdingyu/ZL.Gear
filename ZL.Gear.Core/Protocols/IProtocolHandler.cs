using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Core.Protocols
{
    /// <summary>
    /// 设备协议处理器接口。
    /// 负责将逻辑命令及其参数转换为物理传输指令，并解析响应。
    /// </summary>
    public interface IUniversalProtocolHandler
    {
        /// <summary>
        /// 执行协议命令
        /// </summary>
        /// <param name="transport">传输层接口</param>
        /// <param name="commandKey">协议配置中的命令Key (如 "MeasureResistance")</param>
        /// <param name="args">命令格式化参数</param>
        /// <param name="token">取消令牌</param>
        /// <returns>解析后的结果 (可能是 double, string 等)</returns>
        Task<object> ExecuteAsync(ITransport transport, string commandKey, object args, CancellationToken token = default);
    }
}
