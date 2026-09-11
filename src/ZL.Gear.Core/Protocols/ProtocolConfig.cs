using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Protocols
{
    /// <summary>
    /// 定义设备的通讯协议配置，实现逻辑与数据的完全解耦。
    /// 可以通过 JSON 文件加载此配置，从而适配不同的设备解析规则。
    /// </summary>
    public class ProtocolConfig
    {
        /// <summary>
        /// 协议名称 (如 "Keithley2000", "Chroma62000P")
        /// </summary>
        public string ProtocolName { get; set; }

        /// <summary>
        /// 全局终止符 (如 "\r\n", "\n")，用于发送和接收时的自动处理。
        /// 如果命令本身包含终止符，则优先使用命令中的。
        /// </summary>
        public string Terminator { get; set; } = "\n";

        /// <summary>
        /// 默认的读取超时 (毫秒)
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 2000;

        /// <summary>
        /// 编码格式: "ASCII" (默认) 或 "Hex" (十六进制字符串，如 "01 03 00")
        /// </summary>
        public string Encoding { get; set; } = "ASCII";

        /// <summary>
        /// 命令间的固定等待时间 (毫秒)，用于防止指令发送过快
        /// </summary>
        public int InterCommandWaitMs { get; set; } = 50;

        /// <summary>
        /// 命令定义集合。Key 是逻辑操作名 (如 "MeasureResistance", "SetVoltage")
        /// </summary>
        /// <summary>
        /// 命令定义集合。Key 是逻辑操作名 (如 "MeasureResistance", "SetVoltage")
        /// </summary>
        public Dictionary<string, CommandDefinition> Commands { get; set; } = new Dictionary<string, CommandDefinition>();

        /// <summary>
        /// 默认读取策略 (当命令未指定时使用)
        /// </summary>
        public ReadStrategyDefinition ReadStrategy { get; set; }
    }

    public class CommandDefinition
    {
        /// <summary>
        /// 发送的命令模板。支持格式化参数，如 "VOLT {0}"
        /// </summary>
        public string CommandTemplate { get; set; }

        /// <summary>
        /// 发送后的等待时间 (毫秒)
        /// </summary>
        public int WaitAfterMs { get; set; }

        /// <summary>
        /// 响应解析器配置。如果为空，则表示不关心返回值 (或只等待 OK)
        /// </summary>
        public ResponseParserDefinition Parser { get; set; }
        
        /// <summary>
        /// 读取策略 (用于解决粘包/分包问题)
        /// </summary>
        public ReadStrategyDefinition ReadStrategy { get; set; }
    }
    
    public class ReadStrategyDefinition
    {
        /// <summary>
        /// 策略类型: "Terminator"(默认,读到结束符), "FixedLength"(读固定长度), "Available"(读缓冲区现有)
        /// </summary>
        public string Type { get; set; } = "Terminator";
        
        /// <summary>
        /// 如果 Type="Terminator"，则此字段指定结束符 (默认使用 ProtocolConfig.Terminator)
        /// </summary>
        public string Terminator { get; set; }
        
        /// <summary>
        /// 如果 Type="FixedLength"，则此字段指定字节数
        /// </summary>
        public int Length { get; set; }
    }

    public class ResponseParserDefinition
    {
        /// <summary>
        /// 解析策略类型: "Regex", "Json", "Trim", "Split", "None"
        /// </summary>
        public string Type { get; set; } = "None";

        /// <summary>
        /// 解析模式。
        /// 对于 Regex，这是正则表达式 (如 "^([\d.]+)");
        /// 对于 Json，这是 JsonPath (如 "$.value");
        /// 对于 Split，这是分隔符 (如 ",")
        /// </summary>
        public string Pattern { get; set; }

        /// <summary>
        /// 如果是 Split 或 Regex Group，指定索引
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// 目标数据类型: "Double", "String", "Int", "Boolean"
        /// </summary>
        public string TargetType { get; set; } = "String";
    }
}
