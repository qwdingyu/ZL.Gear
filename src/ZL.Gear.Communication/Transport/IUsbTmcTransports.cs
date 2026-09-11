using System.Threading;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Communication.Transport
{
    public interface IUsbTmcTransports : ITransport
    {
        void Write(string command, CancellationToken token);
        string Query(string command, CancellationToken token, int timeoutMs = 5000);
    }
}

