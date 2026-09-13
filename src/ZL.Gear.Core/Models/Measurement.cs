using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Utils;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 统一测量模型 - 框架核心数据类型
    ///
    /// 设计原则：
    /// 1. 泛型支持 - 支持强类型值，避免装箱/拆箱
    /// 2. 扩展性 - 通过 Metadata 支持任意扩展数据
    /// 3. 兼容性 - 保留原有接口，向后兼容
    /// 4. 线程安全 - 所有只读属性，设计为不可变
    ///
    /// 使用方式：
    /// ```csharp
    /// // 创建泛型测量
    /// var measurement = Measurement<double>.Create(
    ///     key: "resistance.value",
    ///     value: 10.5,
    ///     unit: "Ω",
    ///     success: true
    /// );
    ///
    /// // 创建带扩展数据的测量
    /// var measurement = Measurement.Create(
    ///     key: "noise.spectrum",
    ///     value: noiseData,  // 复杂对象
    ///     metadata: new Dictionary<string, object>
    ///     {
    ///         ["Frequency"] = 1000,
    ///         ["Weighting"] = "dBA"
    ///     }
    /// );
    /// ```
    /// </summary>
    public class Measurement
    {
        #region 核心属性（不可变）

        /// <summary>
        /// 业务唯一标识符，如 "motor.current", "resistance.value", "noise.level"
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// 测量值（通用对象容器，支持任意类型）
        /// </summary>
        public object? Value { get; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// 附加消息/错误信息
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 采集的样本数量
        /// </summary>
        public int SamplesCollected { get; }

        /// <summary>
        /// 单位字符串，如 "Ω", "dBA", "V", "A"
        /// </summary>
        public string Unit { get; }

        /// <summary>
        /// 采集时间戳
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// 扩展元数据字典，用于存储设备特定信息
        /// 如：RawData、Checksum、Channel 等
        /// </summary>
        public ReadOnlyDictionary<string, object> Metadata { get; }

        /// <summary>
        /// 测量值的强类型访问器
        /// 返回可序列化的类型名称，避免 System.Type 无法被部分 JSON 序列化器处理。
        /// </summary>
        public string ValueType => Value?.GetType().FullName ?? string.Empty;

        #endregion

        #region 构造方法（保护）

        /// <summary>
        /// 构造函数
        /// </summary>
        protected Measurement(
            string key,
            object? value,
            bool success,
            string message,
            int samplesCollected,
            string unit,
            DateTime timestamp,
            Dictionary<string, object>? metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Measurement Key 不能为空", nameof(key));

            Key = key;
            Value = value;
            Success = success;
            Message = message ?? string.Empty;
            SamplesCollected = samplesCollected > 0 ? samplesCollected : 1;
            Unit = unit ?? string.Empty;
            Timestamp = timestamp;
            Metadata = new ReadOnlyDictionary<string, object>(metadata ?? new Dictionary<string, object>());
        }

        /// <summary>
        /// legacy 5 参数构造（StepHandlerKit / CommonHandler 兼容）。
        /// </summary>
        public Measurement(string key, object value, bool success, string message = "", int samplesCollected = 1)
            : this(key, value, success, message ?? string.Empty, samplesCollected > 0 ? samplesCollected : 1, string.Empty, DateTime.UtcNow, null)
        {
        }

        #endregion

        #region 工厂方法（静态）

        /// <summary>
        /// 创建测量（通用版本）
        /// </summary>
        public static Measurement Create(
            string key,
            object? value,
            bool success,
            string message = "操作成功",
            string unit = "",
            int samplesCollected = 1,
            Dictionary<string, object>? metadata = null)
        {
            return new Measurement(key, value, success, message, samplesCollected, unit, DateTime.UtcNow, metadata);
        }

        /// <summary>
        /// 创建成功的测量（工厂方法）
        /// </summary>
        public static Measurement Succeeded(
            string key,
            object? value,
            string message = "操作成功",
            string unit = "")
        {
            return new Measurement(key, value, true, message, 1, unit, DateTime.UtcNow, null);
        }

        /// <summary>
        /// 创建失败的测量
        /// </summary>
        public static Measurement Failed(
            string key,
            string message,
            object? value = null)
        {
            return new Measurement(key, value, false, message, 1, string.Empty, DateTime.UtcNow, null);
        }

        /// <summary>
        /// 从 DeviceReading 创建设备测量结果
        /// </summary>
        /// <summary>legacy 别名。</summary>
        public static Measurement CreateFromReading(
            string key,
            DeviceReading reading,
            Func<object, object> valueTransformer = null,
            string successMessage = "操作成功")
            => FromReading(key, reading, valueTransformer, successMessage);

        public static Measurement FromReading(
            string key,
            DeviceReading reading,
            Func<object, object>? valueTransformer = null,
            string successMessage = "操作成功")
        {
            if (!reading.Success)
            {
                return new Measurement(
                    key,
                    reading.Value,
                    false,
                    $"操作失败: {reading.Message}",
                    reading.SamplesCollected,
                    string.Empty,
                    DateTime.UtcNow,
                    null);
            }

            object finalValue = reading.Value;
            if (valueTransformer != null && reading.Value != null)
            {
                try
                {
                    finalValue = valueTransformer(reading.Value);
                }
                catch (Exception ex)
                {
                    return new Measurement(
                        key,
                        reading.Value,
                        false,
                        $"值转换失败: {ex.Message}",
                        reading.SamplesCollected,
                        string.Empty,
                        DateTime.UtcNow,
                        null);
                }
            }

            return new Measurement(
                key,
                finalValue,
                true,
                successMessage,
                reading.SamplesCollected,
                string.Empty,
                DateTime.UtcNow,
                null);
        }

        #endregion

        #region 值访问方法

        /// <summary>
        /// 安全获取值（带类型转换）
        /// </summary>
        public TValue? GetValue<TValue>()
        {
            if (Value == null) return default;
            return ConvertHelper.ConvertOrDefault(Value, default(TValue));
        }

        /// <summary>
        /// 获取扩展元数据
        /// </summary>
        public TValue? GetMetadata<TValue>(string key)
        {
            if (Metadata.TryGetValue(key, out var value))
            {
                if (value is TValue typed) return typed;
                return ConvertHelper.ConvertOrDefault(value, default(TValue));
            }
            return default;
        }

        /// <summary>
        /// 获取用于UI显示的格式化字符串
        /// </summary>
        public string GetDisplayValue()
        {
            if (Value == null) return "N/A";
            return Value switch
            {
                double d when d > 9999999999 => "∞",
                float f when f > 9999999999 => "∞",
                _ => Value.ToString() ?? "N/A"
            };
        }

        /// <summary>
        /// 获取显示字符串（带单位）
        /// </summary>
        public string GetDisplayValueWithUnit()
        {
            var display = GetDisplayValue();
            return string.IsNullOrEmpty(Unit) ? display : $"{display} {Unit}";
        }

        #endregion

        #region 对象操作

        public override bool Equals(object? obj)
        {
            return obj is Measurement other &&
                   Key == other.Key &&
                   Success == other.Success &&
                   Message == other.Message;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Key.GetHashCode();
                hash = hash * 23 + Success.GetHashCode();
                hash = hash * 23 + (Message?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString()
        {
            var status = Success ? "✓" : "✗";
            var unitStr = string.IsNullOrEmpty(Unit) ? "" : $" {Unit}";
            return $"[{status}] {Key}: {GetDisplayValueWithUnit()} {(string.IsNullOrEmpty(Message) ? "" : $"- {Message}")}";
        }

        #endregion
    }

    /// <summary>
    /// 泛型测量 - 提供强类型值访问，避免装箱开销
    ///
    /// 优势：
    /// 1. 避免 object 装箱/拆箱，性能更好
    /// 2. 编译时类型检查，减少运行时错误
    /// 3. 代码可读性更高
    ///
    /// 示例：
    /// ```csharp
    /// var resistance = Measurement<double>.Create(
    ///     key: "resistance.value",
    ///     value: 10.5,  // 直接 double，不用装箱
    ///     unit: "Ω"
    /// );
    ///
    /// double value = resistance.Value;  // 直接访问 double
    /// ```
    /// </summary>
    /// <typeparam name="TValue">测量值类型</typeparam>
    public class Measurement<TValue> : Measurement
    {
        #region 核心属性

        /// <summary>
        /// 强类型测量值
        /// </summary>
        public new TValue? Value { get; }

        #endregion

        #region 构造方法

        public Measurement(
            string key,
            TValue? value,
            bool success,
            string message,
            int samplesCollected,
            string unit,
            DateTime timestamp,
            Dictionary<string, object>? metadata)
            : base(
                key,
                value,
                success,
                message,
                samplesCollected,
                unit,
                timestamp,
                metadata)
        {
            Value = value;
        }

        #endregion

        #region 工厂方法

        /// <summary>
        /// 创建泛型测量
        /// </summary>
        public static Measurement<TValue> Create(
            string key,
            TValue value,
            bool success,
            string message = "操作成功",
            string unit = "",
            Dictionary<string, object>? metadata = null)
        {
            return new Measurement<TValue>(key, value, success, message, 1, unit, DateTime.UtcNow, metadata);
        }

        /// <summary>
        /// 创建成功的测量
        /// </summary>
        public static Measurement<TValue> Succeeded(
            string key,
            TValue value,
            string message = "操作成功",
            string unit = "")
        {
            return new Measurement<TValue>(key, value, true, message, 1, unit, DateTime.UtcNow, null);
        }

        /// <summary>
        /// 创建失败的测量
        /// </summary>
        public static Measurement<TValue> Failed(
            string key,
            string message,
            TValue? value = default)
        {
            return new Measurement<TValue>(key, value, false, message, 1, string.Empty, DateTime.UtcNow, null);
        }

        #endregion

        #region 值访问（重写为强类型）

        /// <summary>
        /// 强类型值访问
        /// </summary>
        public TValue? GetValue()
        {
            return Value;
        }

        #endregion

        #region 重写

        public override string ToString()
        {
            var status = Success ? "✓" : "✗";
            var unitStr = string.IsNullOrEmpty(Unit) ? "" : $" {Unit}";
            var valueStr = Value?.ToString() ?? "N/A";
            return $"[{status}] {Key}: {valueStr}{unitStr} {(string.IsNullOrEmpty(Message) ? "" : $"- {Message}")}";
        }

        #endregion
    }

    /// <summary>
    /// 测量集合工具类
    /// </summary>
    public static class MeasurementKit
    {
        /// <summary>
        /// 从字典创建设备特定数据模型（兼容旧接口）
        /// </summary>
        public static T CreateDeviceModel<T>(Dictionary<string, object> metadata) where T : new()
        {
            var result = new T();
            var type = typeof(T);

            foreach (var kvp in metadata)
            {
                var prop = type.GetProperty(kvp.Key);
                if (prop != null && prop.CanWrite)
                {
                    try
                    {
                        var value = Convert.ChangeType(kvp.Value, prop.PropertyType);
                        prop.SetValue(result, value);
                    }
                    catch
                    {
                        // 类型转换失败时静默跳过，不影响其他字段
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 从 Measurement 提取元数据到指定类型
        /// </summary>
        public static T ExtractMetadata<T>(Measurement measurement) where T : new()
        {
            var dict = new Dictionary<string, object>();
            foreach (var kvp in measurement.Metadata)
            {
                dict[kvp.Key] = kvp.Value;
            }
            return CreateDeviceModel<T>(dict);
        }

        /// <summary>
        /// 创建带元数据的测量
        /// </summary>
        public static Measurement CreateWithMetadata(
            string key,
            object value,
            string unit,
            Dictionary<string, object> metadata)
        {
            return Measurement.Create(key, value, true, "操作成功", unit, 1, metadata);
        }

        /// <summary>
        /// 批量创建测量（用于 ParallelMeasure）
        /// </summary>
        public static List<Measurement> CreateBatch(
            params (string Key, object Value, string Unit, bool Success, string Message)[] measurements)
        {
            var result = new List<Measurement>();
            foreach (var m in measurements)
            {
                result.Add(Measurement.Create(m.Key, m.Value, m.Success, m.Message, m.Unit));
            }
            return result;
        }
    }
}
