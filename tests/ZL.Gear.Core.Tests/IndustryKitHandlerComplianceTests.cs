using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Core.Tests
{
    /// <summary>
    /// IndustryKit 行业 Handler 产线合规（公开轨 · 仅扫描模板三 Handler）。
    /// </summary>
    /// <remarks>
    /// 与私有仓 Drivers.Tests 同名测试语义一致；路径解析统一见 <see cref="GearTestPaths"/>。
    /// </remarks>
    [TestFixture]
    public class IndustryKitHandlerComplianceTests
    {
        private static IEnumerable<string> HandlerSourceFiles()
        {
            var dir = GearTestPaths.IndustryKitHandlersDir(TestContext.CurrentContext.TestDirectory);
            Assert.That(Directory.Exists(dir), Is.True, $"IndustryKit Handlers 目录不存在: {dir}");
            return Directory.GetFiles(dir, "*Handler.cs");
        }

        [Test]
        public void IndustryKit_Handlers_不得使用Variables_Set写流程变量()
        {
            var hits = new List<string>();
            foreach (var file in HandlerSourceFiles())
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.IndexOf("Variables.Set(", StringComparison.Ordinal) >= 0
                        && line.IndexOf("SetShared", StringComparison.Ordinal) < 0)
                    {
                        hits.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                    }
                }
            }

            Assert.That(hits, Is.Empty,
                "行业 Handler 须 args.SetShared，禁止 Variables.Set：\n" + string.Join("\n", hits));
        }

        [Test]
        public void IndustryKit_Handlers_不得用context_Get读参数()
        {
            var hits = HandlerSourceFiles()
                .Where(file => File.ReadAllText(file).IndexOf("context.Get", StringComparison.Ordinal) >= 0)
                .Select(Path.GetFileName)
                .ToList();

            Assert.That(hits, Is.Empty,
                "行业 Handler 须 StepArgsReader，禁止 context.Get：\n" + string.Join("\n", hits));
        }

        [Test]
        public void IndustryKit_ApplyRecipe_限值须ArgsOnly()
        {
            var path = Path.Combine(
                GearTestPaths.IndustryKitHandlersDir(TestContext.CurrentContext.TestDirectory),
                "ApplyRecipeHandler.cs");
            Assert.That(File.Exists(path), Is.True);
            var text = File.ReadAllText(path);
            StringAssert.Contains("TryRequirePositiveDouble(\"LimitOhm\", StepArgSource.ArgsOnly", text);
            StringAssert.Contains("TryRequireString(\"RecipeId\", StepArgSource.ArgsOnly", text);
            Assert.That(
                text.IndexOf("LimitOhm\", StepArgSource.All", StringComparison.Ordinal) < 0
                && text.IndexOf("LimitOhm\", StepArgSource.ArgsThenVariables", StringComparison.Ordinal) < 0,
                "LimitOhm 不得用 All/ArgsThenVariables");
        }

        [Test]
        public void IndustryKit_AllHandlers_限值须ArgsOnly_无All回退()
        {
            var dir = GearTestPaths.IndustryKitHandlersDir(TestContext.CurrentContext.TestDirectory);
            Assert.That(Directory.Exists(dir), Is.True, $"IndustryKit Handlers 目录不存在: {dir}");

            var hits = new List<string>();
            foreach (var file in Directory.GetFiles(dir, "*Handler.cs"))
            {
                var text = File.ReadAllText(file);
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    text,
                    @"TryRequire\w+\(\s*""([^""]+)""\s*,\s*StepArgSource\.(All|ArgsThenVariables)");
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    hits.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value} 使用 {m.Groups[2].Value}");
                }
            }

            Assert.That(hits, Is.Empty,
                "行业 Handler 必填限值须 StepArgSource.ArgsOnly，禁止 All/ArgsThenVariables 回退：\n" + string.Join("\n", hits));
        }

        [Test]
        public void IndustryKit_Scenarios_Parallel节点不得包含Measure子节点()
        {
            var dir = GearTestPaths.IndustryKitScenariosDir(TestContext.CurrentContext.TestDirectory);
            Assert.That(Directory.Exists(dir), Is.True, $"IndustryKit Scenarios 目录不存在: {dir}");

            var hits = new List<string>();
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var text = File.ReadAllText(file);
                var pattern = @"\{\s*""Type""\s*:\s*""(?:Parallel|Group)""[^}]*""Children""\s*:\s*\[[^\]]*\{\s*""Type""\s*:\s*""Measure""";
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    text, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    hits.Add($"{Path.GetFileName(file)}: 发现 Parallel/Group 内嵌 Measure 子节点");
                }
            }

            Assert.That(hits, Is.Empty,
                "DynamicFlow 并行节点 Parallel/Group 不得直接包含 Measure 子节点，须拆分为 Sequence：\n" + string.Join("\n", hits));
        }
    }
}
