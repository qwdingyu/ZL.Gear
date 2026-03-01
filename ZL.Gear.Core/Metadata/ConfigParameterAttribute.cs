using System;

namespace ZL.Gear.Core.Metadata
{

    /// <summary>
    /// 用于标记参数，支持多次使用以定义多个参数
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class ConfigParameterAttribute : Attribute
    {
        /// <summary>
        /// JSON 中的 Key, 如 "plc.id"
        /// </summary>
        public string Key { get; }

        public string DisplayName { get; set; }
        /// <summary>
        /// "String", "Int", "Bool", "Enum"
        /// </summary>
        public string DataType { get; set; } = "String"; // 默认为字符串
        public object DefaultValue { get; set; }
        public bool IsRequired { get; set; } = true;
        /// <summary>
        /// 枚举选项
        /// </summary>
        public string[] Options { get; set; }
        /// <summary>
        /// UI 编辑器提示 默认为文本框
        /// </summary>
        public string Editor { get; set; } = "String"; 
        public string Unit { get; set; }
        /// <summary>
        /// 默认不设置
        /// </summary>
        public double Increment { get; set; } = 0;
        /// <summary>
        /// 权限控制 默认操作员可见
        /// </summary>
        public string RequiredLevel { get; set; } = "Operator"; 

        public ConfigParameterAttribute(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }
        public ConfigParameterAttribute(string key, string dataType, string displayName)
        {
            Key = key;
            DataType = dataType;
            DisplayName = displayName;
        }
    }
}
