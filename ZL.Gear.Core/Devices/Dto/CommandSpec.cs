using System.Collections.Generic;
using ZL.Gear.Core.Protocol.Parsers;

namespace ZL.Gear.Core.Devices.Dto
{
    /// <summary>
    /// 定义一个设备命令的完整、统一的规范。
    /// 这个类是不可变的，用于描述一个命令的方方面面：
    /// - 要发送什么 (CommandTemplate)
    /// - 如何处理参数 (DefaultArgs)
    /// - 执行过程中的行为 (DelayAfterMs, QueryAfterSend)
    /// - 如何接收和解析响应 (ExpectResponse, ParserKey, Delimiter, etc.)
    /// </summary>
    public sealed class CommandSpec
    {
        #region 1. 命令内容 (What to Send)
        /// <summary>
        /// 要发送给设备的命令模板字符串。
        /// 支持使用花括号 { } 的占位符，例如 ":MEASure:ITEM? VRMS,CHANnel{Channel}"。
        /// 在执行时，占位符将被 'args' 字典中同名的键值替换。
        /// </summary>
        public string CommandTemplate { get; set; }
        /// <summary>
        /// 为命令模板中的占位符提供默认值。
        /// 如果 ExecuteAsync 调用中 'args' 字典缺少某个键，将使用此处的默认值。
        /// 例: 对于 CommandTemplate="{Cmd}", DefaultArgs={"Cmd", "*IDN?"}，
        //       直接调用 ExecuteAsync("Query") 就能发送 "*IDN?"。
        /// </summary>
        public Dictionary<string, object> DefaultArgs { get; set; }
        #endregion
        #region 2. 执行行为 (How to Execute)
        /// <summary>
        /// 在主命令发送后，需要额外等待的毫秒数。
        /// 对于某些需要时间来稳定状态的设置命令非常有用。默认为 0。
        /// </summary>
        public int DelayAfterMs { get; set; } = 0;
        /// <summary>
        /// "设置并验证"模式：在主命令发送后，立即发送此查询命令以验证结果。
        /// 例如,主命令是 "VOLT 5.0", QueryAfterSend 可以是 "VOLT?"。
        /// 响应和解析将针对此查询命令。
        /// </summary>
        public string QueryAfterSend { get; set; }
        #endregion
        #region 3. 响应处理 (How to Handle Response)
        /// <summary>
        /// 是否期望设备返回响应数据。
        /// - 对于查询命令 (如 "VOLT?"), 应为 true。
        /// - 对于纯设置命令 (如 "SYST:REM"), 应为 false。
        /// - 如果设置了 QueryAfterSend, 此属性通常也应为 true。
        /// 默认为 true。
        /// </summary>
        public bool ExpectResponse { get; set; } = false;

        /// <summary>
        /// 用于查找响应解析器的唯一键。
        /// 例如 "scpi:double", "kv:double", "raw:string"。
        /// IProtocolHandler 用此键从工厂获取正确的解析逻辑。
        /// 如果 ExpectResponse 为 false，此属性被忽略。
        /// </summary>
        public string ParserKey { get; set; }
        /// <summary>
        /// （可选）直接提供一个解析器实例，覆盖 ParserKey 的查找逻辑。
        /// 用于动态或一次性的特殊解析场景。
        /// </summary>
        public CommonParsers.FrameParser Parser { get; set; } // 假设您有这个类型
        #endregion
        #region 4. 元数据 (Metadata)
        /// <summary>
        /// 命令的简短描述，用于日志记录、调试或生成UI/文档。
        /// </summary>
        public string Description { get; set; }
        #endregion
    }

}
