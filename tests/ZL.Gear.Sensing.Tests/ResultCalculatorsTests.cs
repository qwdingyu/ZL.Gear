using NUnit.Framework;
using System;
using System.Collections.Generic;
using ZL.Gear.Sensing;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class ResultCalculatorsTests
    {
        [Test]
        public void AverageCalculator_计算平均值()
        {
            var calculator = new AverageCalculator<double>();
            var samples = new List<double> { 1.0, 2.0, 3.0 };
            Assert.AreEqual(2.0, calculator.Calculate(samples), 0.001);
        }

        [Test]
        public void MaxCalculator_返回最大值()
        {
            var calculator = new MaxCalculator<double>();
            var samples = new List<double> { 1.0, 5.0, 3.0 };
            Assert.AreEqual(5.0, calculator.Calculate(samples));
        }

        [Test]
        public void MinCalculator_返回最小值()
        {
            var calculator = new MinCalculator<double>();
            var samples = new List<double> { 1.0, 5.0, 3.0 };
            Assert.AreEqual(1.0, calculator.Calculate(samples));
        }

        [Test]
        public void LastValueCalculator_返回最后一个值()
        {
            var calculator = new LastValueCalculator<double>();
            var samples = new List<double> { 1.0, 5.0, 3.0 };
            Assert.AreEqual(3.0, calculator.Calculate(samples));
        }

        [Test]
        public void MedianCalculator_返回中位数()
        {
            var calculator = new MedianCalculator<double>();
            var samples = new List<double> { 1.0, 2.0, 3.0, 4.0 };
            Assert.AreEqual(2.0, calculator.Calculate(samples));
        }

        [Test]
        public void StdDevCalculator_返回标准差()
        {
            var calculator = new StdDevCalculator<double>();
            var samples = new List<double> { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
            Assert.AreEqual(2.13809, calculator.Calculate(samples), 0.001);
        }
    }
}
