using NationalInstruments.Visa;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Communication.Models;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Events;

namespace ZL.Gear.Communication.Transport
{
    public class NiVisaTransport : ITransport, IUsbTmcTransports
    {
        private MessageBasedSession _session;
        private readonly string _visaAddress;
        private readonly string _deviceKey;
        private DeviceState _state = DeviceState.Unknown;

        /// <summary>
        /// 这里的 deviceKey 应该是 DeviceConfig 中的 DeviceCode 或 DeviceName，用于通知状态变更
        /// </summary>
        public NiVisaTransport(ConnectionCfg connectionConfig, string deviceKey)
        {
            if (connectionConfig?.Parameters != null && connectionConfig.Parameters.ContainsKey("ConnectionString"))
            {
                _visaAddress = connectionConfig.Parameters["ConnectionString"]?.ToString();
            }
            else
            {
                _visaAddress = "ASRL1::INSTR"; // Default
            }

            _deviceKey = deviceKey;
            
            // 尝试同步连接或延迟到 ConnectAsync
        }

        public DeviceState State => _state;
        public string VisaAddress => _visaAddress;
        public bool IsConnected => _session != null && !_session.IsDisposed;
        public Stream DataStream => Stream.Null;

        public bool IsHealthy()
        {
            // 简单检查会话状态
            return IsConnected;
        }

        void SetState(DeviceState newState, string info = "")
        {
            if (_state == newState) return;
            _state = newState;
            DeviceNotifier.Notify(_deviceKey, newState, info);
        }

        public Task ConnectAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (_session != null && !_session.IsDisposed) return;

                    SetState(DeviceState.Connecting);
                    var rm = new ResourceManager();
                    _session = (MessageBasedSession)rm.Open(_visaAddress);
                    
                    // 默认超时
                    _session.TimeoutMilliseconds = 2000;
                    
                    SetState(DeviceState.Online, "VISA Connected");
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Error, ex.Message);
                    throw;
                }
            }, token);
        }

        public Task DisconnectAsync(CancellationToken token = default)
        {
             return Task.Run(() => 
             {
                 try
                 {
                     if (_session != null)
                     {
                         _session.Dispose();
                         _session = null;
                     }
                     SetState(DeviceState.Offline);
                 }
                 catch { }
             }, token);
        }
        
        // 兼容旧接口
        public Task DisconnectAsync() => DisconnectAsync(CancellationToken.None);

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (_session == null) throw new InvalidOperationException("Not connected");
                _session.RawIO.Write(data);
            }, token);
        }

        // 兼容 SendAsync(string, ct)
        public Task SendAsync(string msg, CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (_session == null) throw new InvalidOperationException("Not connected");
                _session.RawIO.Write(msg);
            }, token);
        }

        public Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (_session == null) throw new InvalidOperationException("Not connected");
                
                // NI-VISA Read implementation
                // 注意：RawIO.Read 可能会抛出 Timeout 异常
                // 原生 API 可能是 Read(byte[], long count) 或者 ReadString
                // 这里为了稳健，假设是基于字符的仪器，使用 ReadString 然后转换
                // 如果是二进制流，应该使用 _session.RawIO.Read(buffer, offset, count) 如果 API 支持
                // 根据 NI-VISA .NET 文档，RawIO.Read(byte[] buffer, long count) 存在
                
                try 
                {
                    // 尝试二进制读取
                    // 这里的 API 签名可能需要适配具体的 VISA 库版本
                     var readBuffer = _session.RawIO.Read(count);
                     if (readBuffer != null)
                     {
                         int toCopy = Math.Min(readBuffer.Length, count);
                         Array.Copy(readBuffer, 0, buffer, offset, toCopy);
                         return toCopy;
                     }
                     return 0;
                }
                catch (Exception)
                {
                    // Fallback to string if binary read fails or not supported
                     var s = _session.RawIO.ReadString();
                     var b = System.Text.Encoding.ASCII.GetBytes(s);
                     int toCopy = Math.Min(b.Length, count);
                     Array.Copy(b, 0, buffer, offset, toCopy);
                     return toCopy;
                }
            }, token);
        }
        
        public async Task<byte[]> ReceiveAsync(CancellationToken token = default)
        {
            // 简单读取所有（直到结束符）
            // 缓冲区设大一点
            byte[] buf = new byte[4096];
            int count = await ReceiveAsync(buf, 0, 4096, token);
            var result = new byte[count];
            Array.Copy(buf, result, count);
            return result;
        }

        public void ClearBuffers()
        {
             if (_session != null) _session.Clear();
        }
        
        public void ClearInputBuffer() => ClearBuffers();

        public ValueTask DisposeAsync()
        {
             return new ValueTask(DisconnectAsync());
        }

        // IUsbTmcTransports implementation
        public void Dispose()
        {
            DisconnectAsync().Wait();
        }

        public void Write(string msg, CancellationToken token)
        {
            SendAsync(msg, token).GetAwaiter().GetResult();
        }

        public string Query(string msg, CancellationToken token, int timeoutMs = 5000)
        {
            return Task.Run(async () =>
            {
                // Note: timeoutMs is currently ignored in this wrapper, using session timeout instead
                await SendAsync(msg, token);
                var bytes = await ReceiveAsync(token);
                return System.Text.Encoding.ASCII.GetString(bytes).Trim();
            }, token).GetAwaiter().GetResult();
        }
    }
}
