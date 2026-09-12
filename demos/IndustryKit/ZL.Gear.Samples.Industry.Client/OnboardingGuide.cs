using System;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// 客户开发者上手引导文案（控制台输出，非 CI 门禁）。
    /// </summary>
    internal static class OnboardingGuide
    {
        /// <summary>无参数启动或 help 时打印的价值说明与推荐路径。</summary>
        public static void PrintWelcome()
        {
            Console.WriteLine("【ZL.Gear 是什么】");
            Console.WriteLine("  Headless 测试序列框架：JSON 描述步骤，Engine 执行，Handler 做设备动作，Assert 做合格判定。");
            Console.WriteLine("  本 Demo 模拟电阻测试工位（无硬件），展示如何写行业扩展 + 配方 JSON。");
            Console.WriteLine();
            Console.WriteLine("【30 秒看懂架构】");
            Console.WriteLine("  JSON 配方 ──→ SequenceExecutor（框架）──→ StationExtension（行业插件，你可复制）");
            Console.WriteLine("                      └── Calculate / Assert（判据在 JSON，不在 Handler if）");
            Console.WriteLine();
            Console.WriteLine("【推荐上手顺序（客户开发者）】");
            Console.WriteLine("  1. quickstart     跑 1 个合格场景 + 步骤树（约 10 秒，第一次必跑）");
            Console.WriteLine("  2. learn          6 步深度学习（框架+行业+Assert L2，约 2 分钟）");
            Console.WriteLine("  3. showcase       5 个代表场景，产品演示橱窗");
            Console.WriteLine("  4. capabilities   能力矩阵 + 全系统能力地图");
            Console.WriteLine("  5. 打开 Scenarios/Station_HappyPath.json 对照 --report 步骤树");
            Console.WriteLine();
            Console.WriteLine("能力全景：demos/IndustryKit/CAPABILITIES.md");
            Console.WriteLine();
            Console.WriteLine("【维护者 / CI】");
            Console.WriteLine("  verify            7 条门禁（含故意 FAIL + 超时，发版用）");
            Console.WriteLine("  ./demos/IndustryKit/verify.sh");
            Console.WriteLine();
            Console.WriteLine("详细图文：demos/IndustryKit/GETTING_STARTED.md");
            Console.WriteLine(new string('-', 72));
        }

        /// <summary>quickstart 运行前说明本次将看到什么。</summary>
        public static void PrintQuickstartIntro()
        {
            Console.WriteLine("【quickstart】你将看到一条完整的「工位合格路径」：");
            Console.WriteLine("  ApplyRecipe（写限值）→ ProbeChannel（模拟读数）→ Calculate（算裕量）→ Assert（判合格）→ MarkComplete");
            Console.WriteLine("  对应 JSON：Scenarios/Station_HappyPath.json");
            Console.WriteLine("  下方日志即 Engine 逐步执行过程；结束后打印步骤树。");
            Console.WriteLine(new string('-', 72));
            Console.WriteLine();
        }

        /// <summary>quickstart 结束后根据结果给出下一步。</summary>
        public static void PrintQuickstartNextSteps(bool success)
        {
            Console.WriteLine(new string('-', 72));
            if (success)
            {
                Console.WriteLine("✓ quickstart 完成 — 你已看到一次完整的 PASS 闭环。");
            }
            else
            {
                Console.WriteLine("✗ quickstart 未 PASS — 用 --verbose 重跑或检查 Scenarios 是否完整。");
            }

            Console.WriteLine();
            Console.WriteLine("建议下一步：");
            Console.WriteLine("  dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- learn");
            Console.WriteLine("  dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -c Release -- capabilities");
            Console.WriteLine("  阅读 demos/IndustryKit/CAPABILITIES.md · GETTING_STARTED.md");
        }
    }
}
