using ZL.Gear.Core.Events;

namespace ZL.Gear.Sensing
{
    /// <summary>宿主可替换 Sink；默认双发 legacy TestEvents（Phase B 过渡）。</summary>
    public static class RealtimeSamplePublishing
    {
        public static IRealtimeSampleSink Sink { get; set; } = new TestEventsRealtimeSampleSink();
    }

    internal sealed class TestEventsRealtimeSampleSink : IRealtimeSampleSink
    {
        public void Publish(string stepKey, string channel, object value) =>
            TestEvents.RealTimeValueChanged?.Invoke(stepKey, channel, value);
    }
}
