using System.Collections.Generic;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Planning
{
    /// <summary>
    /// 计划编译上下文：Profile、设备角色与 Handler 目录（对标 OpenTAP TestPlan 编译期资源解析）。
    /// </summary>
    /// <remarks>
    /// <para>编译器本身无状态；所有环境差异通过本对象注入，便于单测与多工位复用同一 <see cref="IPlanCompiler"/>。</para>
    /// <para>与 OpenTAP 的差距（仍待办）：表达式/条件预检、Resource Open 清单、插件清单哈希。</para>
    /// </remarks>
    public sealed class PlanCompileContext
    {
        /// <summary>
        /// 逻辑设备角色 → 物理设备标识（Site 级映射，来自 <see cref="Services.IGearProfileService"/>）。
        /// </summary>
        public IDictionary<string, object> DeviceRoleMap { get; set; }

        /// <summary>
        /// Profile 字符串表，供 <see cref="Models.StepConfig.BindProfile"/> 解析 Target 与 StepKey 参数注入。
        /// </summary>
        public IDictionary<string, string> ProfileMap { get; set; }

        /// <summary>
        /// Handler 注册表，用于回填 <see cref="Models.StepConfig.EvaluateResult"/> 与命令存在性检查。
        /// </summary>
        public IStepHandlerRegistry HandlerRegistry { get; set; }

        /// <summary>
        /// Handler 查找策略（注册表 / 模板 / 回退）。缺省时可仅用 <see cref="HandlerRegistry"/>。
        /// </summary>
        public IStepHandlerLookup HandlerLookup { get; set; }

        /// <summary>编译选项；未设置时使用 <see cref="PlanCompileOptions"/> 默认（Strict=true）。</summary>
        public PlanCompileOptions Options { get; set; }

        /// <summary>条件表达式语法预检；未设置时跳过预检。</summary>
        public IWorkflowEvaluator WorkflowEvaluator { get; set; }
    }
}
