using System.Threading;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Communication.NiVisa.Abstractions
{
    /// <summary>
    /// USB-TMC 传输扩展接口。
    /// </summary>
    public interface IUsbTmcTransports : ITransport
    {
        void Write(string command, CancellationToken token);
        string Query(string command, CancellationToken token, int timeoutMs = 5000);
    }
}
