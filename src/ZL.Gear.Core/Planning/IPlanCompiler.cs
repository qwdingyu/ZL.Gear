using System.Collections.Generic;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Planning
{
    /// <summary>
    /// 将可变 Plan DTO 编译为运行时可执行快照（对标 OpenTAP：TestPlanDefinition → Compiled TestPlan）。
    /// </summary>
    /// <remarks>
    /// 编译成功产出 <see cref="CompiledTestPlan"/>，运行期只读快照，不修改调用方传入的 <see cref="StepConfig"/> 树。
    /// 编译失败必须 fail-closed：Executor 不得进入设备租约或步骤执行阶段。
    /// </remarks>
    public interface IPlanCompiler
    {
        /// <summary>
        /// 编译测试计划。
        /// </summary>
        /// <param name="sourceSteps">调用方 Plan（可变 DTO，编译器不得就地修改）。</param>
        /// <param name="context">Profile、设备角色、Handler 目录等编译期依赖。</param>
        /// <returns><see cref="PlanCompileResult.Success"/> 为 false 时 <see cref="PlanCompileResult.Plan"/> 为 null。</returns>
        PlanCompileResult Compile(IReadOnlyList<StepConfig> sourceSteps, PlanCompileContext context);
    }
}
