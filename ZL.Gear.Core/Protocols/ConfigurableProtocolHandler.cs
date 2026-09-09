using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using ZL.Gear.Core.Devices.Abstractions;
using Newtonsoft.Json.Linq;

namespace ZL.Gear.Core.Protocols
{
    /// <summary>
    /// 可配置的设备协议处理器。
    /// 根据 ProtocolConfig 中的定义，动态执行指令构建和响应解析。
    /// </summary>
    public class ConfigurableProtocolHandler : IUniversalProtocolHandler
    {
        private readonly ProtocolConfig _config;
        private readonly System.Collections.Generic.List<byte> _receiveBuffer = new();
        private DateTime _lastExecutionTime = DateTime.MinValue;

        public ConfigurableProtocolHandler(ProtocolConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public async Task<object> ExecuteAsync(ITransport transport, string commandKey, object args, CancellationToken token = default)
        {
            if (!_config.Commands.TryGetValue(commandKey, out var cmdDef))
            {
                throw new InvalidOperationException($"Protocol '{_config.ProtocolName}' does not define command '{commandKey}'");
            }

            // 1. 构建命令 (Format Command)
            string commandToSend = cmdDef.CommandTemplate;
            
            try 
            {
                if (args is System.Collections.IDictionary dict)
                {
                    // 支持命名参数替换 {Key}
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        string key = entry.Key?.ToString();
                        string val = entry.Value?.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            commandToSend = commandToSend.Replace($"{{{key}}}", val);
                        }
                    }
                }
                else if (args is object[] arr)
                {
                    // 支持位置参数替换 {0}, {1}
                    if (commandToSend.Contains("{0}"))
                    {
                        commandToSend = string.Format(commandToSend, arr);
                    }
                }
            }
            catch(Exception ex)
            {
                 throw new FormatException($"Failed to format command '{cmdDef.CommandTemplate}' with args: {ex.Message}");
            }

            // 2. 发送与接收 (Determine if it's a query or write)
            bool expectResponse = cmdDef.Parser != null && cmdDef.Parser.Type != "None";
            
            // 命令间限频 (Inter-Command Wait)
            int waitMs = _config.InterCommandWaitMs;
            if (waitMs > 0)
            {
                var diff = (DateTime.Now - _lastExecutionTime).TotalMilliseconds;
                if (diff < waitMs)
                {
                    await Task.Delay((int)(waitMs - diff), token).ConfigureAwait(false);
                }
            }

            // 添加终止符 (仅 ASCII 模式)
            string term = _config.Terminator ?? "\n";
            bool isHex = string.Equals(_config.Encoding, "Hex", StringComparison.OrdinalIgnoreCase);

            byte[] dataToSend;

            if (isHex)
            {
                // Hex 模式: 将 "01 03 FF" 转换为字节数组
                // 移除所有空格和分隔符
                string hexClean = commandToSend.Replace(" ", "").Replace("-", "").Replace("0x", "");
                if (hexClean.Length % 2 != 0) throw new FormatException($"Invalid Hex string length: {commandToSend}");
                
                dataToSend = new byte[hexClean.Length / 2];
                for (int i = 0; i < hexClean.Length; i += 2)
                {
                    dataToSend[i / 2] = Convert.ToByte(hexClean.Substring(i, 2), 16);
                }
            }
            else
            {
                // ASCII 模式: 自动追加终止符
                if (!commandToSend.EndsWith(term))
                {
                    commandToSend += term;
                }
                dataToSend = System.Text.Encoding.ASCII.GetBytes(commandToSend);
            }

            await transport.SendAsync(dataToSend, token).ConfigureAwait(false);
            _lastExecutionTime = DateTime.Now;

            // 等待 (Wait if needed)
            if (cmdDef.WaitAfterMs > 0)
            {
                await Task.Delay(cmdDef.WaitAfterMs, token).ConfigureAwait(false);
            }

            string rawResponse = null;
            if (expectResponse)
            {
                // 3. 接收响应 (Receive)
                var strategy = cmdDef.ReadStrategy ?? _config.ReadStrategy ?? new ReadStrategyDefinition { Type = "Terminator", Terminator = _config.Terminator };
                
                if (strategy.Type == "FixedLength")
                {
                     int len = strategy.Length > 0 ? strategy.Length : 1024;
                     byte[] buf = new byte[len];
                     int totalRead = 0;
                     while(totalRead < len)
                     {
                         int read = await transport.ReceiveAsync(buf, totalRead, len - totalRead, token).ConfigureAwait(false);
                         if(read == 0) break;
                         totalRead += read;
                     }
                     rawResponse = System.Text.Encoding.ASCII.GetString(buf, 0, totalRead);
                }
                else if (strategy.Type == "Available")
                {
                    // 返回缓冲区中当前所有可用数据
                    byte[] chunk = null;
                    if (_receiveBuffer.Count > 0)
                    {
                        chunk = _receiveBuffer.ToArray();
                        _receiveBuffer.Clear();
                    }
                    else
                    {
                        chunk = await transport.ReceiveAsync(token).ConfigureAwait(false);
                    }
                    
                    if (chunk != null && chunk.Length > 0)
                    {
                        rawResponse = System.Text.Encoding.ASCII.GetString(chunk);
                    }
                }
                else // Terminator 基于结束符的粘包处理
                {
                    string readTerm = !string.IsNullOrEmpty(strategy.Terminator) ? strategy.Terminator : _config.Terminator;
                    if (string.IsNullOrEmpty(readTerm)) readTerm = "\n";
                    byte[] termBytes = System.Text.Encoding.ASCII.GetBytes(readTerm);

                    // 循环读取直到找到终止符
                    int foundIdx = -1;
                    int maxRetries = 20; // 增加重试次数
                    while (foundIdx == -1 && maxRetries-- > 0)
                    {
                        // 检查缓冲区是否已有终止符
                        foundIdx = FindSequence(_receiveBuffer, termBytes);

                        if (foundIdx == -1)
                        {
                            byte[] chunk = await transport.ReceiveAsync(token).ConfigureAwait(false);
                            if (chunk != null && chunk.Length > 0)
                            {
                                _receiveBuffer.AddRange(chunk);
                                // 立即检查
                                foundIdx = FindSequence(_receiveBuffer, termBytes);
                            }
                            else
                            {
                                await Task.Delay(5, token).ConfigureAwait(false); // 没读到，稍等
                            }
                        }
                    }

                    if (foundIdx >= 0)
                    {
                        var packet = _receiveBuffer.GetRange(0, foundIdx);
                        rawResponse = System.Text.Encoding.ASCII.GetString(packet.ToArray());
                        // 移除已处理的包（包括终止符）
                        _receiveBuffer.RemoveRange(0, foundIdx + termBytes.Length);
                    }
                    else
                    {
                        // 超时或未读到完整包，如果有数据也返回，或者报错
                        if (_receiveBuffer.Count > 0)
                        {
                            rawResponse = System.Text.Encoding.ASCII.GetString(_receiveBuffer.ToArray());
                            _receiveBuffer.Clear();
                        }
                    }
                }
                rawResponse = rawResponse?.Trim();
            }
            else
            {
                return null;
            }

            // 3. 解析响应 (Parse Response)
            return ParseResponse(rawResponse, cmdDef.Parser);
        }

        private object ParseResponse(string response, ResponseParserDefinition parser)
        {
            if (string.IsNullOrEmpty(response)) return GetDefault(parser.TargetType);
            
            string extractedValue = response;

            try 
            {
                switch (parser.Type)
                {
                    case "Regex":
                        var match = Regex.Match(response, parser.Pattern);
                        if (match.Success)
                        {
                            if (match.Groups.Count > parser.Index)
                                extractedValue = match.Groups[parser.Index].Value;
                            else
                                extractedValue = match.Value;
                        }
                        break;

                    case "Json":
                        var jtoken = JToken.Parse(response).SelectToken(parser.Pattern);
                        extractedValue = jtoken?.ToString();
                        break;

                    case "Split":
                        var parts = response.Split(new[] { parser.Pattern }, StringSplitOptions.None);
                        if (parts.Length > parser.Index)
                        {
                            extractedValue = parts[parser.Index];
                        }
                        break;

                    case "Trim":
                        extractedValue = response.Trim();
                        break;

                    case "None":
                        extractedValue = null;
                        break;

                    default:
                        extractedValue = response;
                        break;
                }
            }
            catch
            {
                // 解析过程异常
                return GetDefault(parser.TargetType);
            }
            
            // 4. 类型转换 (Convert)
            return ConvertToTargetType(extractedValue, parser.TargetType);
        }
        
        private object ConvertToTargetType(string val, string targetType)
        {
            if (string.IsNullOrEmpty(val)) return GetDefault(targetType);

            switch (targetType)
            {
                case "Double":
                    return double.TryParse(val, out var d) ? d : 0.0;
                case "Int":
                    return int.TryParse(val, out var i) ? i : 0;
                case "Boolean":
                    return bool.TryParse(val, out var b) ? b : false;
                case "String":
                default:
                    return val;
            }
        }

        private object GetDefault(string targetType)
        {
             switch (targetType)
            {
                case "Double": return 0.0;
                case "Int": return 0;
                case "Boolean": return false;
                default: return string.Empty;
            }
        }

        private int FindSequence(System.Collections.Generic.List<byte> buffer, byte[] sequence)
        {
            if (sequence.Length == 0) return -1;
            for (int i = 0; i < buffer.Count - sequence.Length + 1; i++)
            {
                bool match = true;
                for (int j = 0; j < sequence.Length; j++)
                {
                    if (buffer[i + j] != sequence[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
