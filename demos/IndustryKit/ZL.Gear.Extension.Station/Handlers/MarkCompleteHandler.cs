using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Extension.Station;

namespace ZL.Gear.Extension.Station.Handlers
{
    /// <summary>
    /// 工位收尾：置 <c>StationDone=true</c>，供 WaitUntil / MES 上报 / Finally 插值使用。
    /// </summary>
    /// <remarks>
    /// 本 Handler 不重复做合格判定——MicroWorkflow 在前序 Assert 失败时会短路，不会执行到本节点。
    /// 读前序状态用 <see cref="StepArgsReader.GetFlowString"/>，勿混读本步 Args。
    /// </remarks>
    [StepHandlerCommand(
        StationCommands.MarkComplete,
        Description = "工位收尾标记",
        ParameterSchema = "（无必填 Args；读流程变量 RecipeId）")]
    public sealed class MarkCompleteHandler : IStepHandler
    {
        /// <inheritdoc />
        public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            var args = StepArgsReader.From(step, context, "MarkComplete");
            var recipeId = args.GetFlowString("RecipeId");

            args.SetShared("StationDone", true);
            context.Log($"[Industry.Station] MarkComplete RecipeId={recipeId} → StationDone=true");
            return Task.FromResult<ExecutionResultBase>(
                ExecutionResult.Succeeded("工位完成"));
        }
    }
}
