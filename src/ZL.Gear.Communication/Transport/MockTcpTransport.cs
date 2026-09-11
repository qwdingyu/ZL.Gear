using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Communication.Abstractions;

namespace ZL.Gear.Communication.Transport
{
    /// <summary>
    /// Mock TCP 传输层
    /// 模拟 TCP 设备通信，支持响应脚本
    /// </summary>
    public class MockTcpTransport : ITransport
    {
        private readonly MockTcpServer _server;
        private readonly string _deviceCode;
        private bool _disposed;

        public Stream DataStream { get; }

        public MockTcpTransport(string deviceCode, int port = 0)
        {
            _deviceCode = deviceCode;
            _server = new MockTcpServer(deviceCode, port);
            DataStream = new MemoryStream();
        }

        public bool IsConnected => _server.IsRunning;

        public Task ConnectAsync(CancellationToken token = default)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [INFO] [{_deviceCode}] MockTCP 连接已建立 (Port: {_server.Port})");
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken token = default)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [INFO] [{_deviceCode}] MockTCP 连接已断开");
            return Task.CompletedTask;
        }

        public bool IsHealthy() => _server.IsRunning;

        public void ClearBuffers() { }

        public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            return _server.ReceiveAsync(buffer, offset, count, token);
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken token = default)
        {
            return await _server.ReceiveAsync(token);
        }

        public Task SendAsync(byte[] data, CancellationToken token = default)
        {
            try
            {
                string cmd = System.Text.Encoding.ASCII.GetString(data).Trim();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [DEBUG] [{_deviceCode}] Send: {cmd}");
                _server.NotifySent(cmd);
            }
            catch { }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _server?.Dispose();
                if (DataStream is MemoryStream ms) ms.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return new ValueTask(Task.CompletedTask);
        }
    }

    /// <summary>
    /// Mock TCP 服务器
    /// 内部使用内存流模拟 TCP 通信
    /// </summary>
    public class MockTcpServer : IDisposable
    {
        private readonly string _deviceCode;
        private readonly Random _random = new();
        private readonly ConcurrentQueue<string> _responseQueue = new();
        private readonly MemoryStream _inputStream = new();
        private readonly MemoryStream _outputStream = new();
        private bool _disposed;

        public int Port { get; }
        public bool IsRunning => !_disposed;

        public MockTcpServer(string deviceCode, int port = 0)
        {
            _deviceCode = deviceCode;
            Port = port > 0 ? port : 9000 + Math.Abs(deviceCode.GetHashCode()) % 1000;
            SetupResponseScript();
        }

        private readonly ConcurrentDictionary<string, string> _tags = new();

        private void SetupResponseScript(string? lastCommand = null)
        {
            // 处理 PLC 状态逻辑
            if (_deviceCode.Contains("PLC") && lastCommand != null)
            {
                if (lastCommand.StartsWith("SET Cylinder_01 EXTEND")) _tags["Sensor_Limit_Arr"] = "ARRIVED";
                if (lastCommand.StartsWith("SET Cylinder_01 RETRACT")) _tags["Sensor_Limit_Arr"] = "IDLE";
            }

            if (_deviceCode.Contains("ResTester") || _deviceCode.Contains("Keithley"))
            {
                // 模拟 10Ω 标称电阻：对称小噪声 ±0.05Ω。
                // 原为 10.0 + NextDouble()*5.0（10~15Ω），与 FullIntegrationTest /
                // ResistanceNoise_3in1 的 ActualRes < ExpectedRes(10.5) 断言冲突，令门禁呈 ~90% flaky。
                double val = 10.0 + (_random.NextDouble() - 0.5) * 0.1; 
                EnqueueResponse($"{val:F2}\n");
            }
            else if (_deviceCode.Contains("NoiseMeter"))
            {
                double val = 50.0 + _random.NextDouble() * 20.0;
                EnqueueResponse($"{val:F1}\n");
            }
            else if (_deviceCode.Contains("PLC"))
            {
                if (lastCommand?.StartsWith("GET") == true)
                {
                    var tag = lastCommand.Replace("GET ", "").Trim();
                    EnqueueResponse(_tags.TryGetValue(tag, out var v) ? $"{v}\n" : "UNKNOWN\n");
                }
                else
                {
                    EnqueueResponse("OK\n");
                }
            }
            else if (_deviceCode.Contains("PowerSupply"))
            {
                // 模拟 12V 标称电源：对称小噪声 ±0.05V。
                // 原实现为 12.0 + NextDouble()（跨到 13.0），与 FullIntegrationTest 的
                // |ActualVoltage-12.0| < 0.5 断言冲突，令 check_release 第 4 步呈 ~50% flaky。
                double val = 12.0 + (_random.NextDouble() - 0.5) * 0.1;
                EnqueueResponse($"{val:F2}\n");
            }
            else
            {
                EnqueueResponse("1\n");
            }
        }

        public void EnqueueResponse(string response)
        {
            _responseQueue.Enqueue(response);
        }

        public void NotifySent(string command)
        {
            // 清理旧的响应，根据新命令准备
            while (_responseQueue.TryDequeue(out _)) { }
            SetupResponseScript(command);
        }

        public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            if (_responseQueue.TryDequeue(out var response))
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(response);
                var length = Math.Min(bytes.Length, count);
                Buffer.BlockCopy(bytes, 0, buffer, offset, length);

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [DEBUG] [{_deviceCode}] Response: {response.Trim()}");

                return Task.FromResult(length);
            }
            return Task.FromResult(0);
        }

        public Task<byte[]> ReceiveAsync(CancellationToken token = default)
        {
            if (_responseQueue.TryDequeue(out var response))
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [DEBUG] [{_deviceCode}] Response: {response.Trim()}");
                return Task.FromResult(System.Text.Encoding.ASCII.GetBytes(response));
            }
            return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("\n"));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _inputStream.Dispose();
                _outputStream.Dispose();
            }
        }
    }

    /// <summary>
    /// Mock 传输工厂
    /// 根据设备代码创建对应的 Mock 传输层
    /// </summary>
    public static class MockTransportFactory
    {
        private static readonly ConcurrentDictionary<string, MockTcpTransport> _transports = new();

        public static ITransport CreateTransport(string deviceCode, string protocol = "MOCK")
        {
            return _transports.GetOrAdd(deviceCode, code =>
            {
                int port = GetPortForDevice(code);
                return new MockTcpTransport(code, port);
            });
        }

        private static int GetPortForDevice(string deviceCode)
        {
            if (deviceCode.Contains("ResTester")) return 9101;
            if (deviceCode.Contains("NoiseMeter")) return 9102;
            if (deviceCode.Contains("PLC")) return 9103;
            if (deviceCode.Contains("PowerSupply")) return 9104;
            if (deviceCode.Contains("Keithley")) return 9105;
            return 9100 + Math.Abs(deviceCode.GetHashCode()) % 100;
        }

        public static void Reset()
        {
            foreach (var transport in _transports.Values)
            {
                transport.Dispose();
            }
            _transports.Clear();
        }
    }
}
