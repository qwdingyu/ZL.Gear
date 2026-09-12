namespace ZL.Gear.Sensing
{
    /// <summary>宿主可替换 Sink；默认 NoOp（G1b-09 · Demos Bootstrap 接线 LegacyBridge）。</summary>
    public static class RealtimeSamplePublishing
    {
        public static IRealtimeSampleSink Sink { get; set; } = NoOpRealtimeSampleSink.Instance;
    }

    internal sealed class NoOpRealtimeSampleSink : IRealtimeSampleSink
    {
        public static readonly NoOpRealtimeSampleSink Instance = new NoOpRealtimeSampleSink();

        public void Publish(string stepKey, string channel, object value) { }
    }
}
