namespace ZL.Gear.Core.Metadata
{

    /// <summary>
    /// 对应编辑器定义 JSON 中的 parameters 数组项
    /// </summary>
    public class ActionParameterDto
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }

        /// <summary>
        /// 编辑器类型: Number, String, Bool, Enum, TextArea
        /// </summary>
        public string Editor { get; set; }

        /// <summary>
        /// 数据类型: String, Int, Double, Bool
        /// </summary>
        public string DataType { get; set; }

        public object DefaultValue { get; set; }

        /// <summary>
        /// 单位 (如 "A", "ms")
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 数字调整的步长
        /// </summary>
        public double? Increment { get; set; }

        /// <summary>
        /// 权限等级: Operator, Engineer, Admin
        /// </summary>
        public string RequiredLevel { get; set; }

        /// <summary>
        /// 如果是枚举，或者是下拉框，具体的选项列表
        /// </summary>
        public string[] Options { get; set; }

        public bool IsRequired { get; set; } = true;
    }
}
