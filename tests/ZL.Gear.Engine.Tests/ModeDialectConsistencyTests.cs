using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Engine.Tests
{
    /// <summary>
    /// 判定模式方言一致性测试（180 TodoList · G7-03）。
    /// </summary>
    /// <remarks>
    /// 灾难背景（172 §1.1 / 174 §一 / 178 §2.5 一致记载）：
    /// <list type="bullet">
    /// <item><c>ExpectedSpec.Mode</c> 的 XML 注释曾只列 7 种模式，而 <c>ResultEvaluator</c> switch 实为 16 种
    /// （less_lcl/less_ucl/big_lcl/big_ucl/bool/mask/bit_set/regex/not_contains 曾长期漏列）。</item>
    /// <item>若实施者以注释为「规格说明书」去实现判定器，将漏实现 9 种模式 → 产线「漏检」灾难
    /// （其中 <c>big_ucl</c> 正在 YC 配方 19 处使用，语义为「&gt; UCL 才合格」，漏实现即反向判定）。</item>
    /// <item>本测试把「引擎 switch 恰好 16 种」与「注释列全 16 种」固化为 CI 约束，
    /// 从机制上杜绝注释/实现再次分叉（这正是 180 G7-03 的要求）。</item>
    /// </list>
    /// 真值源：<c>src/ZL.Gear.Engine/Evaluation/ResultEvaluator.cs</c> 的 <c>case "…"</c> 标签集合。
    /// 修改本测试的规范集 = 修改判定方言契约，必须同时更新 ResultEvaluator switch 与 ExpectedSpec 注释。
    /// </remarks>
    [TestFixture]
    public class ModeDialectConsistencyTests
    {
        /// <summary>
        /// 判定模式规范全集（16 种）——必须与 ResultEvaluator switch 的 distinct case 完全一致。
        /// 分组口径与 ExpectedSpec.Mode 注释的三组一致：数值 / 布尔·位 / 字符串。
        /// </summary>
        private static readonly string[] CanonicalModes =
        {
            // —— 数值类（含 legacy 单边限值 & 反向判定：less_*/big_*）——
            "range", "equals", "lcl_only", "ucl_only",
            "less_lcl", "less_ucl", "big_lcl", "big_ucl",
            // —— 布尔 / 位 ——
            "bool", "mask", "bit_set",
            // —— 字符串 ——
            "regex", "string_equals", "contains", "not_contains", "has_value"
        };

        /// <summary>
        /// 断言 1：ResultEvaluator 的 switch case 集合（去重后）恰好等于规范 16 种。
        /// 多于/少于/改名任一模式都会在此暴露——新增模式必须同步三处（switch、ExpectedSpec 注释、本测试）。
        /// </summary>
        [Test]
        public void ResultEvaluator_switch模式集合_恰好为规范16种()
        {
            // 定位仓库根（自测试输出目录向上找 ZL.Gear.sln），再定位判定引擎源码。
            var repoRoot = GearTestPaths.FindRepoRoot(TestContext.CurrentContext.TestDirectory);
            var evaluatorPath = Path.Combine(
                repoRoot, "src", "ZL.Gear.Engine", "Evaluation", "ResultEvaluator.cs");

            Assert.That(File.Exists(evaluatorPath), Is.True,
                $"找不到 ResultEvaluator.cs: {evaluatorPath}（公开引擎路径被移动？）");

            var source = File.ReadAllText(evaluatorPath);

            // ResultEvaluator 内含多个 switch（数值/字符串分流），同名 case 会重复出现，故先提取再去重。
            var actualModes = Regex.Matches(source, @"case\s+""([a-z_]+)""")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var expectedModes = CanonicalModes
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actualModes, Is.EqualTo(expectedModes),
                "ResultEvaluator switch 模式集合偏离规范 16 种。"
                + "新增/删除/改名任一模式都会导致判定语义与 ExpectedSpec 注释漂移，"
                + "必须同步更新 switch、ExpectedSpec.Mode 注释、以及本测试的 CanonicalModes。");
        }

        /// <summary>
        /// 断言 2：ExpectedSpec.Mode 上方 XML 注释必须列全 16 种模式（防「注释仅 7 种」陷阱复发）。
        /// 只检查 Mode 属性紧邻的注释块，避免误用类顶部说明。
        /// </summary>
        [Test]
        public void ExpectedSpec_Mode注释_列全16种模式()
        {
            var repoRoot = GearTestPaths.FindRepoRoot(TestContext.CurrentContext.TestDirectory);
            var specPath = Path.Combine(
                repoRoot, "src", "ZL.Gear.Core", "Models", "ExpectedSpec.cs");

            Assert.That(File.Exists(specPath), Is.True,
                $"找不到 ExpectedSpec.cs: {specPath}（公开 Core 路径被移动？）");

            var lines = File.ReadAllLines(specPath);

            // 定位 Mode 属性所在行，取其上方紧邻的 XML 注释块（<summary> 内容行）。
            var modeIndex = Array.FindIndex(lines, l => l.Contains("public string Mode"));
            Assert.That(modeIndex, Is.GreaterThanOrEqualTo(0), "ExpectedSpec.cs 未找到 Mode 属性");

            var commentBlock = lines
                .Skip(Math.Max(0, modeIndex - 8))
                .Take(modeIndex)
                .Where(l => l.TrimStart().StartsWith("///"));
            var commentText = string.Join("\n", commentBlock);

            // 逐模式核对：模式名必须作为独立词出现在注释中（\b 词边界防误匹配）。
            var missing = CanonicalModes
                .Where(m => !Regex.IsMatch(commentText, $@"\b{Regex.Escape(m)}\b"))
                .ToList();

            Assert.That(missing, Is.Empty,
                "ExpectedSpec.Mode 的 XML 注释未列全规范 16 种模式，缺少: " + string.Join(", ", missing)
                + "（172/174 记载的「注释仅 7 种」陷阱：注释与 switch 必须同步，勿再回退）。");
        }
    }
}