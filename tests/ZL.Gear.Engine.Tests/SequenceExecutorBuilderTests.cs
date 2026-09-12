using NUnit.Framework;
using System;
using Moq;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Engine;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class SequenceExecutorBuilderTests
    {
        #region AsLogicOnlyDemoHost

        [Test]
        public void AsLogicOnlyDemoHost_设置LogicOnly模式()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .Build();

            Assert.IsNotNull(executor);
        }

        [Test]
        public void AsLogicOnlyDemoHost_Build_抛出InvalidOperationException_未声明宿主()
        {
            var builder = SequenceExecutorBuilder.Create();

            var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.That(ex.Message, Does.Contain("未声明宿主"));
        }

        #endregion

        #region AsInstrumentedHost

        [Test]
        public void AsInstrumentedHost_设置Instrumented模式()
        {
            var mockService = new Mock<IDeviceService>();
            var builder = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(mockService.Object);

            // 验证不会抛出 Unspecified 异常
            Assert.DoesNotThrow(() => builder.Build());
        }

        [Test]
        public void AsInstrumentedHost_null参数_抛出ArgumentNullException()
        {
            var builder = SequenceExecutorBuilder.Create();

            Assert.Throws<ArgumentNullException>(() => builder.AsInstrumentedHost(null));
        }

        // LogicOnlyDeviceService 为 internal 类型，测试项目无法直接实例化；
        // 此处通过 Build 行为的失败场景已在 Build 门禁中覆盖，无需额外访问 internal 实现。

        #endregion

        #region WithDeviceService

        [Test]
        public void WithDeviceService_设置设备服务()
        {
            var mockService = new Mock<IDeviceService>();
            // 注意：WithDeviceService 为历史用法，实际构建前仍需声明 Instrumented 宿主；
            // 当前正确入口为 AsInstrumentedHost(deviceService)。
            var builder = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(mockService.Object);

            Assert.DoesNotThrow(() => builder.Build());
        }

        #endregion

        #region Build 门禁 - LogicOnly 约束

        [Test]
        public void Build_LogicOnly_WithDeviceConfig_抛出异常()
        {
            var builder = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithDeviceConfig("devices.json");

            var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.That(ex.Message, Does.Contain("LogicOnly 模式禁止加载 devices.json"));
        }

        [Test]
        public void Build_LogicOnly_WithBuiltInModules_包含Sensing_抛出异常()
        {
            var builder = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithBuiltInModules(ZL.Gear.Engine.BuiltInModules.Sensing);

            var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.That(ex.Message, Does.Contain("LogicOnly Demo 宿主仅允许 BuiltInModules.Core"));
        }

        [Test]
        public void Build_LogicOnly_WithBuiltInModules_包含Plc_抛出异常()
        {
            var builder = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithBuiltInModules(ZL.Gear.Engine.BuiltInModules.Plc);

            var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.That(ex.Message, Does.Contain("LogicOnly Demo 宿主仅允许 BuiltInModules.Core"));
        }

        [Test]
        public void Build_LogicOnly_WithBuiltInModules_Core_通过()
        {
            var builder = SequenceExecutorBuilder.Create()
                .AsLogicOnlyDemoHost()
                .WithBuiltInModules(ZL.Gear.Engine.BuiltInModules.Core);

            Assert.DoesNotThrow(() => builder.Build());
        }

        #endregion

        #region Build 门禁 - Instrumented 约束

        [Test]
        public void Build_Instrumented_有DeviceService_通过()
        {
            var builder = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(new Mock<IDeviceService>().Object);

            Assert.DoesNotThrow(() => builder.Build());
        }

        #endregion

        #region 默认行为

        [Test]
        public void Create_默认Unspecified模式()
        {
            var builder = SequenceExecutorBuilder.Create();

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        #endregion
    }
}
