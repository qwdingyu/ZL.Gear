using Newtonsoft.Json.Linq;

namespace ZL.Gear.Core.Utils;

/// <summary>
/// JSON 值兼容辅助：去掉 Newtonsoft.Json 反序列化残留的 JValue，避免表达式引擎对 JValue 做算术时报 Invalid Operation。
/// </summary>
public static class JsonValueHelper
{
    /// <summary>
    /// 如果值是 JValue，返回其底层值；否则原样返回。
    /// </summary>
    public static object Unwrap(object value)
    {
        return value is JValue jv ? jv.Value : value;
    }
}
