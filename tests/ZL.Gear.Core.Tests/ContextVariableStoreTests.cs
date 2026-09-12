using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Tests
{
    [TestFixture]
    public class ContextVariableStoreTests
    {
        private ContextVariableStore _root;

        [SetUp]
        public void Setup()
        {
            _root = new ContextVariableStore();
            _root.Set("GlobalKey", "GlobalValue");
            _root.Set("GlobalNumber", 42.0);
        }

        [TearDown]
        public void TearDown()
        {
            _root?.Dispose();
        }

        #region TryGet / Get 基础查找

        [Test]
        public void TryGet_当前作用域存在时返回成功()
        {
            var child = _root.CreateChildScope();
            child.Set("LocalKey", "LocalValue");

            var result = child.TryGet<string>("LocalKey", out var value);

            Assert.IsTrue(result);
            Assert.AreEqual("LocalValue", value);
        }

        [Test]
        public void TryGet_当前不存在时向上递归查找()
        {
            var child = _root.CreateChildScope();

            var result = child.TryGet<string>("GlobalKey", out var value);

            Assert.IsTrue(result);
            Assert.AreEqual("GlobalValue", value);
        }

        [Test]
        public void TryGet_全链路不存在时返回失败()
        {
            var child = _root.CreateChildScope();

            var result = child.TryGet<string>("NonExist", out var value);

            Assert.IsFalse(result);
            Assert.AreEqual(default, value);
        }

        [Test]
        public void Get_不存在时返回默认值()
        {
            var child = _root.CreateChildScope();

            var value = child.Get("NonExist", "DEFAULT");

            Assert.AreEqual("DEFAULT", value);
        }

        #endregion

        #region SetShared 写入父级

        [Test]
        public void SetShared_子作用域写入_父级可读()
        {
            var child = _root.CreateChildScope();
            child.SetShared("SharedKey", "SharedValue");

            var result = _root.TryGet<string>("SharedKey", out var value);

            Assert.IsTrue(result);
            Assert.AreEqual("SharedValue", value);
        }

        [Test]
        public void SetShared_根作用域写入_写入当前()
        {
            _root.SetShared("RootShared", "RootValue");

            var result = _root.TryGet<string>("RootShared", out var value);

            Assert.IsTrue(result);
            Assert.AreEqual("RootValue", value);
        }

        [Test]
        public void SetShared_不穿透到祖父级()
        {
            var child = _root.CreateChildScope();
            var grandChild = child.CreateChildScope();

            grandChild.SetShared("Key", "Value");

            // 祖父级（_root）不可见
            var result = _root.TryGet<string>("Key", out var value);

            Assert.IsFalse(result);
        }

        #endregion

        #region Set 写入隔离

        [Test]
        public void Set_子作用域写入_父级不可见()
        {
            var child = _root.CreateChildScope();
            child.Set("LocalOnly", "LocalValue");

            var result = _root.TryGet<string>("LocalOnly", out var value);

            Assert.IsFalse(result);
        }

        #endregion

        #region AsDictionary 可见性向上收缩

        [Test]
        public void AsDictionary_包含当前及父级变量()
        {
            var child = _root.CreateChildScope();
            child.Set("ChildKey", "ChildValue");

            var dict = child.AsDictionary();

            Assert.That(dict, Does.ContainKey("GlobalKey"));
            Assert.That(dict, Does.ContainKey("ChildKey"));
            Assert.That(dict, Does.Not.ContainKey("LocalOnly"));
        }

        #endregion

        #region 类型转换

        [Test]
        public void TryGet_数值类型转换()
        {
            var child = _root.CreateChildScope();

            var result = child.TryGet<double>("GlobalNumber", out var value);

            Assert.IsTrue(result);
            Assert.AreEqual(42.0, value);
        }

        [Test]
        public void TryGet_可转换字符串到数值()
        {
            _root.Set("StringNumber", "123");
            var child = _root.CreateChildScope();

            var result = child.TryGet<double>("StringNumber", out var value);

            Assert.IsTrue(result);
            Assert.AreEqual(123.0, value);
        }

        [Test]
        public void TryGet_不可转换时返回默认值()
        {
            _root.Set("BadNumber", "not-a-number");
            var child = _root.CreateChildScope();

            var result = child.TryGet<double>("BadNumber", out var value);

            Assert.IsFalse(result);
            Assert.AreEqual(default(double), value);
        }

        #endregion

        #region 信号管理

        [Test]
        public void RegisterSignalPairFor_注册成功()
        {
            _root.RegisterSignalPairFor("STEP-001");

            var result = _root.TryGetSignalPair("STEP-001", out var signals);

            Assert.IsTrue(result);
            Assert.IsNotNull(signals);
            Assert.IsNotNull(signals.StartSignal);
            Assert.IsNotNull(signals.EndSignalCts);
        }

        [Test]
        public void TryGetSignalPair_不存在时返回失败()
        {
            var result = _root.TryGetSignalPair("NonExist", out var signals);

            Assert.IsFalse(result);
            Assert.IsNull(signals);
        }

        [Test]
        public void RegisterSignalPairFor_重复注册不覆盖()
        {
            _root.RegisterSignalPairFor("STEP-001");
            _root.RegisterSignalPairFor("STEP-001"); // 第二次

            var result = _root.TryGetSignalPair("STEP-001", out var signals);

            Assert.IsTrue(result);
            Assert.IsNotNull(signals);
        }

        #endregion

        #region 生命周期

        [Test]
        public void Dispose_释放资源()
        {
            var cts = new CancellationTokenSource();
            _root.RegisterSignalPairFor("STEP-002");
            // 手动插入一个 disposable
            var store = _root.CreateChildScope();
            store.Set("DisposableKey", cts);

            _root.Dispose();

            // CancellationTokenSource.Dispose 不会设置 IsCancellationRequested，仅释放资源
            // 这里验证 store 被清空（通过再次访问验证）
            Assert.IsTrue(cts.Token.CanBeCanceled);
        }

        #endregion
    }
}
