using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Communication.Abstractions;

#if NETSTANDARD2_0
// 在 .NET Standard 2.0 中，Parity 和 StopBits 是枚举类型
// 使用完整命名空间引用以确保兼容性
#endif

namespace ZL.Gear.Communication.Transport
{
    /// <summary>
    /// 串口传输层实现。
    /// 封装了标准 System.IO.Ports.SerialPort。
    /// </summary>
    public class SerialTransport : ITransport
    {
        private SerialPort _serialPort;
        private bool _isDisposed;
        private string _portName;

        public Stream DataStream => _serialPort.BaseStream;
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        /// <summary>
        /// 使用默认参数创建串口传输
        /// </summary>
        public SerialTransport(string portName, int baudRate)
            : this(portName, baudRate, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
        {
        }

        /// <summary>
        /// 使用完整参数创建串口传输
        /// </summary>
        public SerialTransport(string portName, int baudRate, System.IO.Ports.Parity parity, int dataBits, System.IO.Ports.StopBits stopBits)
        {
            _portName = portName;
            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
        }

        /// <summary>
        /// 使用配置对象创建串口传输
        /// </summary>
        public SerialTransport(SerialPortSettings settings)
        {
            _portName = settings.PortName;
            _serialPort = new SerialPort
            {
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                Parity = settings.Parity,
                DataBits = settings.DataBits,
                StopBits = settings.StopBits,
                ReadTimeout = 5000,
                WriteTimeout = 1000,
                ReadBufferSize = 262144,
                WriteBufferSize = 131072
            };
        }

        public async Task ConnectAsync(CancellationToken token = default)
        {
            CheckDisposed();
            if (IsConnected) return;

            try
            {
                await Task.Run(() => _serialPort.Open(), token);
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
            catch (Exception ex)
            {
                throw new TransportException($"无法打开串口 {_serialPort.PortName}: {ex.Message}", ex);
            }
        }

        public Task DisconnectAsync(CancellationToken token = default)
        {
            if (IsConnected)
            {
                try { _serialPort.Close(); } catch { }
            }
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, CancellationToken token = default)
        {
            CheckConnected();
            try
            {
                await _serialPort.BaseStream.WriteAsync(data, 0, data.Length, token);
            }
            catch (Exception ex)
            {
                throw new TransportException($"串口发送失败: {ex.Message}", ex);
            }
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            CheckConnected();
            try
            {
                return await _serialPort.BaseStream.ReadAsync(buffer, offset, count, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new TransportException($"串口读取失败: {ex.Message}", ex);
            }
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken token = default)
        {
            byte[] buffer = new byte[1024];
            int read = await ReceiveAsync(buffer, 0, buffer.Length, token);
            if (read == 0) return Array.Empty<byte>();
            byte[] result = new byte[read];
            Array.Copy(buffer, 0, result, 0, read);
            return result;
        }

        public bool IsHealthy()
        {
            return IsConnected;
        }

        public void ClearBuffers()
        {
            if (IsConnected)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }

        private void CheckDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(SerialTransport));
        }

        private void CheckConnected()
        {
            CheckDisposed();
            if (!IsConnected) throw new TransportException("串口未连接或已关闭。");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            try { _serialPort?.Dispose(); } catch { }
            _isDisposed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            Dispose();
        }
    }

    /// <summary>
    /// 串口配置
    /// </summary>
    public class SerialPortSettings
    {
        public string PortName { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
    }

    /// <summary>
    /// 传输异常
    /// </summary>
    public class TransportException : Exception
    {
        public TransportException(string message) : base(message) { }
        public TransportException(string message, Exception inner) : base(message, inner) { }
    }
}
