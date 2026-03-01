using System.Collections.Generic;

namespace ZL.Gear.Core.Devices
{
    /// <summary>
    /// 统一设备配置
    /// </summary>
    public class UnifiedDeviceConfig
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Enabled { get; set; } = true;
        public TransportConfig Transport { get; set; }
        public ProtocolConfigRef Protocol { get; set; }
        public Dictionary<string, object> Extra { get; set; }
    }

    /// <summary>
    /// 传输层配置
    /// </summary>
    public class TransportConfig
    {
        public string Type { get; set; }
        public string Port { get; set; }
        public int? BaudRate { get; set; }
        public string Host { get; set; }
        public int? PortNum { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    /// <summary>
    /// 协议配置
    /// </summary>
    public class ProtocolConfigRef
    {
        public string Terminator { get; set; } = "\n";
        public int InterCommandWaitMs { get; set; } = 50;
        public string Ref { get; set; }
        public Dictionary<string, CommandConfig> Commands { get; set; }
    }

    /// <summary>
    /// 命令配置
    /// </summary>
    public class CommandConfig
    {
        public string Template { get; set; }
        public int WaitAfterMs { get; set; }
        public ResponseParserConfig Parser { get; set; }
    }

    /// <summary>
    /// 响应解析器配置
    /// </summary>
    public class ResponseParserConfig
    {
        public string Type { get; set; } = "Trim";
        public string Pattern { get; set; }
        public int Index { get; set; }
        public string TargetType { get; set; } = "String";
    }

    /// <summary>
    /// 设备配置集合（JSON 根对象）
    /// </summary>
    public class DeviceConfigCollection
    {
        public string Version { get; set; }
        public string Description { get; set; }
        public List<UnifiedDeviceConfig> Devices { get; set; }
    }
}
