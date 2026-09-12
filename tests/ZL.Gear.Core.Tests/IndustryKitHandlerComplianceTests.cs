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
    }
}
