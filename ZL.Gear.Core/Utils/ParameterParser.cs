
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZL.Gear.Core.Utils
{

    // 文件路径: Services/ParameterParser.cs

    /// <summary>
    /// 一个静态辅助类，提供将object（可能来自JSON）解析为强类型数组或值的方法。
    /// 核心功能是兼容处理单个值和数组值。
    /// </summary>
    public static class ParameterParser
    {
        /// <summary>
        /// 通用方法，将一个object对象（通常来自Dictionary<string, object>）转换为一个T类型的数组。
        /// 这个方法是整个通用方案的关键，它抹平了JSON中 "LCL": 1.5 和 "LCL": [1.5, 10] 的差异。
        /// </summary>
        /// <typeparam name="T">期望转换的目标类型，如 double, string, int。</typeparam>
        /// <param name="value">输入的object对象。</param>
        /// <returns>转换后的T类型数组。如果输入为null或无法转换，返回空数组。</returns>
        public static T[] ParseToArray<T>(object value)
        {
            if (value == null)
            {
                return Array.Empty<T>();
            }

            // Case 1: 值本身就是一个JArray (来自Newtonsoft.Json)
            if (value is JArray jArray)
            {
                try
                {
                    return jArray.ToObject<T[]>();
                }
                catch (Exception ex)
                {
                    // LogKit.Error($"Failed to convert JArray to {typeof(T).Name}[]: {ex.Message}");
                    return Array.Empty<T>();
                }
            }

            // Case 2: 值是一个.NET数组或List
            if (value is IEnumerable<T> enumerable)
            {
                return enumerable.ToArray();
            }

            // Case 3: 值是一个单一元素
            try
            {
                // 尝试将单个值转换为目标类型，然后放入一个单元素的数组中
                var convertedValue = (T)Convert.ChangeType(value, typeof(T));
                return new T[] { convertedValue };
            }
            catch (Exception ex)
            {
                // LogKit.Error($"Failed to convert single value '{value}' to {typeof(T).Name}: {ex.Message}");
                return Array.Empty<T>();
            }
        }
    }

}
