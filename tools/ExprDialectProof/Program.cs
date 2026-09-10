using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Workflow;

namespace ZL.Gear.ExprDialectProof
{
    /// <summary>
    /// docs/133 独立论证入口：覆盖清单见 COVERAGE.md。须 PROOF_ALL_PASS 才可批量落地。
    /// </summary>
    public static class Program
    {
        private static int _fail;

        public static async Task<int> Main()
        {
            Console.WriteLine("=== ExprDialectProof (docs/133) ===");
            Console.WriteLine("覆盖说明: COVERAGE.md");

            Check("U1_裸标识符算术", () =>
            {
                var ev = new WorkflowEvaluator();
                var r = ev.EvaluateExpression("LimitOhm - MeasuredOhm", new Dictionary<string, object>
                {
                    ["LimitOhm"] = 5.0,
                    ["MeasuredOhm"] = 2.5
                });
                return Convert.ToDouble(r) == 2.5;
            });

            Check("U2_字典含WorkflowDefinition大对象仍可算", () =>
            {
                var ev = new WorkflowEvaluator();
                var vars = new Dictionary<string, object>
                {
                    ["LimitOhm"] = 5.0,
                    ["MeasuredOhm"] = 2.5,
                    ["WorkflowDefinition"] = new Dictionary<string, object> { ["Sequence"] = new List<object> { "noise" } },
                    ["Sequence"] = new List<object> { 1, 2, 3 }
                };
                var promoted = WorkflowEvaluator.ListPromotableKeys(vars);
                if (promoted.Contains("WorkflowDefinition") || promoted.Contains("Sequence")) return false;
                return Convert.ToDouble(ev.EvaluateExpression("LimitOhm - MeasuredOhm", vars)) == 2.5;
            });

            Check("U3_bool条件Ready", () =>
                new WorkflowEvaluator().EvaluateCondition("Ready", new Dictionary<string, object> { ["Ready"] = true }));

            Check("U4_缺键EvaluateExpression抛", () =>
            {
                try
                {
                    new WorkflowEvaluator().EvaluateExpression("MissingVar + 1", new Dictionary<string, object>());
                    return false;
                }
                catch { return true; }
            });

            Check("U5_非法标识符键不提升_SYS", () =>
            {
                var promoted = WorkflowEvaluator.ListPromotableKeys(new Dictionary<string, object>
                {
                    ["$SYS:SIG:START:x"] = new object(),
                    ["OkVar"] = 1.0
                });
                return !promoted.Exists(k => k.StartsWith("$", StringComparison.Ordinal)) && promoted.Contains("OkVar");
            });

            Check("U6_int提升参与运算", () =>
            {
                var r = new WorkflowEvaluator().EvaluateExpression("LimitOhm - MeasuredOhm", new Dictionary<string, object>
                {
                    ["LimitOhm"] = 5,
                    ["MeasuredOhm"] = 2
                });
                return Convert.ToDouble(r) == 3.0;
            });

            Check("U7_缺键EvaluateCondition为false", () =>
                !new WorkflowEvaluator().EvaluateCondition("MissingFlag", new Dictionary<string, object>()));

            await CheckAsync("F1_结构化Assert失败不误PASS", async () =>
            {
                var r = await RunAsync(MiniFlow(Set("V", 10), AssertStruct("V", "Lt", 5, "应失败"))).ConfigureAwait(false);
                return !r.Success && (r.Message ?? "").Contains("应失败");
            });

            await CheckAsync("F2_结构化Assert成功", async () =>
            {
                var r = await RunAsync(MiniFlow(Set("Margin", 2.5), AssertStruct("Margin", "Gt", 0, "裕量不足"))).ConfigureAwait(false);
                return r.Success;
            });

            await CheckAsync("F3_结构化与Condition双写应失败", async () =>
            {
                var r = await RunAsync(MiniFlow(
                    Set("X", 1),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "X", ["Op"] = "Gt", ["Right"] = 0,
                            ["Condition"] = "true", ["Message"] = "双写"
                        }
                    })).ConfigureAwait(false);
                return !r.Success && (r.Message ?? "").Contains("禁止与 Condition");
            });

            await CheckAsync("F4_Fixture全量橱窗新方言端到端", async () =>
            {
                var r = await RunAsync(LoadFixture("Showcase_NewDialect.json")[0]).ConfigureAwait(false);
                Console.WriteLine("  Showcase msg=" + r.Message);
                return r.Success;
            });

            await CheckAsync("F5_真实DynamicFlow污染参数下裸标识符Calculate", async () =>
            {
                var step = new StepConfig
                {
                    StepKey = "pollute",
                    StepName = "pollute",
                    Command = "DynamicFlow",
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowTimeoutMs"] = 5000,
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Variables"] = new Dictionary<string, object> { ["LimitOhm"] = 5.0 },
                            ["Sequence"] = new List<object>
                            {
                                Set("MeasuredOhm", 1.5),
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "Action",
                                    ["ActionKey"] = "Calculate",
                                    ["Args"] = new Dictionary<string, object>
                                    {
                                        ["Expression"] = "LimitOhm - MeasuredOhm",
                                        ["OutputKey"] = "Margin"
                                    }
                                },
                                AssertStruct("Margin", "Eq", 3.5, "算错")
                            }
                        }
                    }
                };
                var r = await RunAsync(step).ConfigureAwait(false);
                Console.WriteLine("  Pollute msg=" + r.Message);
                return r.Success;
            });

            await CheckAsync("F6_Timeout契约Failed且Finally可走", async () =>
            {
                var r = await RunAsync(LoadFixture("Timeout_Contract.json")[0]).ConfigureAwait(false);
                Console.WriteLine("  Timeout msg=" + r.Message);
                return !r.Success && (r.Message ?? "").Contains("超时");
            });

            await CheckAsync("F7_RightVar成功与缺变量Failed", async () =>
            {
                var ok = await RunAsync(MiniFlow(
                    Set("A", 3.0),
                    Set("Max", 5.0),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "A", ["Op"] = "Lte", ["RightVar"] = "Max", ["Message"] = "超限"
                        }
                    })).ConfigureAwait(false);
                if (!ok.Success) return false;

                var miss = await RunAsync(MiniFlow(
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "NoSuch", ["Op"] = "Gt", ["Right"] = 0, ["Message"] = "缺变量"
                        }
                    })).ConfigureAwait(false);
                return !miss.Success && (miss.Message ?? "").Contains("缺少变量");
            });

            await CheckAsync("F8_Op矩阵", async () =>
            {
                async Task<bool> One(string op, double left, object right, bool expectPass)
                {
                    var r = await RunAsync(MiniFlow(
                        Set("L", left),
                        AssertStruct("L", op, right, "opfail"))).ConfigureAwait(false);
                    return r.Success == expectPass;
                }

                return await One("Gt", 3, 2, true).ConfigureAwait(false)
                       && await One("Gte", 2, 2, true).ConfigureAwait(false)
                       && await One("Lt", 1, 2, true).ConfigureAwait(false)
                       && await One("Lte", 2, 2, true).ConfigureAwait(false)
                       && await One("Eq", 2.5, 2.5, true).ConfigureAwait(false)
                       && await One("Neq", 1, 2, true).ConfigureAwait(false)
                       && await One("Gt", 1, 2, false).ConfigureAwait(false);
            });

            await CheckAsync("F9_字符串Eq_PLC状态形态", async () =>
            {
                var r = await RunAsync(MiniFlow(
                    Set("CylStatus", "ARRIVED"),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "CylStatus", ["Op"] = "Eq", ["Right"] = "ARRIVED", ["Message"] = "未到位"
                        }
                    })).ConfigureAwait(false);
                return r.Success;
            });

            await CheckAsync("F10_Vars逃逸路径仍可用", async () =>
            {
                var r = await RunAsync(MiniFlow(
                    Set("Margin", 2.5),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Condition"] = "Convert.ToDouble(Vars[\"Margin\"]) > 0",
                            ["Message"] = "逃逸失败"
                        }
                    })).ConfigureAwait(false);
                return r.Success;
            });

            await CheckAsync("F11_Calculate失败应Failed", async () =>
            {
                var r = await RunAsync(MiniFlow(
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Calculate",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Expression"] = "NotExist + 1",
                            ["OutputKey"] = "X"
                        }
                    })).ConfigureAwait(false);
                return !r.Success && (r.Message ?? "").Contains("计算失败");
            });

            await CheckAsync("F12_WaitUntil超时应Failed", async () =>
            {
                var step = MiniFlow(
                    Set("Ready", false),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "WaitUntil",
                        ["Description"] = "等不到",
                        ["Condition"] = "Ready",
                        ["TimeoutMs"] = 200,
                        ["IntervalMs"] = 40
                    });
                var r = await RunAsync(step).ConfigureAwait(false);
                return !r.Success && (r.Message ?? "").Contains("超时");
            });

            await CheckAsync("F13_Right与RightVar互斥校验", async () =>
            {
                var both = await RunAsync(MiniFlow(
                    Set("A", 1),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "A", ["Op"] = "Gt", ["Right"] = 0, ["RightVar"] = "A", ["Message"] = "双右"
                        }
                    })).ConfigureAwait(false);
                var neither = await RunAsync(MiniFlow(
                    Set("A", 1),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "A", ["Op"] = "Gt", ["Message"] = "无右"
                        }
                    })).ConfigureAwait(false);
                return !both.Success && (both.Message ?? "").Contains("Right")
                       && !neither.Success && (neither.Message ?? "").Contains("Right");
            });

            Check("P1_Check解析_变量与字面量", () =>
            {
                if (!AssertCheckParser.TryParse("LeakageMa <= MaxLeakage", out var a, out _)) return false;
                if (a.Left != "LeakageMa" || a.Op != "Lte" || a.RightVar != "MaxLeakage") return false;
                if (!AssertCheckParser.TryParse("Margin > 0", out var b, out _)) return false;
                if (b.Left != "Margin" || b.Op != "Gt" || b.HasRightVar || Convert.ToDouble(b.RightLiteral) != 0) return false;
                if (!AssertCheckParser.TryParse("CylStatus == \"ARRIVED\"", out var c, out _)) return false;
                return c.Op == "Eq" && (string)c.RightLiteral == "ARRIVED";
            });

            Check("P2_Check拒绝非法算术", () =>
                !AssertCheckParser.TryParse("A + B > 0", out _, out _));

            Check("P3_Check边界与健壮性", () =>
            {
                // 无空格
                if (!AssertCheckParser.TryParse("A>=B", out var t, out _) || t.Op != "Gte" || t.RightVar != "B") return false;
                // 字面量须在右侧：拒绝 0 < A
                if (AssertCheckParser.TryParse("0 < A", out _, out _)) return false;
                // 拒绝 =>
                if (AssertCheckParser.TryParse("A => B", out _, out _)) return false;
                // 拒绝科学计数法
                if (AssertCheckParser.TryParse("A > 1e2", out _, out var e1) || e1 == null || !e1.Contains("科学")) return false;
                // 拒绝未知转义
                if (AssertCheckParser.TryParse("A == \"\\n\"", out _, out var e2) || e2 == null) return false;
                // 允许 \" 
                if (!AssertCheckParser.TryParse("A == \"x\\\"y\"", out var q, out _) || (string)q.RightLiteral != "x\"y") return false;
                // 拒绝中文标识（与提升规则一致）
                if (AssertCheckParser.TryParse("电阻 > 0", out _, out _)) return false;
                // 超长
                var longCheck = "A > " + new string('1', AssertCheckParser.MaxCheckLength);
                if (AssertCheckParser.TryParse(longCheck, out _, out _)) return false;
                // 无效小数
                if (AssertCheckParser.TryParse("A > 1.", out _, out _)) return false;
                return true;
            });

            await CheckAsync("F14_L1_Check端到端与互斥", async () =>
            {
                var ok = await RunAsync(MiniFlow(
                    Set("LeakageMa", 3.0),
                    Set("MaxLeakage", 5.0),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Check"] = "LeakageMa <= MaxLeakage",
                            ["Message"] = "超限"
                        }
                    })).ConfigureAwait(false);
                if (!ok.Success) return false;

                var dual = await RunAsync(MiniFlow(
                    Set("A", 1),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Check"] = "A > 0",
                            ["Left"] = "A",
                            ["Op"] = "Gt",
                            ["Right"] = 0,
                            ["Message"] = "双写"
                        }
                    })).ConfigureAwait(false);
                return !dual.Success && (dual.Message ?? "").Contains("Check");
            });

            await CheckAsync("F15_L0_Op符号别名", async () =>
            {
                var r = await RunAsync(MiniFlow(
                    Set("V", 3),
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Left"] = "V", ["Op"] = "<=", ["Right"] = 5, ["Message"] = "别名失败"
                        }
                    })).ConfigureAwait(false);
                return r.Success;
            });

            await CheckAsync("F16_空Assert不得误PASS", async () =>
            {
                var empty = await RunAsync(MiniFlow(
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object>()
                    })).ConfigureAwait(false);
                var msgOnly = await RunAsync(MiniFlow(
                    new Dictionary<string, object>
                    {
                        ["Type"] = "Action",
                        ["ActionKey"] = "Assert",
                        ["Args"] = new Dictionary<string, object> { ["Message"] = "只有消息" }
                    })).ConfigureAwait(false);
                return !empty.Success && (empty.Message ?? "").Contains("缺少判定")
                       && !msgOnly.Success && (msgOnly.Message ?? "").Contains("缺少判定");
            });

            Console.WriteLine(_fail == 0 ? "PROOF_ALL_PASS" : "PROOF_HAS_FAILURES=" + _fail);
            return _fail == 0 ? 0 : 1;
        }

        private static void Check(string name, Func<bool> body)
        {
            try
            {
                var ok = body();
                Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
                if (!ok) _fail++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + name + " EX: " + ex.Message);
                _fail++;
            }
        }

        private static async Task CheckAsync(string name, Func<Task<bool>> body)
        {
            try
            {
                var ok = await body().ConfigureAwait(false);
                Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
                if (!ok) _fail++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] " + name + " EX: " + ex.Message);
                _fail++;
            }
        }

        private static List<StepConfig> LoadFixture(string fileName)
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
            if (!File.Exists(fixture))
            {
                fixture = Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "Fixtures", fileName));
            }
            return ScenarioLoader.Load(fixture);
        }

        private static Dictionary<string, object> Set(string key, object value) =>
            new Dictionary<string, object>
            {
                ["Type"] = "Action",
                ["ActionKey"] = "SetVariable",
                ["Args"] = new Dictionary<string, object> { ["Key"] = key, ["Value"] = value }
            };

        private static Dictionary<string, object> AssertStruct(string left, string op, object right, string msg) =>
            new Dictionary<string, object>
            {
                ["Type"] = "Action",
                ["ActionKey"] = "Assert",
                ["Args"] = new Dictionary<string, object>
                {
                    ["Left"] = left,
                    ["Op"] = op,
                    ["Right"] = right,
                    ["Message"] = msg
                }
            };

        private static StepConfig MiniFlow(params Dictionary<string, object>[] nodes) =>
            new StepConfig
            {
                StepKey = "mini",
                StepName = "mini",
                Command = "DynamicFlow",
                Parameters = new Dictionary<string, object>
                {
                    ["Sequence"] = new List<object>(nodes)
                }
            };

        private static async Task<ExecutionResultBase> RunAsync(StepConfig step)
        {
            var registry = new WorkflowActionService(_ => { });
            new StandardActionsProvider().RegisterActions(registry);

            var services = new ServiceCollection();
            services.AddSingleton<IActionResolver>(registry);
            services.AddSingleton<IWorkflowEvaluator>(new WorkflowEvaluator());
            var sp = services.BuildServiceProvider();

            var ctx = new StepContext(
                step.StepKey,
                step,
                new Dictionary<string, IDevice>(),
                sp,
                CancellationToken.None,
                RunTestMode.Auto,
                new ContextVariableStore(),
                null,
                null,
                s => { });

            return await new DynamicFlowHandler().ExecuteAsync(step, ctx).ConfigureAwait(false);
        }
    }
}
