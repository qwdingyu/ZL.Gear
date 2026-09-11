using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Communication.Guard
{
    /// <summary>
    /// 串口适配器
    /// </summary>
    public sealed class SerialAdapter : IChannelAdapter
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private SerialPort _serialPort;
        private CancellationTokenSource _readCts;
        private Task _readTask;

        public SerialAdapter(string portName, int baudRate = 9600)
        {
            _portName = portName ?? throw new ArgumentNullException(nameof(portName));
            _baudRate = baudRate;
        }

        public string ChannelId => $"Serial:{_portName}:{_baudRate}";
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public event Action<byte[]> OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            CloseInternal();
            _serialPort = new SerialPort(_portName, _baudRate)
            {
                ReadTimeout = 5000,
                WriteTimeout = 1000
            };
            _serialPort.Open();
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            return Task.Run(CloseInternal);
        }

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new InvalidOperationException("Serial port not open.");
            _serialPort.BaseStream.Write(data, 0, data.Length);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseInternal();
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            if (_serialPort == null) return;
            byte[] buffer = new byte[4096];

            try
            {
                while (!token.IsCancellationRequested && _serialPort.IsOpen)
                {
                    int read;
                    try
                    {
                        read = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, token);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }
                    catch
                    {
                        break;
                    }

                    if (read <= 0) break;
                    byte[] data = new byte[read];
                    Buffer.BlockCopy(buffer, 0, data, 0, read);
                    OnDataReceived?.Invoke(data);
                }
            }
            finally
            {
            }
        }

        private void CloseInternal()
        {
            try { _readCts?.Cancel(); } catch { }
            try { _serialPort?.Close(); } catch { }
            try { _serialPort?.Dispose(); } catch { }
            _serialPort = null;
            _readCts?.Dispose();
            _readCts = null;
            _readTask = null;
        }
    }
}
