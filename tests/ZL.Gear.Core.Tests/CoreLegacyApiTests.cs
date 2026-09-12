using System.Collections.Generic;
using NUnit.Framework;
using ZL.Gear.Core.Models;
using ZL.Gear.Testing.Common;

namespace ZL.Gear.Core.Tests
{
    [TestFixture]
    public class CoreLegacyApiTests
    {
        [Test]
        public void Measurement_legacy五参数构造_保留Success与SamplesCollected()
        {
            var measurement = new Measurement("motor.current", 1.25, true, "OK", 3);

            Assert.AreEqual("motor.current", measurement.Key);
            Assert.AreEqual(1.25, measurement.Value);
            Assert.IsTrue(measurement.Success);
            Assert.AreEqual("OK", measurement.Message);
            Assert.AreEqual(3, measurement.SamplesCollected);
        }

        [Test]
        public void StepContext_ProductInfo_从GlobalContext读取Barcode()
        {
            var context = StepContextFactory.CreateLogicOnly(
                new StepConfig { StepKey = "LEGACY", Command = "Log" },
                globalContext: new Dictionary<string, object> { ["Barcode"] = "SN-001" });

            Assert.AreEqual("SN-001", context.ProductInfo);
        }
    }
}
