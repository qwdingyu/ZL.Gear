using System;

namespace ZL.Gear.Core.Utils
{
    /// <summary>
    /// 类型转换辅助：集中处理通用 Convert.ChangeType 场景，减少重复 try/catch 样板。
    /// </summary>
    /// <remarks>
    /// <para><strong>设计意图</strong>：在工控参数解析、字典值读取、JSON 反序列化后的对象转换等场景中，经常需要把 <c>object</c> 安全转换为目标类型。
    /// 直接写 <c>Convert.ChangeType</c> 会导致大量重复 try/catch，且语义分散。</para>
    /// <para><strong>使用原则</strong>：
    /// <list type="bullet">
    /// <item>优先使用 <see cref="ConvertOrDefault{T}(object, T)"/>：失败时返回调用方指定的默认值，不会吞异常。</item>
    /// <item>需要知道转换是否成功时，使用 <see cref="TryConvert{T}(object, out T)"/>：返回 bool，不抛异常。</item>
    /// <item>当目标类型仅运行时可知（如 <c>Type</c> 对象）时，使用非泛型 <see cref="ConvertOrDefault(object, Type)"/>。</item>
    /// </list>
    /// </remarks>
    public static class ConvertHelper
    {
        /// <summary>
        /// 安全转换：尝试将 <paramref name="value"/> 转换为目标类型 <typeparamref name="T"/>；失败返回 <paramref name="defaultValue"/>。
        /// </summary>
        /// <param name="value">待转换的原始值，可能来自字典、JSON、参数表等。</param>
        /// <param name="defaultValue">转换失败时的兜底值。调用方应显式传入业务默认值，避免隐式 <c>default(T)</c> 带来的可读性问题。</param>
        /// <returns>转换成功返回转换后的值；转换失败返回 <paramref name="defaultValue"/>。</returns>
        /// <remarks>
        /// <para>转换顺序：</para>
        /// <list type="number">
        /// <item>若 <paramref name="value"/> 为 <c>null</c>，直接返回 <paramref name="defaultValue"/>。</item>
        /// <item>若 <paramref name="value"/> 已是 <typeparamref name="T"/> 类型，直接返回，避免不必要的装箱/拆箱。</item>
        /// <item>其余情况委托 <see cref="System.Convert.ChangeType(object, Type)"/>，失败时静默返回默认值。</item>
        /// </list>
        /// <para><strong>注意</strong>：该方法不会抛出异常，适合用于参数解析、配置读取等“失败应静默降级”的场景。</para>
        /// </remarks>
        public static T ConvertOrDefault<T>(object value, T defaultValue = default)
        {
            if (value == null)
            {
                return defaultValue;
            }

            if (value is T direct)
            {
                return direct;
            }

            try
            {
                return (T)System.Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 安全转换：尝试将 <paramref name="value"/> 转换为 <paramref name="targetType"/>；失败返回 <c>null</c>。
        /// </summary>
        /// <param name="value">待转换的原始值。</param>
        /// <param name="targetType">目标类型，不能为 <c>null</c>。</param>
        /// <returns>转换成功返回转换后的装箱值；转换失败或输入无效时返回 <c>null</c>。</returns>
        /// <remarks>
        /// <para>与泛型版本的区别：当目标类型仅在运行时确定时（例如通过反射读取属性类型），使用本方法。</para>
        /// <para>若 <paramref name="targetType"/> 与 <paramref name="value"/> 实际类型相同，直接返回原值，不做转换。</para>
        /// </remarks>
        public static object ConvertOrDefault(object value, Type targetType)
        {
            if (value == null || targetType == null)
            {
                return null;
            }

            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            try
            {
                return System.Convert.ChangeType(value, targetType);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 尝试转换：尝试将 <paramref name="value"/> 转换为目标类型 <typeparamref name="T"/>；返回是否成功。
        /// </summary>
        /// <param name="value">待转换的原始值。</param>
        /// <param name="result">转换成功时存放结果；转换失败时置为 <c>default(T)</c>。</param>
        /// <returns>转换成功返回 <c>true</c>；转换失败返回 <c>false</c>。</returns>
        /// <remarks>
        /// <para>适用于需要明确区分“值为默认值”与“转换失败”的场景。</para>
        /// <para>转换顺序与 <see cref="ConvertOrDefault{T}(object, T)"/> 一致：null 检查 → 直接类型匹配 → <c>Convert.ChangeType</c>。</para>
        /// </remarks>
        public static bool TryConvert<T>(object value, out T result)
        {
            result = default;
            if (value == null)
            {
                return false;
            }

            if (value is T direct)
            {
                result = direct;
                return true;
            }

            try
            {
                result = (T)System.Convert.ChangeType(value, typeof(T));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
