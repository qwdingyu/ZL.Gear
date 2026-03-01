using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Utils
{
    /// <summary>
    /// 安全比较帮助类，提供各种数据类型的健壮比较方法
    /// </summary>
    public static class SafeCompareHelper
    {
        /// <summary>
        /// 安全的值比较方法，处理各种边界情况（推荐使用）
        /// </summary>
        public static bool SafeEquals<T>(T expected, T actual)
        {
            // 处理引用相等和null值
            if (ReferenceEquals(expected, actual)) return true;
            if (expected == null || actual == null) return false;

            var type = typeof(T);

            // 字符串类型比较
            if (type == typeof(string))
            {
                return string.Equals(expected as string, actual as string, StringComparison.Ordinal);
            }

            // 数值类型比较
            if (IsNumericType(type))
            {
                return NumericEquals(expected, actual, type);
            }

            // 默认比较
            return EqualityComparer<T>.Default.Equals(expected, actual);
        }

        /// <summary>
        /// 安全的值比较方法（简化版本）
        /// </summary>
        public static bool SafeEqualsSimple<T>(T expected, T actual)
        {
            // 处理null值
            if (ReferenceEquals(expected, actual)) return true;
            if (expected == null || actual == null) return false;

            var type = typeof(T);

            // 类型特定的比较
            if (type == typeof(string))
            {
                return string.Equals(expected as string, actual as string, StringComparison.Ordinal);
            }
            else if (type == typeof(double))
            {
                return Math.Abs((double)(object)expected - (double)(object)actual) < 0.0001;
            }
            else if (type == typeof(float))
            {
                return Math.Abs((float)(object)expected - (float)(object)actual) < 0.0001;
            }
            else if (type == typeof(decimal))
            {
                return Math.Abs((decimal)(object)expected - (decimal)(object)actual) < 0.0001m;
            }
            else
            {
                return EqualityComparer<T>.Default.Equals(expected, actual);
            }
        }

        /// <summary>
        /// 检查是否为数值类型
        /// </summary>
        private static bool IsNumericType(Type type)
        {
            if (type == null) return false;

            type = Nullable.GetUnderlyingType(type) ?? type; // 处理可空类型

            return type == typeof(int) || type == typeof(long) || type == typeof(float) ||
                   type == typeof(double) || type == typeof(decimal) || type == typeof(short) ||
                   type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) ||
                   type == typeof(uint) || type == typeof(ulong);
        }

        /// <summary>
        /// 数值类型的比较
        /// </summary>
        private static bool NumericEquals<T>(T expected, T actual, Type type)
        {
            try
            {
                // 使用Convert而不是dynamic，避免运行时异常
                double d1 = Convert.ToDouble(expected);
                double d2 = Convert.ToDouble(actual);

                // 对于整数类型，使用精确比较
                if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
                    type == typeof(byte) || type == typeof(sbyte) || type == typeof(ushort) ||
                    type == typeof(uint) || type == typeof(ulong))
                {
                    return d1 == d2;
                }

                // 对于浮点类型，使用容忍度比较
                return Math.Abs(d1 - d2) <= 0.0001;
            }
            catch
            {
                // 如果转换失败，回退到默认比较
                return EqualityComparer<T>.Default.Equals(expected, actual);
            }
        }

        /// <summary>
        /// 带容忍度的数值比较
        /// </summary>
        public static bool NumericEqualsWithTolerance<T>(T expected, T actual, double tolerance) where T : struct
        {
            try
            {
                double d1 = Convert.ToDouble(expected);
                double d2 = Convert.ToDouble(actual);
                return Math.Abs(d1 - d2) <= tolerance;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 字符串比较（忽略大小写）
        /// </summary>
        public static bool StringEqualsIgnoreCase(string str1, string str2)
        {
            return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 字符串比较（考虑空白字符）
        /// </summary>
        public static bool StringEqualsTrim(string str1, string str2, StringComparison comparison = StringComparison.Ordinal)
        {
            if (str1 == null && str2 == null) return true;
            if (str1 == null || str2 == null) return false;

            return string.Equals(str1.Trim(), str2.Trim(), comparison);
        }

        /// <summary>
        /// 深度比较两个对象的所有属性
        /// </summary>
        public static bool DeepEquals<T>(T obj1, T obj2)
        {
            if (ReferenceEquals(obj1, obj2)) return true;
            if (obj1 == null || obj2 == null) return false;

            var type = typeof(T);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (!property.CanRead) continue;

                var value1 = property.GetValue(obj1);
                var value2 = property.GetValue(obj2);

                // 使用非泛型版本进行比较
                if (!DeepEqualsInternal(value1, value2))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 深度比较内部实现
        /// </summary>
        private static bool DeepEqualsInternal(object obj1, object obj2)
        {
            if (ReferenceEquals(obj1, obj2)) return true;
            if (obj1 == null || obj2 == null) return false;

            var type1 = obj1.GetType();
            var type2 = obj2.GetType();

            if (type1 != type2) return false;

            // 使用反射调用适当的SafeEquals方法
            var method = typeof(SafeCompareHelper).GetMethod(nameof(SafeEquals), BindingFlags.Public | BindingFlags.Static);
            var genericMethod = method.MakeGenericMethod(type1);

            try
            {
                return (bool)genericMethod.Invoke(null, new[] { obj1, obj2 });
            }
            catch
            {
                return object.Equals(obj1, obj2);
            }
        }
    }
}
