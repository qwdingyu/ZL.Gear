using System;
using System.Threading;

namespace ZL.Gear.Core.Devices.Utils
{
    public class MessageTimeoutException : OperationCanceledException
    {
        public byte[] PartialData { get; }
        public MessageTimeoutException(string message, CancellationToken token, byte[] partialData)
            : base(message, token)
        {
            PartialData = partialData;
        }
    }
}
