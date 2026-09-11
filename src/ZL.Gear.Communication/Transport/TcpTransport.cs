using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Communication.Abstractions;

namespace ZL.Gear.Communication.Transport
{
    /// <summary>
    /// TCP/IP 传输层实现。
    /// </summary>
    public class TcpTransport : ITransport
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly string _host;
        private readonly int _port;
        private bool _isDisposed;

        public Stream DataStream => _stream;
        public bool IsConnected => _client != null && _client.Connected;

        public TcpTransport(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken token = default)
        {
            CheckDisposed();
            if (IsConnected) return;

            try
            {
                _client = new TcpClient();
                await Task.Run(() => _client.ConnectAsync(_host, _port), token);
                _stream = _client.GetStream();
            }
            catch (Exception ex)
            {
                throw new TransportException($"连接到 TCP 服务器 {_host}:{_port} 失败: {ex.Message}", ex);
            }
        }

        public Task DisconnectAsync(CancellationToken token = default)
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch { }
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, CancellationToken token = default)
        {
            CheckConnected();
            try
            {
                await _stream.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new TransportException($"TCP 发送失败: {ex.Message}", ex);
            }
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            CheckConnected();
            try
            {
                return await _stream.ReadAsync(buffer, offset, count, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new TransportException($"TCP 读取失败: {ex.Message}", ex);
            }
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken token = default)
        {
            byte[] buffer = new byte[4096];
            int read = await ReceiveAsync(buffer, 0, buffer.Length, token);
            if (read <= 0) return Array.Empty<byte>();
            byte[] result = new byte[read];
            Array.Copy(buffer, 0, result, 0, read);
            return result;
        }

        public bool IsHealthy()
        {
            if (!IsConnected) return false;
            try
            {
                return !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0);
            }
            catch { return false; }
        }

        public void ClearBuffers()
        {
        }

        private void CheckDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(TcpTransport));
        }

        private void CheckConnected()
        {
            CheckDisposed();
            if (!IsConnected) throw new TransportException("TCP 未连接或已断开。");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _stream?.Dispose();
            _client?.Close();
            _isDisposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            Dispose();
        }
    }
}
