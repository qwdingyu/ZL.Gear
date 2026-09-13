using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Runner;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Runner;
using ZL.Gear.Extension.Station;
namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// 行业模板客户端：调用最新 L-Test（SequenceExecutor）+ L-Adapter（StationExtension）跑 DynamicFlow 配方，并做期望结果闭环验证。
    /// </summary>
    /// <remarks>
    /// 用法：
    /// <code>
    /// dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -- verify
    /// dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -- run Station_HappyPath
    /// dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -- --verbose verify
    /// </code>
    /// </remarks>
    public static class Program
    {
        /// <summary>扩展命令是否已在启动期自检（避免每条用例重复查注册表）。</summary>
        private static bool _extensionRegistrationChecked;

        /// <summary>入口。</summary>
        public static async Task<int> Main(string[] args)
        {
            var verbose = args.Any(a => string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase));
            var report = args.Any(a => string.Equals(a, "--report", StringComparison.OrdinalIgnoreCase));
            var commandArgs = args
                .Where(a => !string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(a, "--report", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var command = commandArgs.Length > 0 ? commandArgs[0].Trim().ToLowerInvariant() : "welcome";
            var scenariosDir = ResolveScenariosDir();

            Console.WriteLine("====================================================");
            Console.WriteLine(" ZL.Gear IndustryKit — 行业扩展 + JSON 配方 Demo");
            Console.WriteLine("====================================================");
            Console.WriteLine($"场景目录: {scenariosDir}");
            Console.WriteLine($"扩展插件: {new StationExtension().Name}（电阻工位样板 · 可替换为你的行业）");
            Console.WriteLine($"运行模式: LogicOnly（无仪器驱动 · 适合学习与集成验证）");
            Console.WriteLine();

            try
            {
                return command switch
                {
                    "welcome" => PrintWelcome(),
                    "quickstart" or "start" => await QuickstartAsync(scenariosDir),
                    "learn" or "tour" => await LearnAsync(scenariosDir, verbose || report),
                    "list" => ListScenarios(scenariosDir),
                    "capabilities" or "caps" => PrintCapabilities(),
                    "showcase" or "demo" => await ShowcaseAsync(scenariosDir, verbose || report),
                    "run" => await RunOneAsync(scenariosDir, commandArgs.Skip(1).FirstOrDefault(), verbose, report),
                    "verify" => await VerifyAllAsync(scenariosDir, verbose),
                    "bootstrap-demo" => PrintSeatBootstrapDoc(),
                    "help" or "-h" or "--help" => PrintHelp(),
                    _ => await UnknownAsync(command)
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FATAL] {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex);
                }

                return 99;
            }
        }

        private static int PrintWelcome()
        {
            OnboardingGuide.PrintWelcome();
            Console.WriteLine("命令一览：help");
            return 0;
        }

        private static int PrintHelp()
        {
            OnboardingGuide.PrintWelcome();
            Console.WriteLine("命令:");
            Console.WriteLine("  quickstart | start  ★ 第一次必跑：1 个合格场景 + 步骤树");
            Console.WriteLine("  learn | tour        深度学习路径（6 步 PASS，约 2 分钟）");
            Console.WriteLine("  showcase | demo     产品能力橱窗（5 个代表场景）");
            Console.WriteLine("  capabilities | caps 能力矩阵 + 全系统能力地图");
            Console.WriteLine("  run <场景名|路径>   单场景执行（建议加 --report 看步骤树）");
            Console.WriteLine("  list                列出 Scenarios/*.json");
            Console.WriteLine("  verify              CI 门禁（7 条，含故意 FAIL · 非日常演示）");
            Console.WriteLine("  bootstrap-demo      座椅 Bootstrap 文档入口（完整验证见私有 ConsoleApp seat-*）");
            Console.WriteLine("  welcome             本说明（无参数默认）");
            Console.WriteLine("  help                本帮助");
            Console.WriteLine();
            Console.WriteLine("全局选项:");
            Console.WriteLine("  --verbose           失败时输出完整异常栈");
            Console.WriteLine("  --report            run/showcase/quickstart 后打印步骤执行树");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine("  dotnet run --project ... -c Release -- quickstart");
            Console.WriteLine("  dotnet run --project ... -c Release -- showcase");
            Console.WriteLine("  dotnet run --project ... -c Release -- --report run Station_HappyPath");
            return 0;
        }

        /// <summary>客户开发者 5 分钟路径：单场景 + 步骤树 + 下一步提示。</summary>
        private static async Task<int> QuickstartAsync(string scenariosDir)
        {
            OnboardingGuide.PrintQuickstartIntro();

            var path = ResolveScenarioPath(scenariosDir, "Station_HappyPath");
            var result = await ExecuteScenarioAsync(path, verbose: true);
            RunReportPrinter.Print(result);

            Console.WriteLine();
            Console.WriteLine($"OverallSuccess = {result.OverallSuccess}");
            OnboardingGuide.PrintQuickstartNextSteps(result.OverallSuccess);

            return result.OverallSuccess ? 0 : 1;
        }

        /// <summary>公开轨文档入口；盐城 Bootstrap 实现在私有 ZL.Gear.Demos（G3-01 已迁出 IndustryKit）。</summary>
        private static int PrintSeatBootstrapDoc()
        {
            Console.WriteLine("[bootstrap-demo] 盐城座椅 legacy StepConfig 树 — 公开 IndustryKit 不含 Seat Bootstrap（178/179）");
            Console.WriteLine("文档: ZL.Gear.Docs/174 §五 B-1～B-6 · 180 G5");
            Console.WriteLine();
            Console.WriteLine("Canonical 宿主（Gear.All 私有轨 · ZL.Gear.Demos）:");
            Console.WriteLine("  代码: ZL.Gear.Demos/ZL.Gear.ConsoleApp/Seat/SeatHostBootstrap.cs");
            Console.WriteLine("  dotnet run --project ../../ZL.Gear.Demos/ZL.Gear.ConsoleApp -- seat-load");
            Console.WriteLine("  dotnet run --project ../../ZL.Gear.Demos/ZL.Gear.ConsoleApp -- seat-bootstrap");
            Console.WriteLine("  dotnet run --project ../../ZL.Gear.Demos/ZL.Gear.ConsoleApp -- seat-noise-smoke");
            return 0;
        }

        private static int PrintCapabilities()
        {
            Console.WriteLine("Gear.NET 能力矩阵 — IndustryKit 已演示（LogicOnly + Core）");
            Console.WriteLine("全景图：demos/IndustryKit/CAPABILITIES.md");
            Console.WriteLine(new string('-', 72));
            foreach (var entry in CapabilityCatalog.All)
            {
                Console.WriteLine($"[{entry.Layer}] {entry.Capability}");
                Console.WriteLine($"  场景: {entry.ScenarioFile}");
                Console.WriteLine($"  证明: {entry.Proof}");
                Console.WriteLine();
            }

            Console.WriteLine(new string('-', 72));
            Console.WriteLine("以下能力框架已具备，IndustryKit 未挂载（需 Instrumented / 私有轨）：");
            foreach (var deferred in SystemCapabilityMap.InstrumentedOrPrivate)
            {
                Console.WriteLine($"  · {deferred.Capability}");
                Console.WriteLine($"    位置: {deferred.Where}");
                Console.WriteLine($"    说明: {deferred.WhyNotInIndustryKit}");
            }

            return 0;
        }

        /// <summary>按认知顺序跑 6 个 PASS 场景，帮客户开发者建立完整心智模型。</summary>
        private static async Task<int> LearnAsync(string scenariosDir, bool report)
        {
            Console.WriteLine("[learn] 深度学习路径 — 6 步（全部期望 PASS，不含 verify 故意 FAIL）");
            Console.WriteLine("提示：每步可加 --report 看步骤树；详述见 CAPABILITIES.md");
            Console.WriteLine();

            var failed = 0;
            foreach (var step in LearningCatalog.All)
            {
                Console.WriteLine($"── 第 {step.Order} 步：{step.Title}");
                Console.WriteLine($"   学到: {step.WhatYouLearn}");
                Console.WriteLine($"   场景: {step.ScenarioFile}");
                Console.WriteLine();

                var path = ResolveScenarioPath(scenariosDir, step.ScenarioFile);
                var result = await ExecuteScenarioAsync(path, verbose: true);
                Console.WriteLine($"   → OverallSuccess={result.OverallSuccess}");
                if (report)
                {
                    RunReportPrinter.Print(result);
                }

                if (!result.OverallSuccess)
                {
                    failed++;
                }

                Console.WriteLine();
            }

            Console.WriteLine("门禁专用（故意 FAIL）：Station_AssertFail · Station_TimeoutContract → 用 verify 命令");
            if (failed == 0)
            {
                Console.WriteLine("LEARN_ALL_PASS");
                return 0;
            }

            Console.WriteLine($"LEARN_FAIL ({failed} 步失败)");
            return 1;
        }

        private static async Task<int> ShowcaseAsync(string scenariosDir, bool report)
        {
            Console.WriteLine("[showcase] 产品能力橱窗 — 5 个代表场景（全部期望 PASS）");
            Console.WriteLine("提示：这是产品演示，不是发版门禁；门禁请用 verify（含故意 FAIL）。");
            Console.WriteLine();

            var failed = 0;
            foreach (var item in ShowcaseCatalog.All)
            {
                Console.WriteLine($"══ {item.Title}");
                Console.WriteLine($"   场景: {item.ScenarioFile}");
                Console.WriteLine($"   亮点: {item.Highlight}");
                Console.WriteLine();

                var path = ResolveScenarioPath(scenariosDir, item.ScenarioFile);
                var result = await ExecuteScenarioAsync(path, verbose: true);
                Console.WriteLine($"   → OverallSuccess={result.OverallSuccess}");
                if (report)
                {
                    RunReportPrinter.Print(result);
                }

                if (!result.OverallSuccess)
                {
                    failed++;
                }

                Console.WriteLine();
            }

            if (failed == 0)
            {
                Console.WriteLine("SHOWCASE_ALL_PASS");
                return 0;
            }

            Console.WriteLine($"SHOWCASE_FAIL ({failed} 场景失败)");
            return 1;
        }

        private static async Task<int> UnknownAsync(string command)
        {
            Console.Error.WriteLine($"未知命令: {command}");
            PrintHelp();
            return 2;
        }

        private static int ListScenarios(string scenariosDir)
        {
            foreach (var file in ScenarioLoader.GetAllScenarioFiles(scenariosDir).OrderBy(f => f))
            {
                Console.WriteLine(Path.GetFileName(file));
            }

            return 0;
        }

        private static async Task<int> RunOneAsync(string scenariosDir, string? nameOrPath, bool verbose, bool report)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
            {
                Console.Error.WriteLine("用法: run <场景名|路径>，例如: run Station_HappyPath");
                return 2;
            }

            var path = ResolveScenarioPath(scenariosDir, nameOrPath);
            var result = await ExecuteScenarioAsync(path, verbose: true);
            Console.WriteLine();
            Console.WriteLine($"OverallSuccess = {result.OverallSuccess}");
            Console.WriteLine($"Summary        = {result.Summary}");
            if (report || verbose)
            {
                RunReportPrinter.Print(result);
            }

            return result.OverallSuccess ? 0 : 1;
        }

        private static async Task<int> VerifyAllAsync(string scenariosDir, bool verbose)
        {
            Console.WriteLine($"[verify] 开始闭环验证（{VerificationCatalog.All.Count} 条门禁）...");
            Console.WriteLine();

            var failures = new List<string>();
            foreach (var testCase in VerificationCatalog.All)
            {
                var path = ResolveScenarioPath(scenariosDir, testCase.ScenarioFile);
                Console.WriteLine($"── {testCase.Id}");
                Console.WriteLine($"   文件: {Path.GetFileName(path)}");
                Console.WriteLine($"   期望: OverallSuccess={testCase.ExpectSuccess}"
                                  + (string.IsNullOrEmpty(testCase.SummaryContains)
                                      ? string.Empty
                                      : $", Summary∋「{testCase.SummaryContains}」"));

                var result = await ExecuteScenarioAsync(path, verbose: false);
                if (MatchesExpectation(testCase, result, out var mismatch))
                {
                    Console.WriteLine($"   结果: PASS（实际 OverallSuccess={result.OverallSuccess}）");
                }
                else
                {
                    Console.WriteLine($"   结果: FAIL {mismatch}");
                    failures.Add($"{testCase.Id}: {mismatch}");
                    if (verbose && !string.IsNullOrWhiteSpace(result.Summary))
                    {
                        Console.WriteLine("   --- Summary ---");
                        foreach (var line in result.Summary.Split('\n'))
                        {
                            Console.WriteLine($"   {line.TrimEnd()}");
                        }
                    }
                }

                Console.WriteLine();
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("INDUSTRY_KIT_VERIFY_PASS");
                return 0;
            }

            Console.WriteLine("INDUSTRY_KIT_VERIFY_FAIL");
            foreach (var f in failures)
            {
                Console.WriteLine($"  - {f}");
            }

            return 1;
        }

        /// <summary>判定单条验证用例是否满足期望（OverallSuccess + 可选 Summary 子串）。</summary>
        private static bool MatchesExpectation(VerificationCase testCase, TestRunResult result, out string mismatch)
        {
            if (result.OverallSuccess != testCase.ExpectSuccess)
            {
                mismatch = $"期望 Success={testCase.ExpectSuccess}，实际={result.OverallSuccess}";
                return false;
            }

            if (!string.IsNullOrEmpty(testCase.SummaryContains)
                && !ResultContainsText(result, testCase.SummaryContains))
            {
                mismatch = $"未找到期望文本「{testCase.SummaryContains}」；Summary 首行={FirstLine(result.Summary)}";
                return false;
            }

            mismatch = string.Empty;
            return true;
        }

        private static string FirstLine(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "(empty)";
            }

            var idx = text.IndexOf('\n');
            return idx < 0 ? text : text.Substring(0, idx);
        }

        /// <summary>在 Summary 或步骤树 Message 中查找子串（Log 输出在步骤 Message 内）。</summary>
        private static bool ResultContainsText(TestRunResult result, string needle)
        {
            if (result.Summary != null
                && result.Summary.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return StepTreeContains(result.StepResults, needle);
        }

        private static bool StepTreeContains(IReadOnlyList<StepRunResult> steps, string needle)
        {
            foreach (var step in steps)
            {
                if (ContainsIgnoreCase(step.Message, needle)
                    || ContainsIgnoreCase(step.StepName, needle)
                    || ContainsIgnoreCase(step.StepConfig?.StepKey, needle))
                {
                    return true;
                }

                if (StepTreeContains(step.SubStepResults, needle))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string? haystack, string needle) =>
            haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// 执行单个场景 JSON（L-Test 宿主 + L-DSL DynamicFlow + L-Adapter 扩展）。
        /// </summary>
        /// <remarks>
        /// 关键口径（防误解）：
        /// <list type="bullet">
        /// <item><c>BuiltInModules.Core</c>：仅逻辑 DSL（DynamicFlow/Assert/Calculate/Delay），不挂 Sensing/PLC/AI，示范最小组合根。</item>
        /// <item>场景 <c>EvaluateResult:false</c>：跳过步骤级 ExpectedResults 评测，但 DynamicFlow 内 Assert 失败仍令 <c>measurementResult.Success=false</c> → OverallSuccess 可失败。</item>
        /// <item><c>WithExtension</c>：扩展须 RegisterHandlerWithAction，否则 JSON ActionKey 无法解析（灾难型静默跳过）。</item>
        /// </list>
        /// </remarks>
        private static async Task<TestRunResult> ExecuteScenarioAsync(string scenarioPath, bool verbose)
        {
            Action<string> log = verbose
                ? Console.WriteLine
                : (Action<string>)(_ => { });

            if (verbose)
            {
                Console.WriteLine($"[run] 加载: {scenarioPath}");
            }

            var steps = ScenarioLoader.Load(scenarioPath);

            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithLogger(log)
                // 显式 Core：不默认 All，避免样例被 Sensing/PLC 副作用污染（docs/138 §五）
                .WithBuiltInModules(BuiltInModules.Core)
                .WithExtension(new StationExtension())
                .WithTestStepInterval(0)
                .Build();

            // 启动期一次性自检：扩展命令必须双面注册，否则 verify 全绿也可能是假绿
            EnsureStationExtensionRegistered(executor);

            var scenarioName = Path.GetFileNameWithoutExtension(scenarioPath);
            var model = "IndustryKit";
            var barcode = "SN-" + scenarioName;
            // GlobalContext 供 Handler 读条码/型号（StepArgsReader.GlobalOnly）；插值 ${} 仍走 Variables
            var globalContext = new Dictionary<string, object>
            {
                ["Model"] = model,
                ["Barcode"] = barcode,
                ["Operator"] = "IndustryKit-Demo"
            };

            if (verbose)
            {
                Console.WriteLine($"[run] GlobalContext: Model={model}, Barcode={barcode}");
            }

            return await executor.ExecuteAsync(
                steps,
                model: model,
                barcode: barcode,
                globalContext: globalContext,
                token: CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 确认 Station 扩展三条命令已进注册表（Handler 侧；Action 侧由 RegisterHandlerWithAction 同步保证）。
        /// </summary>
        private static void EnsureStationExtensionRegistered(SequenceExecutor executor)
        {
            if (_extensionRegistrationChecked)
            {
                return;
            }

            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            var registered = new HashSet<string>(
                executor.GetRegisteredStepCommands(),
                StringComparer.OrdinalIgnoreCase);

            if (registered.Count == 0)
            {
                throw new InvalidOperationException(
                    "无法从本 Runtime 获取已注册命令，扩展可能未装载。");
            }

            foreach (var cmd in new[]
                     {
                         StationCommands.ApplyRecipe,
                         StationCommands.ProbeChannel,
                         StationCommands.MarkComplete
                     })
            {
                if (!registered.Contains(cmd))
                {
                    throw new InvalidOperationException(
                        $"行业命令未注册: {cmd}。请确认 WithExtension 且扩展使用 RegisterHandlerWithAction。");
                }
            }

            _extensionRegistrationChecked = true;
        }

        /// <summary>
        /// 解析场景目录：优先输出目录（CopyToOutputDirectory），便于 dotnet run 与 CI。
        /// </summary>
        private static string ResolveScenariosDir()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Scenarios"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Scenarios")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Scenarios")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
                    "demos", "IndustryKit", "ZL.Gear.Samples.Industry.Client", "Scenarios"))
            };

            foreach (var dir in candidates)
            {
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.json").Length > 0)
                {
                    return dir;
                }
            }

            throw new DirectoryNotFoundException(
                "找不到 Scenarios 目录。请从仓库根目录运行，或确保 json 已 CopyToOutputDirectory。");
        }

        private static string ResolveScenarioPath(string scenariosDir, string nameOrPath)
        {
            if (File.Exists(nameOrPath))
            {
                return Path.GetFullPath(nameOrPath);
            }

            var withJson = nameOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? nameOrPath
                : nameOrPath + ".json";

            var combined = Path.Combine(scenariosDir, withJson);
            if (File.Exists(combined))
            {
                return combined;
            }

            throw new FileNotFoundException($"场景不存在: {nameOrPath}", combined);
        }
    }
}
