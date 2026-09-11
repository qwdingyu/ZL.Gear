using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Extension.Station;

namespace ZL.Gear.Extension.Station.Handlers
{
    /// <summary>
    /// 应用工位配方：把限值/模拟值写入流程共享变量，供后继 Calculate/Assert 读取。
    /// </summary>
    /// <remarks>
    /// 参数契约（与 <see cref="StepHandlerCommandAttribute.ParameterSchema"/> 字符串同义，运行时由 <see cref="StepArgsReader"/> 执行）：
    /// RecipeId:string; LimitOhm:double+; SimulatedOhm:double; ChannelId:string=CH1
    /// </remarks>
    [StepHandlerCommand(
        StationCommands.ApplyRecipe,
        Description = "应用工位配方限值与仿真注入",
        ParameterSchema = "RecipeId:string 配方ID; LimitOhm:double+ 判据上限; SimulatedOhm:double 仿真测量; ChannelId:string(CH1) 通道")]
    public sealed class ApplyRecipeHandler : IStepHandler
    {
        /// <inheritdoc />
        public Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            var args = StepArgsReader.From(step, context, "ApplyRecipe");

            if (!args.TryRequireString("RecipeId", StepArgSource.ArgsOnly, out var recipeId, out var err))
            {
                return Task.FromResult(StepArgsReader.Fail(err));
            }

            if (!args.TryRequirePositiveDouble("LimitOhm", StepArgSource.ArgsOnly, out var limitOhm, out err))
            {
                return Task.FromResult(StepArgsReader.Fail(err));
            }

            // 无真实仪表时须在 Args 注入；接表后本键可改为可选或删除
            if (!args.TryGetDouble("SimulatedOhm", StepArgSource.ArgsOnly, out var simulatedOhm, out err, true))
            {
                return Task.FromResult(StepArgsReader.Fail(err));
            }

            var channelId = args.GetOptionalString("ChannelId", "CH1", StepArgSource.ArgsOnly);

            args.SetShared("RecipeId", recipeId);
            args.SetShared("LimitOhm", limitOhm);
            args.SetShared("SimulatedOhm", simulatedOhm);
            args.SetShared("ChannelId", channelId);
            args.SetShared("StationDone", false);

            context.Log($"[Industry.Station] ApplyRecipe RecipeId={recipeId}, Channel={channelId}, LimitOhm={limitOhm}, SimulatedOhm={simulatedOhm}");
            return Task.FromResult<ExecutionResultBase>(
                ExecutionResult.Succeeded($"配方已应用: {recipeId}"));
        }
    }
}
