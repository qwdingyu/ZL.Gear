using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Extension.Station.Handlers;

namespace ZL.Gear.Extension.Station
{
    /// <summary>
    /// 通用「工位」行业扩展入口（模板）。
    /// </summary>
    /// <remarks>
    /// 设计意图（相对早期 <c>ZL.Gear.Extension.Seat</c>）：
    /// <list type="bullet">
    /// <item>只依赖 Core 契约，不绑 PFLite/PlcBase 等私有 DLL。</item>
    /// <item>只注册行业特异命令；通用 Delay/Assert/Calculate/DynamicFlow 由 Engine 内置模块提供。</item>
    /// <item>配方差异优先改 JSON（Variables / Args），换行业时复制本工程并改命令前缀即可。</item>
    /// </list>
    /// 宿主用法：<c>SequenceExecutorBuilder.Create().WithExtension(new StationExtension()).Build()</c>
    /// </remarks>
    public sealed class StationExtension : IGearExtension
    {
        /// <summary>扩展显示名。</summary>
        public string Name => "Industry.Station";

        /// <summary>
        /// 向调度器双面注册行业命令（Handler + Action），供 Step.Command 与 DynamicFlow ActionKey 共用。
        /// </summary>
        /// <param name="registry">步骤处理器注册表。</param>
        /// <remarks>
        /// RegisterHandlerWithAction 缺一不可：只 RegisterHandler 会导致 DynamicFlow 解析不到 ActionKey（启动期交叉检查会 WARN）。
        /// 命令前缀 Industry.Station.* 便于多扩展并存与按前缀卸载（ModuleLoader.RemoveByPrefix）。
        /// </remarks>
        public void Initialize(IStepHandlerRegistry registry)
        {
            registry.RegisterHandlerWithAction(StationCommands.ApplyRecipe, new ApplyRecipeHandler());
            registry.RegisterHandlerWithAction(StationCommands.ProbeChannel, new ProbeChannelHandler());
            registry.RegisterHandlerWithAction(StationCommands.MarkComplete, new MarkCompleteHandler());
        }
    }

    /// <summary>
    /// 工位扩展命令名常量（ActionKey / Command 平面）。
    /// </summary>
    public static class StationCommands
    {
        /// <summary>下发/应用配方限值到流程共享变量。</summary>
        public const string ApplyRecipe = "Industry.Station.ApplyRecipe";

        /// <summary>模拟通道探测，写入 MeasuredOhm 等共享变量。</summary>
        public const string ProbeChannel = "Industry.Station.ProbeChannel";

        /// <summary>工位收尾：置 StationDone 并记日志。</summary>
        public const string MarkComplete = "Industry.Station.MarkComplete";
    }
}
