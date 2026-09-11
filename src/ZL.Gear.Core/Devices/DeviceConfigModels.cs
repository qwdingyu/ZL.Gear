using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        /// <summary>
        /// 允许在设备层级直接定义简单的 SCPI 命令
        /// </summary>
        public Dictionary<string, CommandConfig> Commands { get; set; }
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
        private string _template;

        /// <summary>
        /// 指令字符串（支持 ${arg} 占位符）。
        /// 在 JSON 中推荐使用 "Cmd"，但也支持旧的 "Template" 字段。
        /// </summary>
        [JsonProperty("Cmd")]
        public string Cmd { get => _template; set => _template = value; }

        [JsonProperty("Template")]
        public string Template { get => _template; set => _template = value; }

        public int WaitAfterMs { get; set; }

        public bool ExpectResponse { get; set; }

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
