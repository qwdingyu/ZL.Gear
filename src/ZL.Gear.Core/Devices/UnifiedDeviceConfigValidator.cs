using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ZL.Gear.Core.Abstractions;

namespace ZL.Gear.Core.Devices
{
    /// <summary>
    /// 设备配置验证结果
    /// </summary>
    public class ConfigValidationResult
    {
        // P2-3：Warning 仅作诊断提示，不应把"建议项缺失"判定为配置无效；
        // 生产环境如需严格门禁，由宿主配置 WarningsAsErrors 策略显式升级。
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public void AddError(string message) => Errors.Add(message);
        public void AddWarning(string message) => Warnings.Add(message);

        public override string ToString()
        {
            if (IsValid) return "验证通过";
            return $"错误: {string.Join("; ", Errors)}, 警告: {string.Join("; ", Warnings)}";
        }
    }

    /// <summary>
    /// 设备配置验证器
    /// 
    /// 验证内容：
    /// 1. 必填字段（Code, Type）
    /// 2. 传输配置（串口必须有 Port，TCP 必须有 Host）
    /// 3. 协议配置
    /// 
    /// 使用方式：
    /// ```csharp
    /// var validator = new UnifiedDeviceConfigValidator();
    /// var result = validator.Validate(config);
    /// if (!result.IsValid)
    /// {
    ///     Console.WriteLine($"配置错误: {string.Join(", ", result.Errors)}");
    /// }
    /// ```
    /// </summary>
    public class UnifiedDeviceConfigValidator
    {
        private static readonly HashSet<string> ValidTransportTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Serial", "Tcp", "Mock", "Usb", "Visa"
        };

        private static readonly HashSet<string> ValidDeviceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SerialScpi", "TcpScpi", "Mock", "Plc", "Http", "Usb"
        };

        private readonly IDeviceLogger _log;

        public UnifiedDeviceConfigValidator(IDeviceLogger? logger = null)
        {
            _log = logger ?? NullDeviceLogger.Instance;
        }

        /// <summary>
        /// 验证单个设备配置
        /// </summary>
        public ConfigValidationResult Validate(UnifiedDeviceConfig config)
        {
            var result = new ConfigValidationResult();

            if (config == null)
            {
                result.AddError("配置对象不能为 null");
                return result;
            }

            ValidateRequiredFields(config, result);
            ValidateTransport(config.Transport, result);
            ValidateProtocol(config.Protocol, result);

            return result;
        }

        /// <summary>
        /// 批量验证配置集合
        /// </summary>
        public Dictionary<string, ConfigValidationResult> ValidateAll(Dictionary<string, UnifiedDeviceConfig> configs)
        {
            var results = new Dictionary<string, ConfigValidationResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in configs)
            {
                var result = Validate(kvp.Value);
                results[kvp.Key] = result;

                // 记录警告
                foreach (var warning in result.Warnings)
                {
                    _log.Warn("配置验证警告 [{DeviceCode}]: {Warning}", kvp.Key, warning);
                }

                // 记录错误
                foreach (var error in result.Errors)
                {
                    _log.Error("配置验证错误 [{DeviceCode}]: {Error}", kvp.Key, error);
                }
            }

            return results;
        }

        private void ValidateRequiredFields(UnifiedDeviceConfig config, ConfigValidationResult result)
        {
            // Code 是必填
            if (string.IsNullOrWhiteSpace(config.Code))
            {
                result.AddError("设备 Code 是必填字段");
            }
            else if (!Regex.IsMatch(config.Code, @"^[A-Za-z0-9_]+$"))
            {
                result.AddError("设备 Code 只能包含字母、数字和下划线");
            }

            // Type 是必填
            if (string.IsNullOrWhiteSpace(config.Type))
            {
                result.AddError("设备 Type 是必填字段");
            }
            else if (!ValidDeviceTypes.Contains(config.Type))
            {
                result.AddWarning($"未知的设备类型 '{config.Type}'，支持的类型: {string.Join(", ", ValidDeviceTypes)}");
            }

            // Name 是可选，但建议设置
            if (string.IsNullOrWhiteSpace(config.Name))
            {
                result.AddWarning("设备 Name 未设置，将使用 Code 作为显示名称");
            }
        }

        private void ValidateTransport(TransportConfig? transport, ConfigValidationResult result)
        {
            if (transport == null)
            {
                result.AddError("设备 Transport 配置不能为 null");
                return;
            }

            // 验证传输类型
            var type = transport.Type?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(type))
            {
                result.AddError("传输类型 Type 是必填");
                return;
            }

            if (!ValidTransportTypes.Contains(type))
            {
                result.AddWarning($"未知的传输类型 '{transport.Type}'，将使用 Mock 传输");
            }

            // 根据类型验证特定字段
            switch (type)
            {
                case "SERIAL":
                    ValidateSerialTransport(transport, result);
                    break;
                case "TCP":
                    ValidateTcpTransport(transport, result);
                    break;
                case "USB":
                case "VISA":
                    ValidateUsbTransport(transport, result);
                    break;
            }
        }

        private void ValidateSerialTransport(TransportConfig transport, ConfigValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(transport.Port))
            {
                result.AddError("串口传输必须配置 Port（如 COM3 或 /dev/ttyUSB0）");
            }

            if (transport.BaudRate <= 0)
            {
                result.AddWarning("串口波特率 BaudRate 未设置或无效，将使用默认值 9600");
            }
        }

        private void ValidateTcpTransport(TransportConfig transport, ConfigValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(transport.Host))
            {
                result.AddError("TCP 传输必须配置 Host（如 192.168.1.100）");
            }

            if (transport.PortNum <= 0 || transport.PortNum > 65535)
            {
                result.AddError("TCP 传输必须配置有效的 PortNum (1-65535)");
            }
        }

        private void ValidateUsbTransport(TransportConfig transport, ConfigValidationResult result)
        {
            // USB 和 VISA 通常需要额外的配置信息
            if (transport.Parameters == null || transport.Parameters.Count == 0)
            {
                result.AddWarning("USB/VISA 传输建议配置 Parameters（如 VID, PID）");
            }
        }

        private void ValidateProtocol(ProtocolConfigRef? protocol, ConfigValidationResult result)
        {
            if (protocol == null)
            {
                // 协议是可选的，如果没有协议则使用默认配置
                result.AddWarning("未配置协议，将使用默认配置（Terminator=\\n）");
                return;
            }

            // 同时配置了 Ref 和 Commands
            if (!string.IsNullOrWhiteSpace(protocol.Ref) && protocol.Commands != null && protocol.Commands.Count > 0)
            {
                result.AddWarning("同时配置了 Ref 和 Commands，将优先使用内联 Commands");
            }

            // 验证命令配置
            if (protocol.Commands != null)
            {
                ValidateCommands(protocol.Commands, result);
            }
        }

        private void ValidateCommands(Dictionary<string, CommandConfig> commands, ConfigValidationResult result)
        {
            foreach (var cmd in commands)
            {
                var cmdName = cmd.Key;

                if (string.IsNullOrWhiteSpace(cmdName))
                {
                    result.AddError("命令名称不能为空");
                    continue;
                }

                if (cmd.Value == null)
                {
                    result.AddWarning($"命令 '{cmdName}' 的配置对象为 null");
                    continue;
                }

                // 验证命令模板
                if (string.IsNullOrWhiteSpace(cmd.Value.Template))
                {
                    result.AddWarning($"命令 '{cmdName}' 未配置 Template，将使用空模板");
                }

                // 验证解析器配置
                ValidateParserConfig(cmd.Value.Parser, cmdName, result);
            }
        }

        private void ValidateParserConfig(ResponseParserConfig? parser, string commandName, ConfigValidationResult result)
        {
            if (parser == null)
            {
                return;
            }

            var validParserTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "None", "Trim", "Regex", "Split", "Json"
            };

            if (!string.IsNullOrWhiteSpace(parser.Type) && !validParserTypes.Contains(parser.Type))
            {
                result.AddWarning($"命令 '{commandName}' 的解析器类型 '{parser.Type}' 未知");
            }

            // 如果是 Regex 类型，必须提供 Pattern
            if (string.Equals(parser.Type, "Regex", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(parser.Pattern))
                {
                    result.AddError($"命令 '{commandName}' 使用 Regex 解析器，必须配置 Pattern");
                }
                else
                {
                    try
                    {
                        new Regex(parser.Pattern);
                    }
                    catch (Exception)
                    {
                        result.AddError($"命令 '{commandName}' 的正则表达式 Pattern 无效: {parser.Pattern}");
                    }
                }
            }
        }
    }
}
