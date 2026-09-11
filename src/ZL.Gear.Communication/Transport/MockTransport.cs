using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Communication.Abstractions;

namespace ZL.Gear.Communication.Transport
{
    /// <summary>
    /// 仿真传输层。
    /// 用于离线调试，根据发送的指令模拟返回预设的数据。
    /// </summary>
    public class MockTransport : ITransport
    {
        private bool _isConnected;
        private readonly MemoryStream _mockStream = new MemoryStream();
        private readonly Func<string, string> _responder;

        public Stream DataStream => _mockStream;
        public bool IsConnected => _isConnected;

        public MockTransport(Func<string, string> responder = null)
        {
            _responder = responder ?? (cmd => 
            {
                if (cmd.Contains("?")) return (new Random().NextDouble() * 100).ToString("F2") + "\r\n";
                return "OK\r\n";
            });
        }

        public Task ConnectAsync(CancellationToken token = default)
        {
            _isConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken token = default)
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, CancellationToken token = default)
        {
            if (!_isConnected) throw new InvalidOperationException("MockTransport is not connected.");

            string cmd = Encoding.ASCII.GetString(data).Trim();
            string response = _responder(cmd);
            byte[] responseBytes = Encoding.ASCII.GetBytes(response);

            await Task.Delay(10, token);

            lock (_mockStream)
            {
                _mockStream.Write(responseBytes, 0, responseBytes.Length);
            }
        }

        public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            if (!_isConnected) throw new InvalidOperationException("MockTransport is not connected.");

            lock (_mockStream)
            {
                if (_mockStream.Length == 0) return Task.FromResult(0);

                _mockStream.Position = 0;
                int read = _mockStream.Read(buffer, offset, count);
                
                byte[] remaining = new byte[_mockStream.Length - read];
                _mockStream.Read(remaining, 0, remaining.Length);
                _mockStream.SetLength(0);
                _mockStream.Write(remaining, 0, remaining.Length);
                
                return Task.FromResult(read);
            }
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken token = default)
        {
            byte[] buffer = new byte[1024];
            int read = await ReceiveAsync(buffer, 0, buffer.Length, token);
            byte[] result = new byte[read];
            Array.Copy(buffer, 0, result, 0, read);
            return result;
        }

        public bool IsHealthy() => true;

        public void ClearBuffers()
        {
            lock (_mockStream)
            {
                _mockStream.SetLength(0);
            }
        }

        public void Dispose()
        {
            _mockStream.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
