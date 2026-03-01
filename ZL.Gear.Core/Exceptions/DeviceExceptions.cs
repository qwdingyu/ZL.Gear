using System;

namespace ZL.Gear.Core.Exceptions
{
    /// <summary>
    /// 设备配置异常
    /// </summary>
    public class DeviceConfigException : Exception
    {
        public string? DeviceCode { get; }
        public string? ConfigPath { get; }

        public DeviceConfigException(string message, string? deviceCode = null, string? configPath = null) 
            : base(message)
        {
            DeviceCode = deviceCode;
            ConfigPath = configPath;
        }

        public DeviceConfigException(string message, Exception innerException, string? deviceCode = null) 
            : base(message, innerException)
        {
            DeviceCode = deviceCode;
        }

        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(DeviceCode)) parts.Add($"DeviceCode={DeviceCode}");
            if (!string.IsNullOrEmpty(ConfigPath)) parts.Add($"ConfigPath={ConfigPath}");
            
            var details = parts.Count > 0 ? $" ({string.Join(", ", parts)})" : "";
            return $"{GetType().Name}{details}: {Message}";
        }
    }

    /// <summary>
    /// 设备创建异常
    /// </summary>
    public class DeviceCreateException : Exception
    {
        public string DeviceCode { get; }
        public string? DeviceType { get; }

        public DeviceCreateException(string message, string deviceCode, string? deviceType = null) 
            : base(message)
        {
            DeviceCode = deviceCode;
            DeviceType = deviceType;
        }

        public DeviceCreateException(string message, Exception innerException, string deviceCode) 
            : base(message, innerException)
        {
            DeviceCode = deviceCode;
        }

        public override string ToString()
        {
            var details = $" DeviceCode={DeviceCode}";
            if (!string.IsNullOrEmpty(DeviceType)) details += $", DeviceType={DeviceType}";
            return $"{GetType().Name}{details}: {Message}";
        }
    }

    /// <summary>
    /// 设备连接异常
    /// </summary>
    public class DeviceConnectionException : Exception
    {
        public string DeviceCode { get; }
        public string TransportType { get; }

        public DeviceConnectionException(string message, string deviceCode, string transportType) 
            : base(message)
        {
            DeviceCode = deviceCode;
            TransportType = transportType;
        }

        public DeviceConnectionException(string message, Exception innerException, string deviceCode) 
            : base(message, innerException)
        {
            DeviceCode = deviceCode;
        }

        public override string ToString()
        {
            return $"{GetType().Name}(Device={DeviceCode}, Transport={TransportType}): {Message}";
        }
    }

    /// <summary>
    /// 协议加载异常
    /// </summary>
    public class ProtocolLoadException : Exception
    {
        public string ProtocolName { get; }
        public string? ProtocolPath { get; }

        public ProtocolLoadException(string message, string protocolName, string? protocolPath = null) 
            : base(message)
        {
            ProtocolName = protocolName;
            ProtocolPath = protocolPath;
        }

        public ProtocolLoadException(string message, Exception innerException, string protocolName) 
            : base(message, innerException)
        {
            ProtocolName = protocolName;
        }

        public override string ToString()
        {
            var details = $" Protocol={ProtocolName}";
            if (!string.IsNullOrEmpty(ProtocolPath)) details += $", Path={ProtocolPath}";
            return $"{GetType().Name}{details}: {Message}";
        }
    }

    /// <summary>
    /// 设备忙异常（用于并发访问场景）
    /// </summary>
    public class DeviceBusyException : Exception
    {
        public string DeviceCode { get; }
        public TimeSpan WaitTime { get; }

        public DeviceBusyException(string deviceCode, TimeSpan waitTime) 
            : base($"设备 {deviceCode} 已被占用，等待 {waitTime.TotalSeconds:F1} 秒后超时")
        {
            DeviceCode = deviceCode;
            WaitTime = waitTime;
        }
    }

    /// <summary>
    /// 命令执行异常
    /// </summary>
    public class DeviceCommandException : Exception
    {
        public string DeviceCode { get; }
        public string Command { get; }

        public DeviceCommandException(string message, string deviceCode, string command) 
            : base(message)
        {
            DeviceCode = deviceCode;
            Command = command;
        }

        public DeviceCommandException(string message, Exception innerException, string deviceCode, string command) 
            : base(message, innerException)
        {
            DeviceCode = deviceCode;
            Command = command;
        }

        public override string ToString()
        {
            return $"{GetType().Name}(Device={DeviceCode}, Command={Command}): {Message}";
        }
    }
}
