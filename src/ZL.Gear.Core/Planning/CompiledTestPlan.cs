using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Planning
{
    /// <summary>
    /// 编译后的测试计划快照（对标 OpenTAP Compiled TestPlan / frozen plan）。
    /// </summary>
    /// <remarks>
    /// <para>步骤树为深拷贝，已完成 Profile 绑定、Normalize 与 Handler 元数据桥接。</para>
    /// <para><see cref="PlanHash"/> 基于<b>源 Plan DTO</b> 的 JSON 序列化，标识「配方定义」而非编译后物理 Target；
    /// 便于同一 Recipe 在不同 Profile 下追溯时哈希一致。</para>
    /// </remarks>
    public sealed class CompiledTestPlan
    {
        public CompiledTestPlan(
            IReadOnlyList<StepConfig> steps,
            string planHash,
            DateTimeOffset compiledAtUtc)
        {
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            PlanHash = planHash ?? string.Empty;
            CompiledAtUtc = compiledAtUtc;
        }

        /// <summary>编译后的步骤树（深拷贝，已 Profile 绑定与 Normalize）。</summary>
        public IReadOnlyList<StepConfig> Steps { get; }

        /// <summary>源 Plan 定义哈希（SHA256 hex），写入 <see cref="Runner.TestRunResult.PlanHash"/>。</summary>
        public string PlanHash { get; }

        /// <summary>编译完成时刻（UTC），供审计与缓存失效策略使用。</summary>
        public DateTimeOffset CompiledAtUtc { get; }
    }
}
