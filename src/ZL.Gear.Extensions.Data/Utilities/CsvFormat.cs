using System.Collections.Generic;
using System.Text;

namespace ZL.Gear.Extensions.Data.Utilities
{
    /// <summary>
    /// RFC 4180 风格 CSV 字段转义与解析（单写者场景，不依赖第三方库）。
    /// </summary>
    public static class CsvFormat
    {
        public static string EscapeField(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var needsQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string JoinFields(IEnumerable<string> fields)
        {
            return string.Join(",", fields);
        }

        /// <summary>
        /// 解析单行 CSV（支持双引号包裹字段）。
        /// </summary>
        public static string[] ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return new string[0];
            }

            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
