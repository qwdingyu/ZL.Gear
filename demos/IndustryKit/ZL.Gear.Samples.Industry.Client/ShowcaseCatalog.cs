using System.Collections.Generic;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// 能力橱窗运行顺序（非门禁；verify 仍只跑 <see cref="VerificationCatalog"/>）。
    /// </summary>
    public sealed class ShowcaseItem
    {
        public string Title { get; }
        public string ScenarioFile { get; }
        public string Highlight { get; }

        public ShowcaseItem(string title, string scenarioFile, string highlight)
        {
            Title = title;
            ScenarioFile = scenarioFile;
            Highlight = highlight;
        }
    }

    public static class ShowcaseCatalog
    {
        public static IReadOnlyList<ShowcaseItem> All { get; } = new[]
        {
            new ShowcaseItem(
                "① 框架内核橱窗（无行业 Handler 也可跑）",
                "Gear_Core_Showcase.json",
                "Calculate / Assert / Parallel / Retry / WaitUntil / Finally"),
            new ShowcaseItem(
                "② 标准工位合格路径",
                "Station_HappyPath.json",
                "Industry.Station.* + 双 Assert + MarkComplete"),
            new ShowcaseItem(
                "③ 行业 + 控制流一体化 ATE",
                "Station_Integrated_Ate.json",
                "Parallel 准备 + WaitUntil(ChannelReady) + 行业三步法"),
            new ShowcaseItem(
                "④ Args 覆盖（仪表读数覆盖仿真值）",
                "Station_ProbeOverride.json",
                "MeasuredOhm Args 显式覆盖 Variables.SimulatedOhm"),
            new ShowcaseItem(
                "⑤ 行业 JSON 分叉（安全带叙事）",
                "Fork_Seatbelt_Like.json",
                "换 RecipeId/限值，扩展 DLL 不变")
        };
    }
}
