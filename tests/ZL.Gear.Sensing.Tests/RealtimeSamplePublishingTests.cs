using System.Collections.Generic;
using NUnit.Framework;
using ZL.Gear.Sensing;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class RealtimeSamplePublishingTests
    {
        [Test]
        public void Sink_can_be_replaced_without_TestEvents_in_test_scope()
        {
            var captured = new List<(string Step, string Channel, object Value)>();
            var previous = RealtimeSamplePublishing.Sink;
            try
            {
                RealtimeSamplePublishing.Sink = new CaptureSink(captured);
                RealtimeSamplePublishing.Sink.Publish("step-1", "noise_1", 42.5);
                Assert.That(captured.Count, Is.EqualTo(1));
                Assert.That(captured[0].Step, Is.EqualTo("step-1"));
                Assert.That(captured[0].Channel, Is.EqualTo("noise_1"));
                Assert.That(captured[0].Value, Is.EqualTo(42.5));
            }
            finally
            {
                RealtimeSamplePublishing.Sink = previous;
            }
        }

        private sealed class CaptureSink : IRealtimeSampleSink
        {
            private readonly List<(string, string, object)> _captured;

            public CaptureSink(List<(string, string, object)> captured) => _captured = captured;

            public void Publish(string stepKey, string channel, object value) =>
                _captured.Add((stepKey, channel, value));
        }
    }
}
