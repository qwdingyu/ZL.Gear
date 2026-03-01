using System.Collections.Generic;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 单个工作流节点 (递归结构)
    /// </summary>
    public class WorkflowNode
    {
        public string Id { get; set; } // UI使用的唯一ID
        public WorkflowNodeType Type { get; set; } = WorkflowNodeType.Action;

        /// <summary>
        /// 动作在 Registry 中的 Key (例如 "SetupPlcRelay")
        /// 对于 Parallel/Group 等容器节点，此字段可能为空
        /// </summary>
        public string ActionKey { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// 执行条件 (表达式，如 "Vars['Volt'] > 10")
        /// </summary>
        public string Condition { get; set; }

        /// <summary>
        /// 结果存储键 (执行成功后，将结果存入此变量名)
        /// </summary>
        public string OutputKey { get; set; }

        /// <summary>
        /// 目标设备重定向 (覆盖 StepConfig 中的默认 Target)
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// 输入变量映射 (逻辑名 -> 表达式/变量名)
        /// 例如: { "TargetVolt": "Vars['BaseVolt'] + 2" }
        /// </summary>
        public Dictionary<string, object> Inputs { get; set; } = new();

        /// <summary>
        /// 输出变量映射 (结果Key -> 目标变量名)
        /// 例如: { "Value": "MeasuredRes" }
        /// </summary>
        public Dictionary<string, string> Outputs { get; set; } = new();

        /// <summary>
        /// 节点的参数配置 (静态配置)
        /// </summary>
        public Dictionary<string, object> Args { get; set; } = new();

        // --- 控制流专用参数 ---
        public int? DelayMs { get; set; }
        public int? RetryCount { get; set; }
        public int? IntervalMs { get; set; }
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// 子节点列表 (用于 Parallel, Sequence, Group)
        /// </summary>
        public List<WorkflowNode> Children { get; set; } = new();

        /// <summary>
        /// 无论成功失败都会执行的清理动作
        /// </summary>
        public List<WorkflowNode> Finally { get; set; } = new();
    }
}
