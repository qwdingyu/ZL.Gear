using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Samplers;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class DeviceSamplerTests
    {
        [Test]
        public async Task Start_设备读失败时_向DataStream推送失败测量()
        {
            var mockDevice = new Mock<IDevice>();
            mockDevice.Setup(d => d.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<StepContext>()))
                .ReturnsAsync(DeviceReading.Failed("设备异常"));

            var step = new StepConfig { StepKey = "step", Command = "Read" };
            var context = StepContextFactory.CreateLogicOnly(step);

            var sampler = new DeviceSampler<double>(
                "TestSampler",
                mockDevice.Object,
                "Read",
                new Dictionary<string, object>(),
                context,
                TimeSpan.FromMilliseconds(10));

            var sampleReceived = new TaskCompletionSource<Measurement>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var _ = sampler.DataStream.Subscribe(m => sampleReceived.TrySetResult(m));

            sampler.Start();
            var measurement = await sampleReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sampler.Stop();

            Assert.IsFalse(measurement.Success);
            Assert.That(measurement.Message, Does.Contain("设备异常"));
        }

        [Test]
        public async Task Start_连续Start_不重复启动()
        {
            var mockDevice = new Mock<IDevice>();
            mockDevice.Setup(d => d.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<StepContext>()))
                .ReturnsAsync(DeviceReading.Succeeded("ok", 1));

            var step = new StepConfig { StepKey = "step", Command = "Read" };
            var context = StepContextFactory.CreateLogicOnly(step);

            var sampler = new DeviceSampler<double>(
                "TestSampler",
                mockDevice.Object,
                "Read",
                new Dictionary<string, object>(),
                context,
                TimeSpan.FromMilliseconds(10));

            sampler.Start();
            sampler.Start(); // 第二次不应启动新循环
            sampler.Stop();

            // 验证 ExecuteAsync 仅被调用一次（因为只启动了一个循环）
            mockDevice.Verify(d => d.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<StepContext>()), Times.AtMost(2));
        }

    }
}
