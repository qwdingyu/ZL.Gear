using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using ZL.Gear.Core.Devices.Dto;

namespace ZL.Gear.Communication.Models
{
    public class SerialParameters
    {
        [JsonProperty("Port")]
        public string Port { get; set; }

        [JsonProperty("BaudRate")]
        public int BaudRate { get; set; }

        [JsonProperty("DataBits")]
        public int DataBits { get; set; }

        [JsonProperty("Parity")]
        public string Parity { get; set; }

        [JsonProperty("StopBits")]
        public string StopBits { get; set; }
    }

    public class PlcParameters
    {
        [JsonProperty("DeviceType")]
        public string DeviceType { get; set; }

        [JsonProperty("PlcIp")]
        public string PlcIp { get; set; }

        [JsonProperty("PlcRack")]
        public int PlcRack { get; set; }

        [JsonProperty("PlcSlot")]
        public int PlcSlot { get; set; }

        [JsonProperty("TagFilePath")]
        public string TagFilePath { get; set; }
    }
    public class DeviceTypeInfo
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string DefaultConnectionType { get; set; }
        public string[] RequiredParameters { get; set; } = Array.Empty<string>();
        public string Description { get; set; }

        public static DeviceTypeInfo Generic { get; } = new DeviceTypeInfo
        {
            Name = "Generic Device",
            Category = "Other",
            Description = "通用设备类型"
        };
    }
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class DevicesRoot
    {
        [JsonProperty("Devices")]
        public List<DeviceConfig> Devices { get; set; }
    }
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ConnectionCfg
    {
        [JsonProperty("Protocol")]
        public string Protocol { get; set; } = "Unknown";

        [JsonProperty("Parameters")]
        public JObject Parameters { get; set; } = new JObject();
        //public ConnectionCfg(string protocol, JObject parameters)
        //{
        //    Protocol = protocol;
        //    Parameters = parameters;
        //}
    }

    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class DeviceConfig
    {
        [JsonProperty("Type")]
        public string Type { get; set; }
        [JsonProperty("DeviceCode")]
        public string DeviceCode { get; set; }
        [JsonProperty("DeviceName")]
        public string DeviceName { get; set; }
        
        [JsonIgnore] // 避免序列化冲突，主要用于兼容旧代码
        public string ConnectionString { get; set; }

        //public Dictionary<string, object> Settings { get; set; } = new Dictionary<string, object>();
        //[JsonIgnore]
        //public Dictionary<string, object> Settings
        //{
        //    get => TypeSpecific?.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
        //    set => TypeSpecific = value != null ? JObject.FromObject(value) : new JObject();
        //}
        [JsonProperty("ConnectionCfg")]
        public ConnectionCfg ConnectionCfg { get; set; } = new ConnectionCfg();

        // 动态类型特定配置
        [JsonProperty("TypeSpecific")]
        public JObject TypeSpecific { get; set; } = new JObject();
        /// <summary>
        /// 启用 禁用
        /// </summary>
        [JsonProperty("Enable")]
        public bool Enable { get; set; } = true;
        /// <summary>
        /// 是否再UI上显示状态
        /// </summary>
        [JsonProperty("ShowUi")]
        public bool ShowUi { get; set; } = false;

        [JsonIgnore]
        public string FullName => $"{DeviceCode} - {DeviceName}";

        /// <summary>
        /// 定义设备特定的命令集。
        /// 允许通过配置文件(devices.json)直接定义简单的 SCPI 命令，无需编写额外的 CommandHandler。
        /// </summary>
        [JsonProperty("Commands")]
        public Dictionary<string, CommandSpec> Commands { get; set; } = new Dictionary<string, CommandSpec>();
    }
    /// <summary>
    /// 可复用的握手/初始化执行器
    /// </summary>
    public sealed class HandshakeSpec
    {
        public bool OnLoad { get; set; } = true;
        public string Cmd { get; set; }
        public bool ExpectResponse { get; set; } = false;
        public int TimeoutMs { get; set; } = 300;
        public int Retries { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 200;
        public int DelayAfterMs { get; set; } = 0;

        public VerifySpec Verify { get; set; } // 可空
    }

    public sealed class VerifySpec
    {
        public string Cmd { get; set; } = "*IDN?\n";
        public string Contains { get; set; }   // 例如 "HP3544"
    }

    public sealed class InitStepSpec
    {
        public string Cmd { get; set; }
        public bool ExpectResponse { get; set; } = false;
        public int TimeoutMs { get; set; } = 300;
        public int DelayAfterMs { get; set; } = 0;
    }
}