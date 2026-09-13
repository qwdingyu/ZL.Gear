using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Models;
using ZL.Gear.Engine.Planning;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class ConditionExpressionCollectorTests
    {
        #region Step.Parameters 条件收集

        [Test]
        public void CollectFromSteps_StepParameters含Condition_收集成功()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "X",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Condition"] = "x > 0"
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].StepKey, Is.EqualTo("S1"));
            Assert.That(results[0].Expression, Is.EqualTo("x > 0"));
            Assert.That(results[0].IsWaitUntilCondition, Is.False);
        }

        [Test]
        public void CollectFromSteps_多个Condition_全部收集()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "X",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Condition"] = "a == 1",
                        ["Args"] = new Dictionary<string, object>
                        {
                            ["Condition"] = "b == 2"
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Select(r => r.Expression), Does.Contain("a == 1"));
            Assert.That(results.Select(r => r.Expression), Does.Contain("b == 2"));
        }

        #endregion

        #region WorkflowDefinition 内嵌条件收集

        [Test]
        public void CollectFromSteps_WorkflowDefinition含Condition_收集成功()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Sequence"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "Action",
                                    ["Condition"] = "Ready == true"
                                }
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].StepKey, Is.EqualTo("S1"));
            Assert.That(results[0].Expression, Is.EqualTo("Ready == true"));
            Assert.That(results[0].IsWaitUntilCondition, Is.False);
        }

        #endregion

        #region WaitUntil 条件标记

        [Test]
        public void CollectFromSteps_WaitUntil节点Condition_标记为执行期条件()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Sequence"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "WaitUntil",
                                    ["Condition"] = "Sensor.Value > 10"
                                }
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].StepKey, Is.EqualTo("S1"));
            Assert.That(results[0].Expression, Is.EqualTo("Sensor.Value > 10"));
            Assert.That(results[0].IsWaitUntilCondition, Is.True);
        }

        [Test]
        public void CollectFromSteps_非WaitUntil节点Condition_不标记为执行期条件()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Sequence"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "Action",
                                    ["Condition"] = "x > 0"
                                },
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "Measure",
                                    ["Condition"] = "y < 100"
                                }
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.All(r => r.IsWaitUntilCondition == false), Is.True);
        }

        [Test]
        public void CollectFromSteps_WaitUntil嵌套在Sequence中_正确标记()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new Dictionary<string, object>
                        {
                            ["Sequence"] = new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    ["Type"] = "Sequence",
                                    ["Children"] = new List<object>
                                    {
                                        new Dictionary<string, object>
                                        {
                                            ["Type"] = "WaitUntil",
                                            ["Condition"] = "Ready == true"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].IsWaitUntilCondition, Is.True);
        }

        #endregion

        #region 边界情况

        [Test]
        public void CollectFromSteps_空步骤列表_返回空()
        {
            var results = ConditionExpressionCollector.CollectFromSteps(new List<StepConfig>());
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void CollectFromSteps_Null根节点_返回空()
        {
            var results = ConditionExpressionCollector.CollectFromSteps(null);
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void CollectFromSteps_禁用步骤_跳过()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "X",
                    Enable = false,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Condition"] = "x > 0"
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void CollectFromSteps_空Condition字符串_跳过()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "X",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["Condition"] = "   "
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void CollectFromSteps_SubSteps递归收集()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "Parent",
                    Command = "Group",
                    Enable = true,
                    SubSteps = new List<StepConfig>
                    {
                        new StepConfig
                        {
                            StepKey = "Child1",
                            Command = "X",
                            Enable = true,
                            Parameters = new Dictionary<string, object>
                            {
                                ["Condition"] = "a == 1"
                            }
                        },
                        new StepConfig
                        {
                            StepKey = "Child2",
                            Command = "Y",
                            Enable = true,
                            Parameters = new Dictionary<string, object>
                            {
                                ["Condition"] = "b == 2"
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Select(r => r.StepKey), Does.Contain("Child1"));
            Assert.That(results.Select(r => r.StepKey), Does.Contain("Child2"));
        }

        #endregion

        #region JObject 支持

        [Test]
        public void CollectFromSteps_JObjectWorkflowDefinition_正确收集()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new JObject
                        {
                            ["Sequence"] = new JArray
                            {
                                new JObject
                                {
                                    ["Type"] = new JValue("Action"),
                                    ["Condition"] = new JValue("Ready == true")
                                }
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Expression, Is.EqualTo("Ready == true"));
            Assert.That(results[0].IsWaitUntilCondition, Is.False);
        }

        [Test]
        public void CollectFromSteps_JObjectWaitUntil_正确标记()
        {
            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "DynamicFlow",
                    Enable = true,
                    Parameters = new Dictionary<string, object>
                    {
                        ["WorkflowDefinition"] = new JObject
                        {
                            ["Sequence"] = new JArray
                            {
                                new JObject
                                {
                                    ["Type"] = new JValue("WaitUntil"),
                                    ["Condition"] = new JValue("Sensor.Value > 10")
                                }
                            }
                        }
                    }
                }
            };

            var results = ConditionExpressionCollector.CollectFromSteps(steps).ToList();

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].IsWaitUntilCondition, Is.True);
        }

        #endregion
    }
}
