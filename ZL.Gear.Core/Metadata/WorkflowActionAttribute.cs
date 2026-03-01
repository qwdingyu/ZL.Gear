using System;

namespace ZL.Gear.Core.Metadata
{
    /// <summary>
    /// 用于标记 IStepHandler 类 或 IWorkflowActionProvider 的方法
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public class WorkflowActionAttribute : Attribute
    {
        public string Name { get; }       // Command Name
        public string Description { get; set; } // 对应 JSON description
        public string Group { get; set; }

        /// <summary>
        /// 对应 exposedProperties，用逗号分隔，如 "Enable,TimeoutMs"
        /// </summary>
        public string ExposedProps { get; set; }

        public WorkflowActionAttribute(string name)
        {
            Name = name;
        }
    }
}
