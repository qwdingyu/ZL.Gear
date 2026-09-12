using NUnit.Framework;
using System;
using System.Collections.Generic;
using ZL.Gear.Sensing;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class SamplingTriggersTests
    {
        [Test]
        public void ImmediateTrigger_始终触发()
        {
            var trigger = new ImmediateTrigger<double>();
            Assert.IsTrue(trigger.ShouldStart(1.0, false));
            Assert.IsFalse(trigger.ShouldStop(1.0, true));
        }

        [Test]
        public void ConditionalTrigger_条件满足时触发()
        {
            var trigger = new ConditionalTrigger<double>(v => v > 5);
            Assert.IsFalse(trigger.ShouldStart(3.0, false));
            Assert.IsTrue(trigger.ShouldStart(6.0, false));
        }
    }
}
