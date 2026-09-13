using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Utils;

/// <summary>
/// 类型判断辅助：集中处理数值类型判定，避免多处重复 switch/typeof。
/// </summary>
public static class TypeHelper
{
    private static readonly HashSet<TypeCode> NumericTypeCodes = new HashSet<TypeCode>
    {
        TypeCode.Byte,
        TypeCode.SByte,
        TypeCode.UInt16,
        TypeCode.UInt32,
        TypeCode.UInt64,
        TypeCode.Int16,
        TypeCode.Int32,
        TypeCode.Int64,
        TypeCode.Decimal,
        TypeCode.Double,
        TypeCode.Single
    };

    /// <summary>
    /// 判断一个类型是否为数值类型（自动解包 Nullable）。
    /// </summary>
    public static bool IsNumeric(Type type)
    {
        if (type == null) return false;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return NumericTypeCodes.Contains(Type.GetTypeCode(underlying));
    }

    /// <summary>
    /// 判断一个对象是否为数值类型（自动解包 Nullable 与 JValue）。
    /// </summary>
    public static bool IsNumeric(object obj)
    {
        if (obj == null) return false;
        var value = JsonValueHelper.Unwrap(obj);
        return value is IConvertible && IsNumeric(value.GetType());
    }
}
