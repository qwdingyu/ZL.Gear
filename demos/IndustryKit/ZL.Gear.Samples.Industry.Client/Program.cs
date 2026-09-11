using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine;
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
    /// </code>
    /// </remarks>
    public static class Program
    {
        /// <summary>扩展命令是否已在启动期自检（避免每条用例重复查注册表）。</summary>
        private static bool _extensionRegistrationChecked;

        /// <summary>入口。</summary>
        public static async Task<int> Main(string[] args)
        {
            var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "verify";
            var scenariosDir = ResolveScenariosDir();

            Console.WriteLine("====================================================");
            Console.WriteLine(" ZL.Gear IndustryKit Client（最新架构行业模板）");
            Console.WriteLine("====================================================");
            Console.WriteLine($"场景目录: {scenariosDir}");
            Console.WriteLine($"内置模块: BuiltInModules.Core（无 Sensing/PLC/AI，示范最小组合）");
            Console.WriteLine($"扩展: {new StationExtension().Name}");
            Console.WriteLine();

            try
            {
                return command switch
                {
                    "list" => ListScenarios(scenariosDir),
                    "run" => await RunOneAsync(scenariosDir, args.Skip(1).FirstOrDefault()),
                    "verify" => await VerifyAllAsync(scenariosDir),
                    "help" or "-h" or "--help" => PrintHelp(),
                    _ => await UnknownAsync(command)
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FATAL] {ex.Message}");
                return 99;
            }
        }

        private static int PrintHelp()
        {
            Console.WriteLine("命令:");
            Console.WriteLine("  verify              跑全部验证用例（默认），期望结果全对则 exit 0");
            Console.WriteLine("  run <场景名|路径>   单场景执行并打印 OverallSuccess");
            Console.WriteLine("  list                列出 Scenarios/*.json");
            Console.WriteLine("  help                本帮助");
            return 0;
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

        private static async Task<int> RunOneAsync(string scenariosDir, string? nameOrPath)
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
            return result.OverallSuccess ? 0 : 1;
        }

        private static async Task<int> VerifyAllAsync(string scenariosDir)
        {
            Console.WriteLine("[verify] 开始闭环验证（PASS / 故意 FAIL / 超时契约 / 行业 JSON 分叉）...");
            Console.WriteLine();

            var failures = new List<string>();
            foreach (var testCase in VerificationCatalog.All)
            {
                var path = ResolveScenarioPath(scenariosDir, testCase.ScenarioFile);
                Console.WriteLine($"── {testCase.Id}");
                Console.WriteLine($"   文件: {Path.GetFileName(path)}");
                Console.WriteLine($"   期望: OverallSuccess={testCase.ExpectSuccess}");

                var result = await ExecuteScenarioAsync(path, verbose: false);
                var ok = result.OverallSuccess == testCase.ExpectSuccess;
                if (ok && !string.IsNullOrEmpty(testCase.SummaryContains)
                    && (result.Summary == null
                        || result.Summary.IndexOf(testCase.SummaryContains, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    ok = false;
                }

                if (ok)
                {
                    Console.WriteLine($"   结果: PASS（实际 OverallSuccess={result.OverallSuccess}）");
                }
                else
                {
                    var msg = $"FAIL 期望 Success={testCase.ExpectSuccess}，实际={result.OverallSuccess}；Summary={result.Summary}";
                    Console.WriteLine($"   结果: {msg}");
                    failures.Add($"{testCase.Id}: {msg}");
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
        private static async Task<ZL.Gear.Core.Runner.TestRunResult> ExecuteScenarioAsync(string scenarioPath, bool verbose)
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
                .WithLogger(log)
                // 显式 Core：不默认 All，避免样例被 Sensing/PLC 副作用污染（docs/138 §五）
                .WithBuiltInModules(BuiltInModules.Core)
                .WithExtension(new StationExtension())
                .WithTestStepInterval(0)
                .Build();

            // 启动期一次性自检：扩展命令必须双面注册，否则 verify 全绿也可能是假绿
            EnsureStationExtensionRegistered();

            return await executor.ExecuteAsync(
                steps,
                model: "IndustryKit",
                barcode: "VERIFY-" + Path.GetFileNameWithoutExtension(scenarioPath),
                globalContext: null,
                token: CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 确认 Station 扩展三条命令已进注册表（Handler 侧；Action 侧由 RegisterHandlerWithAction 同步保证）。
        /// </summary>
        private static void EnsureStationExtensionRegistered()
        {
            if (_extensionRegistrationChecked)
            {
                return;
            }

            var registry = WorkflowGlobal.Services.GetService<IStepHandlerRegistry>();

            if (registry == null)
            {
                throw new InvalidOperationException(
                    "无法从 WorkflowGlobal 获取 IStepHandlerRegistry，扩展可能未装载。");
            }

            var registered = new HashSet<string>(
                registry.GetRegisteredCommands(),
                StringComparer.OrdinalIgnoreCase);

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
                    "samples", "IndustryKit", "ZL.Gear.Samples.Industry.Client", "Scenarios"))
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
