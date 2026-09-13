using NUnit.Framework;
using System.Collections.Generic;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Tests
{
    [TestFixture]
    public class WorkflowEvaluatorTests
    {
        private WorkflowEvaluator _evaluator;

        [SetUp]
        public void Setup()
        {
            _evaluator = new WorkflowEvaluator();
        }

        #region TryValidateConditionSyntax - 基础行为

        [Test]
        public void TryValidateConditionSyntax_空表达式_返回成功()
        {
            var result = _evaluator.TryValidateConditionSyntax(null, null, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_空白表达式_返回成功()
        {
            var result = _evaluator.TryValidateConditionSyntax("   ", null, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_合法字面量_返回成功()
        {
            var result = _evaluator.TryValidateConditionSyntax("true", null, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_语法错误_返回失败并携带错误信息()
        {
            var result = _evaluator.TryValidateConditionSyntax("1 + + 2", null, out var error);
            Assert.IsFalse(result);
            Assert.IsNotNull(error);
            Assert.That(error, Is.Not.Empty);
        }

        #endregion

        #region TryValidateConditionSyntax - 变量表行为（DynamicExpresso 核心契约）

        [Test]
        public void TryValidateConditionSyntax_引用未定义变量_返回失败()
        {
            // DynamicExpresso.Parse 对未定义变量会报 Unknown identifier
            var result = _evaluator.TryValidateConditionSyntax("Ready == true", null, out var error);
            Assert.IsFalse(result);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("Ready"));
        }

        [Test]
        public void TryValidateConditionSyntax_提供空变量表_引用未定义变量仍失败()
        {
            var vars = new Dictionary<string, object>();
            var result = _evaluator.TryValidateConditionSyntax("ChannelReady == true", vars, out var error);
            Assert.IsFalse(result);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("ChannelReady"));
        }

        [Test]
        public void TryValidateConditionSyntax_提供变量表_引用已定义变量_返回成功()
        {
            var vars = new Dictionary<string, object>
            {
                ["Ready"] = true,
                ["ChannelReady"] = false
            };

            var result = _evaluator.TryValidateConditionSyntax("Ready == true && ChannelReady == false", vars, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_变量值为Null_仍视为已定义_返回成功()
        {
            // 变量表中存在键但值为 null，DynamicExpresso 将其提升为 object(null)，语法上合法
            var vars = new Dictionary<string, object>
            {
                ["Signal"] = null
            };

            var result = _evaluator.TryValidateConditionSyntax("Signal == null", vars, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_变量名为非法标识符_不提升为变量()
        {
            // "123abc" 不是合法 C# 标识符，DynamicExpresso 不会将其提升为变量
            var vars = new Dictionary<string, object>
            {
                ["123abc"] = 1
            };

            // 使用合法变量名配合，确保表达式本身合法
            var result = _evaluator.TryValidateConditionSyntax("true == true", vars, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        #endregion

        #region TryValidateConditionSyntax - 复杂表达式

        [Test]
        public void TryValidateConditionSyntax_数学表达式_返回成功()
        {
            var vars = new Dictionary<string, object>
            {
                ["x"] = 10,
                ["y"] = 20
            };

            var result = _evaluator.TryValidateConditionSyntax("x + y > 25", vars, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_字符串比较_返回成功()
        {
            var vars = new Dictionary<string, object>
            {
                ["Status"] = "OK"
            };

            var result = _evaluator.TryValidateConditionSyntax("Status == \"OK\"", vars, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        [Test]
        public void TryValidateConditionSyntax_方法调用_返回成功()
        {
            // DynamicExpresso 默认引用了 Math/Convert/TimeSpan，可直接使用
            var result = _evaluator.TryValidateConditionSyntax("Math.Max(1, 2) > 0", null, out var error);
            Assert.IsTrue(result);
            Assert.IsNull(error);
        }

        #endregion
    }
}
