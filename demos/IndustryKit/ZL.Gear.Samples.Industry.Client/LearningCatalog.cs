using System.Collections.Generic;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// 客户开发者「深度学习路径」：按认知顺序跑 PASS 场景（不含 verify 故意 FAIL）。
    /// </summary>
    public sealed class LearningStep
    {
        public int Order { get; }
        public string Title { get; }
        public string ScenarioFile { get; }
        public string WhatYouLearn { get; }

        public LearningStep(int order, string title, string scenarioFile, string whatYouLearn)
        {
            Order = order;
            Title = title;
            ScenarioFile = scenarioFile;
            WhatYouLearn = whatYouLearn;
        }
    }

    public static class LearningCatalog
    {
        /// <summary>推荐学习顺序（约 2 分钟，全部期望 PASS）。</summary>
        public static IReadOnlyList<LearningStep> All { get; } = new[]
        {
            new LearningStep(1, "最短合格工位", "Station_HappyPath.json",
                "行业三步 + Calculate + Assert；先看 OverallSuccess 怎么闭环"),
            new LearningStep(2, "框架内核橱窗", "Gear_Core_Showcase.json",
                "Parallel/Retry/WaitUntil/Finally/节点级 Condition；L1 Check + L0 Left/Op/Right"),
            new LearningStep(3, "Assert L2 Condition", "Gear_Assert_L2_Condition.json",
                "第三种 Assert 方言：Condition 布尔或表达式（docs/006）"),
            new LearningStep(4, "Args 覆盖 fail-closed", "Station_ProbeOverride.json",
                "显式 MeasuredOhm 必须生效，禁止静默回退"),
            new LearningStep(5, "行业 + 控制流一体", "Station_Integrated_Ate.json",
                "Parallel + WaitUntil(ChannelReady) + Retry"),
            new LearningStep(6, "换行业只改 JSON", "Fork_Seatbelt_Like.json",
                "同扩展 DLL，不同 RecipeId/限值叙事")
        };
    }
}
