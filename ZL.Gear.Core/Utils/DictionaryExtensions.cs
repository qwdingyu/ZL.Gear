using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZL.Gear.Core.Utils
{
    public static class DictionaryExtensions
    {
        /// <summary>
        /// 尝试将指定的键和值添加到字典中。
        /// （为旧版 .NET Framework 提供 .NET Core 风格的 TryAdd 方法）
        /// </summary>
        /// <typeparam name="TKey">字典中键的类型。</typeparam>
        /// <typeparam name="TValue">字典中值的类型。</typeparam>
        /// <param name="dictionary">要向其中添加元素的目标字典。</param>
        /// <param name="key">要添加的元素的键。</param>
        /// <param name="value">要添加的元素的值。</param>
        /// <returns>
        /// 如果成功添加了键/值对，则返回 true；
        /// 如果字典中已存在具有相同键的元素，则返回 false。
        /// </returns>
        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value, bool replace = false)
        {
            // 检查键是否已经存在
            if (!dictionary.ContainsKey(key))
            {
                // 如果不存在，则添加并返回 true
                dictionary.Add(key, value);
                return true;
            }
            if (replace)
            {
                dictionary[key] = value;
                return true;
            }
            return false;
        }
        public static bool TryGet<T>(this IReadOnlyDictionary<string, object> dictionary, string key, out T value)
        {
            if (dictionary.TryGetValue(key, out object objValue))
            {
                if (objValue is T directValue)
                {
                    value = directValue;
                    return true;
                }
                try
                {
                    value = (T)Convert.ChangeType(objValue, typeof(T));
                    return true;
                }
                catch (Exception)
                {
                    // 转换失败
                }
            }
            value = default;
            return false;
        }
        /// <summary>
        /// 从字典中安全地获取一个指定类型的值。
        /// </summary>
        /// <typeparam name="T">期望获取的值的类型。</typeparam>
        /// <param name="dictionary">要操作的字典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="defaultValue">如果键不存在，或者值的类型不正确时，返回的默认值。</param>
        /// <returns>返回字典中的值（如果存在且类型正确），否则返回指定的默认值。</returns>
        public static T Get<T>(this IDictionary<string, object> dictionary, string key, T defaultValue = default)
        {
            // 1. 检查字典和键是否有效
            if (dictionary == null || key == null) return defaultValue;
            // 2. 尝试获取值
            if (dictionary.TryGetValue(key, out object value))
            {
                // 3. 检查值是否存在且类型是否匹配
                // 如果值已经是期望的类型，直接返回
                if (value is T typedValue)
                {
                    return typedValue;
                }
                // --- 专门处理十六进制字符串 ---
                if (value is string s && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // 移除 "0x" 前缀并按16进制解析
                        string hexValue = s.Substring(2);
                        // 使用 Convert.ToUInt16/32/64 等方法，它们可以指定进制
                        object convertedHex = Convert.ChangeType(Convert.ToUInt64(hexValue, 16), typeof(T));
                        return (T)convertedHex;
                    }
                    catch (Exception)
                    {
                        return defaultValue; // 如果 "0x" 后面的部分不是有效的十六进制，也返回默认值
                    }
                }
                // 4. 尝试进行类型转换 (例如，值是 int，但期望的是 long，或者值是字符串 "123"，期望的是 int)
                try
                {
                    // 使用 Convert.ChangeType 进行更广泛的转换
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (Exception)
                {
                    // 如果转换失败 (例如，尝试将 "hello" 转换为 int)，则返回默认值
                    return defaultValue;
                }
            }

            // 5. 如果键不存在，返回默认值
            return defaultValue;
        }

        //// 使用扩展方法
        //double value = dict.GetValue("DoubleValue", 0.0);
        public static T GetValue<T>(this Dictionary<string, object> dict, string key, T defVal = default)
        {
            if (dict == null || !dict.ContainsKey(key))
                return defVal;

            try
            {
                object value = dict[key];

                if (value == null || Convert.IsDBNull(value))
                    return defVal;

                if (value is T typedValue)
                    return typedValue;

                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return defVal;
            }
        }

        /// <summary>
        /// 获取与指定键关联的值。如果未找到键，则返回默认值。
        /// 这是为 .NET Framework 项目提供的兼容性扩展方法。
        /// </summary>
        /// <typeparam name="TKey">字典中键的类型。</typeparam>
        /// <typeparam name="TValue">字典中值的类型。</typeparam>
        /// <param name="dictionary">当前字典实例。</param>
        /// <param name="key">要查找其值的键。</param>
        /// <param name="defaultValue">如果未找到键时要返回的值，默认为 TValue 的默认值（例如，对于引用类型是 null，对于 int 是 0）。</param>
        /// <returns>如果字典包含具有指定键的元素，则为该元素的值；否则为 defaultValue。</returns>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue = default)
        {
            TValue value;
            return dictionary.TryGetValue(key, out value) ? value : defaultValue;
        }


        /// <summary>
        /// 将默认字典中的键值对应用到主字典中。
        /// 只有当主字典中不存在某个键时，才会从默认字典中添加该键值对。
        /// </summary>
        /// <typeparam name="TKey">字典键的类型。</typeparam>
        /// <typeparam name="TValue">字典值的类型。</typeparam>
        /// <param name="primary">主字典，将接收默认值。不能为 null。</param>
        /// <param name="defaults">包含默认值的字典。</param>
        /// <returns>返回主字典，以便进行链式调用。</returns>
        /// <exception cref="ArgumentNullException">当主字典 (primary) 为 null 时抛出。</exception>
        public static IDictionary<TKey, TValue> ApplyDefaults<TKey, TValue>(
            this IDictionary<TKey, TValue> primary,
            IDictionary<TKey, TValue> defaults)
        {
            if (primary == null)
            {
                throw new ArgumentNullException(nameof(primary), "主字典不能为 null。");
            }
            if (defaults == null || defaults.Count == 0)
            {
                return primary; // 没有默认值可应用
            }
            foreach (var item in defaults)
            {
                // 如果主字典中不包含这个键，就添加它
                if (!primary.ContainsKey(item.Key))
                {
                    primary[item.Key] = item.Value;
                }
            }
            return primary;
        }
        /// <summary>
        /// 将一个或多个源字典合并到目标字典中。
        /// 如果存在相同的键，源字典中的值将覆盖目标字典中的值。
        /// “后来者居上”原则。
        /// </summary>
        /// <typeparam name="TKey">字典键的类型。</typeparam>
        /// <typeparam name="TValue">字典值的类型。</typeparam>
        /// <param name="target">目标字典，将接收合并后的值。不能为 null。</param>
        /// <param name="sources">一个或多个源字典。</param>
        /// <returns>返回目标字典，以便进行链式调用。</returns>
        /// <exception cref="ArgumentNullException">当目标字典 (target) 为 null 时抛出。</exception>
        public static IDictionary<TKey, TValue> Merge<TKey, TValue>(this IDictionary<TKey, TValue> target, params IDictionary<TKey, TValue>[] sources)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target), "目标字典不能为 null。");
            }
            if (sources == null || sources.Length == 0)
            {
                return target;
            }
            foreach (var source in sources)
            {
                if (source == null) continue;

                foreach (var item in source)
                {
                    // 直接覆盖或添加
                    target[item.Key] = item.Value;
                }
            }

            return target;
        }

        /// <summary>
        /// 将多个字典合并到一个全新的字典中。
        /// 如果键冲突，后一个字典中的值会覆盖前一个字典中的值。
        /// </summary>
        /// <typeparam name="TKey">字典键的类型。</typeparam>
        /// <typeparam name="TValue">字典值的类型。</typeparam>
        /// <param name="dictionaries">要合并的字典列表。</param>
        /// <returns>一个包含所有合并结果的新字典。</returns>
        public static Dictionary<TKey, TValue> MergeToNew<TKey, TValue>(params IDictionary<TKey, TValue>[] dictionaries)
        {
            var result = new Dictionary<TKey, TValue>();
            if (dictionaries == null || dictionaries.Length == 0)
            {
                return result;
            }
            foreach (var dict in dictionaries)
            {
                if (dict == null) continue;
                foreach (var item in dict)
                {
                    result[item.Key] = item.Value;
                }
            }
            return result;
        }
        /// <summary>
        /// 尝试从字典中获取一个值，并将其智能转换为目标类型 T。
        /// 此方法非常强大，可以处理：
        /// 1. 直接类型匹配。
        /// 2. 从十六进制字符串 (如 "0x7A") 转换为数字类型。
        /// 3. 标准类型转换 (如 long -> int, 或 "123" -> int)。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="dictionary">要操作的只读字典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="value">如果成功，则为转换后的值。</param>
        /// <returns>如果键存在且值可以成功转换为目标类型，则为 true；否则为 false。</returns>
        public static bool TryGetAs<T>(this IReadOnlyDictionary<string, object> dictionary, string key, out T value)
        {
            value = default;
            if (!dictionary.TryGetValue(key, out object objValue) || objValue == null)
            {
                return false;
            }
            // 路径1: 类型直接匹配，最高效
            if (objValue is T directValue)
            {
                value = directValue;
                return true;
            }
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            // 路径2: 尝试从字符串进行智能转换（包括十六进制）
            if (objValue is string strValue)
            {
                // 如果目标是数字类型，则优先尝试解析十六进制
                if (IsNumericType(targetType))
                {
                    if (strValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string hex = strValue.Substring(2);
                            // 先解析为 ulong，因为它能容纳所有无符号整数，避免溢出
                            ulong parsedValue = ulong.Parse(hex, NumberStyles.HexNumber);
                            // 然后安全地转换为最终的目标类型
                            value = (T)Convert.ChangeType(parsedValue, targetType);
                            return true;
                        }
                        catch (Exception)
                        {
                            // 十六进制解析失败，将交由下面的通用转换处理
                        }
                    }
                }
            }

            // 路径3: 最后的通用转换尝试 (例如 int -> uint, "123" -> int)
            try
            {
                value = (T)Convert.ChangeType(objValue, targetType);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        /// <summary>
        /// 辅助方法：判断一个类型是否为数字类型。
        /// </summary>
        private static bool IsNumericType(Type type)
        {
            if (type == null) return false;
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }
        /// <summary>
        /// 从字典中获取一个值，并尝试将其解析为 uint。
        /// 支持十进制字符串 ("1888") 和十六进制字符串 ("0x760", "760")。
        /// </summary>
        /// <param name="dict">参数字典</param>
        /// <param name="key">要查找的键</param>
        /// <param name="defaultValue">如果键不存在或解析失败时返回的默认值</param>
        /// <returns>解析后的 uint 值或默认值</returns>
        public static uint GetHexOrDecUint(this IDictionary<string, object> dict, string key, uint defaultValue = 0)
        {
            if (!dict.TryGetValue(key, out var valueObj) || valueObj == null)
            {
                return defaultValue;
            }
            string valueStr = valueObj.ToString().Trim();
            if (string.IsNullOrEmpty(valueStr))
            {
                return defaultValue;
            }
            try
            {
                // 检查是否以 "0x" 或 "0X" 开头
                if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    // 解析十六进制，从第三个字符开始（跳过 "0x"）
                    return uint.Parse(valueStr.Substring(2), NumberStyles.HexNumber);
                }
                else
                {
                    // 尝试直接解析十六进制（例如 "7A0"）或十进制
                    // NumberStyles.Any 允许它自动检测
                    return uint.Parse(valueStr, NumberStyles.Any);
                }
            }
            catch (FormatException)
            {
                // 如果格式错误，返回默认值
                return defaultValue;
            }
            catch (OverflowException)
            {
                // 如果数值超出 uint 范围，返回默认值
                return defaultValue;
            }
        }
    }
}
