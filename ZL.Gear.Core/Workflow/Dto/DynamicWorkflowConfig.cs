using System.Collections.Generic;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 微工作流的定义数据（UI拖拽生成的产物）
    /// </summary>
    public class DynamicWorkflowConfig
    {
        public string Version { get; set; } = "1.0";
        /// <summary>
        /// 清理动作列表 (对应 .Finally)
        /// </summary>
        public List<WorkflowNode> Finalizers { get; set; } = new();

        /// <summary>
        /// 初始变量池 (用于流程启动时注入常量)
        /// </summary>
        public Dictionary<string, object> Variables { get; set; } = new();

        /// <summary>
        /// 顺序执行的动作列表 (对应 .Then / .ThenMeasure / .Delay)
        /// </summary>
        public List<WorkflowNode> Sequence { get; set; } = new();
    }

}
