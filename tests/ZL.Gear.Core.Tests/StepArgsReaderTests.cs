using NUnit.Framework;
using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Core.Tests
{
    [TestFixture]
    public class StepArgsReaderTests
    {
        private StepConfig _step;
        private StepContext _context;
        private ContextVariableStore _variables;

        [SetUp]
        public void Setup()
        {
            _step = new StepConfig
            {
                StepKey = "TEST-001",
                Command = "ApplyRecipe",
                Parameters = new Dictionary<string, object>
                {
                    { "RecipeId", "R001" },
                    { "LimitOhm", 100.0 },
                    { "SimulatedOhm", 50.0 }
                }
            };

            _variables = new ContextVariableStore();
            _variables.SetShared("RecipeId", "R-VAR-001");
            _variables.SetShared("LimitOhm", 200.0);

            var globalContext = new Dictionary<string, object>
            {
                { "Barcode", "BAR-001" },
                { "Model", "MODEL-A" }
            };

            _context = StepContextFactory.CreateLogicOnly(_step, _variables, globalContext);
        }

        #region ArgsOnly

        [Test]
        public void TryRequireString_ArgsOnly_存在时返回成功()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("RecipeId", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("R001", value);
            Assert.IsNull(error);
        }

        [Test]
        public void TryRequireString_ArgsOnly_不存在时返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("NonExist", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("缺少必填 Args 参数 'NonExist'"));
        }

        [Test]
        public void TryRequirePositiveDouble_ArgsOnly_正数时返回成功()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequirePositiveDouble("LimitOhm", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual(100.0, value);
            Assert.IsNull(error);
        }

        [Test]
        public void TryRequirePositiveDouble_ArgsOnly_负数时返回失败()
        {
            _step.Parameters["LimitOhm"] = -10.0;
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequirePositiveDouble("LimitOhm", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("须为正数"));
        }

        [Test]
        public void TryRequirePositiveDouble_ArgsOnly_零时返回失败()
        {
            _step.Parameters["LimitOhm"] = 0.0;
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequirePositiveDouble("LimitOhm", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("须为正数"));
        }

        #endregion

        #region ArgsThenVariables

        [Test]
        public void TryRequireString_ArgsThenVariables_Args存在时取Args()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("RecipeId", StepArgSource.ArgsThenVariables, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("R001", value); // Args 优先
        }

        [Test]
        public void TryRequireString_ArgsThenVariables_Args不存在时取Variables()
        {
            _step.Parameters.Remove("RecipeId");
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("RecipeId", StepArgSource.ArgsThenVariables, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("R-VAR-001", value);
            Assert.IsNull(error);
        }

        [Test]
        public void TryRequireString_ArgsThenVariables_Args值为null时回退Variables()
        {
            _step.Parameters["RecipeId"] = null;
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("RecipeId", StepArgSource.ArgsThenVariables, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("R-VAR-001", value);
            Assert.IsNull(error);
        }

        [Test]
        public void TryRequireString_ArgsThenVariables_都不存在时返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("NonExist", StepArgSource.ArgsThenVariables, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("未找到参数或变量 'NonExist'"));
        }

        #endregion

        #region All

        [Test]
        public void TryGetString_All_能读取Global()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("Barcode", StepArgSource.All, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("BAR-001", value);
        }

        [Test]
        public void TryGetString_All_requireArgKey时缺失返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("NonExist", StepArgSource.All, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("未找到参数或变量 'NonExist'"));
        }

        #endregion

        #region VariablesOnly

        [Test]
        public void TryRequireString_VariablesOnly_读取流程变量()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("RecipeId", StepArgSource.VariablesOnly, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("R-VAR-001", value); // Variables 中的值
        }

        [Test]
        public void TryRequireString_VariablesOnly_不存在时返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("NonExist", StepArgSource.VariablesOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("未找到流程变量 'NonExist'"));
        }

        #endregion

        #region GlobalOnly

        [Test]
        public void TryRequireString_GlobalOnly_读取全局变量()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("Barcode", StepArgSource.GlobalOnly, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual("BAR-001", value);
        }

        [Test]
        public void TryRequireString_GlobalOnly_不存在时返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("NonExist", StepArgSource.GlobalOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("未找到 GlobalContext 键 'NonExist'"));
        }

        #endregion

        #region SetShared / GetFlowString / TryRequireFlowString

        [Test]
        public void TryRequireFlowString_流程变量存在_返回成功()
        {
            var reader = StepArgsReader.From(_step, _context, "MarkComplete");
            reader.SetShared("RecipeId", "FLOW-RECIPE-01");

            var ok = reader.TryRequireFlowString("RecipeId", out var value, out var error);

            Assert.IsTrue(ok);
            Assert.AreEqual("FLOW-RECIPE-01", value);
            Assert.IsNull(error);
        }

        [Test]
        public void TryRequireFlowString_流程变量缺失_返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "MarkComplete");

            var ok = reader.TryRequireFlowString("PriorStepOutput", out var value, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("PriorStepOutput"));
        }

        [Test]
        public void SetShared_写入流程变量_后继可读()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            reader.SetShared("OutputKey", "VALUE-123");

            var value = reader.GetFlowString("OutputKey");
            Assert.AreEqual("VALUE-123", value);
        }

        [Test]
        public void GetFlowString_不存在时返回默认值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetFlowString("NonExist", "DEFAULT");

            Assert.AreEqual("DEFAULT", value);
        }

        #endregion

        #region GetGlobalString

        [Test]
        public void GetGlobalString_读取全局条码()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetGlobalString("Barcode");

            Assert.AreEqual("BAR-001", value);
        }

        [Test]
        public void GetGlobalString_不存在时返回默认值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetGlobalString("NonExist", "DEFAULT");

            Assert.AreEqual("DEFAULT", value);
        }

        #endregion

        #region GetOptionalDouble / GetOptionalString

        [Test]
        public void GetOptionalDouble_存在时返回值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetOptionalDouble("LimitOhm", 0.0);

            Assert.AreEqual(100.0, value);
        }

        [Test]
        public void GetOptionalDouble_不存在时返回默认值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetOptionalDouble("NonExist", 0.0);

            Assert.AreEqual(0.0, value);
        }

        [Test]
        public void GetOptionalString_存在时返回值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetOptionalString("RecipeId", "DEFAULT");

            Assert.AreEqual("R001", value);
        }

        #endregion

        #region TryGetDouble

        [Test]
        public void TryGetDouble_能解析整数()
        {
            _step.Parameters["IntValue"] = 42;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("IntValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsTrue(result);
            Assert.AreEqual(42.0, value);
        }

        [Test]
        public void TryGetDouble_无法解析时返回失败()
        {
            _step.Parameters["BadValue"] = "not-a-number";
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("BadValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("无法解析为数值"));
        }

        [Test]
        public void TryGetDouble_NaN时返回失败()
        {
            _step.Parameters["NanValue"] = double.NaN;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("NanValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("数值非法"));
        }

        [Test]
        public void TryGetDouble_PositiveInfinity时返回失败()
        {
            _step.Parameters["InfValue"] = double.PositiveInfinity;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("InfValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("数值非法"));
        }

        #endregion

        #region 边界与空值（补充）

        [Test]
        public void TryRequireString_键为null_返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString(null, StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("键名为空"));
        }

        [Test]
        public void TryRequireString_键为空字符串_返回失败()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("键名为空"));
        }

        [Test]
        public void TryGetDouble_值为null_返回失败()
        {
            _step.Parameters["NullValue"] = null;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("NullValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("无法解析为数值"));
        }

        [Test]
        public void TryRequirePositiveDouble_值为零_返回失败()
        {
            _step.Parameters["ZeroValue"] = 0.0;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryRequirePositiveDouble("ZeroValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("须为正数"));
        }

        [Test]
        public void TryRequirePositiveDouble_值为负零_返回失败()
        {
            _step.Parameters["NegZero"] = -0.0;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryRequirePositiveDouble("NegZero", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("须为正数"));
        }

        [Test]
        public void GetOptionalDouble_非法字符串_返回默认值()
        {
            _step.Parameters["BadDouble"] = "not-a-number";
            var reader = StepArgsReader.From(_step, _context, "Test");
            var value = reader.GetOptionalDouble("BadDouble", 99.9);

            Assert.AreEqual(99.9, value);
        }

        [Test]
        public void GetFlowString_键为null_返回默认值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetFlowString(null, "DEFAULT");

            Assert.AreEqual("DEFAULT", value);
        }

        [Test]
        public void SetShared_跨实例可读()
        {
            var reader1 = StepArgsReader.From(_step, _context, "Step1");
            reader1.SetShared("CrossKey", "CROSS-VALUE");

            var reader2 = StepArgsReader.From(_step, _context, "Step2");
            var ok = reader2.TryRequireFlowString("CrossKey", out var value, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual("CROSS-VALUE", value);
        }

        [Test]
        public void GetGlobalString_GlobalContext为null_返回默认值()
        {
            var nullGlobalContext = StepContextFactory.CreateLogicOnly(_step, _variables, globalContext: null);
            var reader = StepArgsReader.From(_step, nullGlobalContext, "ApplyRecipe");
            var value = reader.GetGlobalString("Barcode", "DEFAULT");

            Assert.AreEqual("DEFAULT", value);
        }

        [Test]
        public void TryRequireString_GlobalOnly_GlobalContext为null_返回失败()
        {
            var nullGlobalContext = StepContextFactory.CreateLogicOnly(_step, _variables, globalContext: null);
            var reader = StepArgsReader.From(_step, nullGlobalContext, "ApplyRecipe");
            var result = reader.TryRequireString("Barcode", StepArgSource.GlobalOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("未找到 GlobalContext 键 'Barcode'"));
        }

        [Test]
        public void TryRequireString_ArgsOnly_键存在但值为空字符串_返回失败()
        {
            _step.Parameters["EmptyValue"] = "";
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("EmptyValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("不能为空"));
        }

        [Test]
        public void TryRequireString_ArgsOnly_键存在但值为null_返回失败()
        {
            _step.Parameters["NullValue"] = null;
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("NullValue", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("不能为空"));
        }

        [Test]
        public void TryRequireString_ArgsThenVariables_Args值为空字符串_不回退Variables()
        {
            _step.Parameters["RecipeId"] = "";
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var result = reader.TryRequireString("RecipeId", StepArgSource.ArgsThenVariables, out var value, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(value);
            Assert.That(error, Does.Contain("不能为空"));
        }

        [Test]
        public void TryGetDouble_NegativeInfinity_返回失败()
        {
            _step.Parameters["NegInf"] = double.NegativeInfinity;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("NegInf", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("数值非法"));
        }

        [Test]
        public void TryRequirePositiveDouble_NegativeInfinity_返回失败()
        {
            _step.Parameters["NegInf"] = double.NegativeInfinity;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryRequirePositiveDouble("NegInf", StepArgSource.ArgsOnly, out var value, out var error);

            Assert.IsFalse(result);
            Assert.That(error, Does.Contain("数值非法"));
        }

        [Test]
        public void GetOptionalString_不存在时返回默认值()
        {
            var reader = StepArgsReader.From(_step, _context, "ApplyRecipe");
            var value = reader.GetOptionalString("NonExist", "DEFAULT");

            Assert.AreEqual("DEFAULT", value);
        }

        #endregion

        #region Fail

        [Test]
        public void Fail_返回失败结果()
        {
            var result = StepArgsReader.Fail("测试失败");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("测试失败", result.Message);
        }

        #endregion

        #region 类型转换

        [Test]
        public void TryToDouble_支持多种数值类型()
        {
            // 这些测试通过 TryGetDouble 间接覆盖
            _step.Parameters["FloatValue"] = 3.14f;
            var reader = StepArgsReader.From(_step, _context, "Test");
            var result = reader.TryGetDouble("FloatValue", StepArgSource.ArgsOnly, out var value, out _);

            Assert.IsTrue(result);
            Assert.AreEqual(3.14, value, 0.01);
        }

        #endregion
    }
}
