using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ZL.Gear.Sensing;

namespace ZL.Gear.Sensing.Tests
{
    [TestFixture]
    public class SampleEventsTests
    {
        [TearDown]
        public void TearDown() => SampleEvents.SessionRequesters.Clear();

        [Test]
        public async Task RequestSessionAsync_WhenRegistered_ReturnsSession()
        {
            SampleEvents.SessionRequesters["Noise"] = () =>
                Task.FromResult<object>(new StubSession(55.5));

            var session = await SampleEvents.RequestSessionAsync<double>("Noise");
            session.Dispose();

            Assert.That(session.Result.Average, Is.EqualTo(55.5));
            Assert.That(session.Result.Success, Is.True);
        }

        [Test]
        public void RequestSessionAsync_WhenMissing_ThrowsKeyNotFound()
        {
            var task = SampleEvents.RequestSessionAsync<double>("Missing");
            var ex = Assert.ThrowsAsync<KeyNotFoundException>(async () => await task);
            Assert.That(ex!.Message, Does.Contain("Missing"));
        }

        private sealed class StubSession : ISamplingSession<double>
        {
            private readonly double _average;

            public StubSession(double average)
            {
                _average = average;
                Result = new SamplerStatisticsResult<double> { Channel = "Noise", Success = true, Average = average };
            }

            public SamplerStatisticsResult<double> Result { get; }

            public void Dispose() { }
        }
    }
}
