using NUnit.Framework;
using System;
using System.Collections.Generic;
using ZL.Gear.Sensing;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class SamplingStrategiesTests
    {
        [Test]
        public void FixedCountStrategy_收集足够样本_返回完成()
        {
            var strategy = new FixedCountStrategy<double>(3);
            var samples = new List<double>();

            bool isDone = false;
            bool shouldCollect = false;
            int sampleIndex = 0;
            while (!isDone && sampleIndex < 5)
            {
                sampleIndex++;
                (isDone, shouldCollect) = strategy.ProcessSample(sampleIndex + 1.0, samples);
                if (shouldCollect) samples.Add(sampleIndex + 1.0);
            }

            Assert.IsTrue(isDone);
            Assert.AreEqual(3, samples.Count);
            Assert.AreEqual(3.0, strategy.CalculateResult(samples), 0.001);
        }

        [Test]
        public void DurationStrategy_由计算器决定结果()
        {
            var strategy = new DurationStrategy<double>(TimeSpan.FromSeconds(1), new MaxCalculator<double>());
            var samples = new List<double> { 1.0, 5.0, 3.0 };

            var result = strategy.CalculateResult(samples);
            Assert.AreEqual(5.0, result, 0.001);
        }
    }
}
