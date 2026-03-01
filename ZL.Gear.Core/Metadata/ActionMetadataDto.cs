using System.Collections.Generic;

namespace ZL.Gear.Core.Metadata
{
    /// <summary>
    /// 对应编辑器定义的 JSON 根节点
    /// </summary>
    public class ActionMetadataDto
    {
        /// <summary>
        /// 命令 Key (对应 Command 或 StepKey)
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// 显示名称 / 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 分组 (如 PLC控制, 电流测试)
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// [新增] 标识此元数据的用途
        /// StepHandler: 顶层步骤，可以直接添加到测试流程
        /// AtomicAction: 原子动作，只能在 DynamicFlow 内部使用
        /// </summary>
        public string Usage { get; set; }

        /// <summary>
        /// 只有 StepHandler 级别才有，指定哪些通用属性需要显示 (如 "Enable", "TimeoutMs")
        /// </summary>
        public List<string> ExposedProperties { get; set; } = new List<string>();

        /// <summary>
        /// 参数列表
        /// </summary>
        public List<ActionParameterDto> Parameters { get; set; } = new List<ActionParameterDto>();
    }

}
