using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing;
using ZL.Gear.Sensing.Abstractions;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Sensing.Orchestration;
using ZL.Gear.Sensing.Samplers;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class DefaultMeasurementEngineTests
    {
        [Test]
        public async Task ExecuteAsync_触发后收集样本_完成时返回成功结果()
        {
            var engine = new DefaultMeasurementEngine<double>(_ => { });
            var config = new SamplingConfig<double>
            {
                TotalTimeoutMs = 1000,
                Trigger = new ImmediateTrigger<double>(),
                Strategy = new FixedCountStrategy<double>(3)
            };

            var dataStream = Observable.Interval(TimeSpan.FromMilliseconds(10))
                .Take(5)
                .Select(i => (double)i);

            var result = await engine.ExecuteAsync(dataStream, config, CancellationToken.None);

            Assert.AreEqual(ExeStatus.Completed, result.Status);
            Assert.IsTrue(result.SpecPassed);
            Assert.AreEqual(3, result.AllSamples.Count);
        }

        [Test]
        public async Task ExecuteAsync_超时_返回超时状态()
        {
            var engine = new DefaultMeasurementEngine<double>(_ => { });
            var config = new SamplingConfig<double>
            {
                TotalTimeoutMs = 120,
                Trigger = new ImmediateTrigger<double>(),
                Strategy = new FixedCountStrategy<double>(100)
            };

            // 慢速样本流：在总超时前无法凑齐目标数量，稳定触发 TimedOut
            var dataStream = Observable.Generate(
                0,
                i => true,
                i => i + 1,
                i => (double)i,
                i => TimeSpan.FromMilliseconds(30));

            var result = await engine.ExecuteAsync(dataStream, config, CancellationToken.None);

            Assert.AreEqual(ExeStatus.TimedOut, result.Status);
        }

        [Test]
        public async Task ExecuteAsync_取消_返回取消状态()
        {
            var engine = new DefaultMeasurementEngine<double>(_ => { });
            var config = new SamplingConfig<double>
            {
                TotalTimeoutMs = 10000,
                Trigger = new ImmediateTrigger<double>(),
                Strategy = new FixedCountStrategy<double>(10)
            };

            // 先快速发送一个样本，再持续发送，以便取消时已有样本
            var dataStream = Observable.Concat(
                Observable.Return(1.0),
                Observable.Generate(
                    0,
                    i => true,
                    i => i + 1,
                    i => (double)i,
                    i => TimeSpan.FromMilliseconds(5)));

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);

            var result = await engine.ExecuteAsync(dataStream, config, cts.Token);

            // 当前实现在取消且无样本时返回 Failed；有样本时也走 Failed 分支，不单独暴露 Cancelled 枚举
            Assert.AreEqual(ExeStatus.Failed, result.Status);
        }

        [Test]
        public async Task ExecuteAsync_空流_返回失败且无样本()
        {
            var engine = new DefaultMeasurementEngine<double>(_ => { });
            var config = new SamplingConfig<double>
            {
                TotalTimeoutMs = 100,
                Trigger = new ImmediateTrigger<double>(),
                Strategy = new FixedCountStrategy<double>(3)
            };

            // 空流：不产生任何样本
            var dataStream = Observable.Empty<double>();

            var result = await engine.ExecuteAsync(dataStream, config, CancellationToken.None);

            Assert.AreEqual(ExeStatus.Failed, result.Status);
            Assert.AreEqual(0, result.AllSamples.Count);
            Assert.That(result.Message, Does.Contain("未采集到任何有效样本"));
        }

        [Test]
        public async Task ExecuteAsync_取消后已有样本_保留已收集样本()
        {
            var engine = new DefaultMeasurementEngine<double>(_ => { });
            var config = new SamplingConfig<double>
            {
                TotalTimeoutMs = 10000,
                Trigger = new ImmediateTrigger<double>(),
                Strategy = new FixedCountStrategy<double>(10)
            };

            // 先发送 2 个样本，再以较慢速度持续发送，以便取消时样本不足
            var dataStream = Observable.Concat(
                Observable.Return(1.0),
                Observable.Return(2.0),
                Observable.Generate(
                    0,
                    i => true,
                    i => i + 1,
                    i => (double)i,
                    i => TimeSpan.FromMilliseconds(20)));

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(50);

            var result = await engine.ExecuteAsync(dataStream, config, cts.Token);

            // 当前实现在有样本时也走 Failed 分支
            Assert.AreEqual(ExeStatus.Failed, result.Status);
            Assert.GreaterOrEqual(result.AllSamples.Count, 2);
        }

        [Test]
        public async Task ExecuteAsync_Trigger不启动_不收集样本()
        {
            var engine = new DefaultMeasurementEngine<double>(_ => { });
            var config = new SamplingConfig<double>
            {
                TotalTimeoutMs = 100,
                Trigger = new ConditionalTrigger<double>(sample => false), // 永远不启动
                Strategy = new FixedCountStrategy<double>(3)
            };

            // 持续发送样本，但触发器永远不会启动采集
            var dataStream = Observable.Interval(TimeSpan.FromMilliseconds(10))
                .Take(5)
                .Select(i => (double)i);

            var result = await engine.ExecuteAsync(dataStream, config, CancellationToken.None);

            Assert.AreEqual(ExeStatus.Failed, result.Status);
            Assert.AreEqual(0, result.AllSamples.Count);
            Assert.That(result.Message, Does.Contain("未采集到任何有效样本"));
        }

    }
}
