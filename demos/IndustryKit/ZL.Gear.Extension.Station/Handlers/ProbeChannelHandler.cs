using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Extension.Station;

namespace ZL.Gear.Extension.Station.Handlers
{
    /// <summary>
    /// 模拟通道探测：将测量值写入 <c>MeasuredOhm</c>（无硬件，可替换为真实仪表 Handler）。
    /// </summary>
    /// <remarks>
    /// 取值：Args.MeasuredOhm 覆盖 → 否则 Variables.SimulatedOhm（ApplyRecipe SetShared）。
    /// 产线接表：保留 SetShared("MeasuredOhm")，中间改为设备 Read/Query 即可。
    /// </remarks>
    [StepHandlerCommand(
        StationCommands.ProbeChannel,
        Description = "通道探测（仿真或覆盖）",
        ParameterSchema = "MeasuredOhm:double 可选覆盖; 缺省读 Variables.SimulatedOhm")]
    public sealed class ProbeChannelHandler : IStepHandler
    {
        /// <inheritdoc />
        public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            var args = StepArgsReader.From(step, context, "ProbeChannel");

            // 1) Args 显式含 MeasuredOhm → 必须解析成功（禁止无效值静默回退 SimulatedOhm）
            // 2) 未提供 MeasuredOhm → 读前序 ApplyRecipe SetShared 的 SimulatedOhm（VariablesOnly，须为正）
            double measured;
            string err;

            if (HasArgKey(step, "MeasuredOhm"))
            {
                if (!args.TryRequirePositiveDouble("MeasuredOhm", StepArgSource.ArgsOnly, out measured, out err))
                {
                    return Task.FromResult(StepArgsReader.Fail(err));
                }
            }
            else if (!args.TryRequirePositiveDouble("SimulatedOhm", StepArgSource.VariablesOnly, out measured, out err))
            {
                return Task.FromResult(StepArgsReader.Fail(err));
            }

            args.SetShared("MeasuredOhm", measured);
            // ChannelId 由 ApplyRecipe 写入流程级；读前序状态用 GetFlowString，勿用 All（防 Global 同名污染）
            var channel = args.GetFlowString("ChannelId");
            context.Log($"[Industry.Station] ProbeChannel Channel={channel}, MeasuredOhm={measured}");
            return Task.FromResult<ExecutionResultBase>(
                ExecutionResult.Succeeded($"探测完成: {measured}"));
        }

        /// <summary>本步 Args 是否显式提供键（区分「未提供」与「提供了无效值」）。</summary>
        private static bool HasArgKey(StepConfig step, string key) =>
            step.Parameters != null && step.Parameters.ContainsKey(key);
    }
}
