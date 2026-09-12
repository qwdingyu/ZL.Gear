using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Sensing;

namespace ZL.Gear.Samples.Industry.Client.Bootstrap
{
    /// <summary>
    /// 无硬件时的 Noise 通道 mock，等价 legacy SeatTest/Plugin/NoiseService 的 SessionRequesters 注册。
    /// 生产环境替换为 NoiseSamplingService + 串口设备。
    /// </summary>
    public static class InMemoryNoiseSessionFactory
    {
        public static Task<ISamplingSession<double>> BeginSamplingAsync(double mockAverage = 45.0)
        {
            return Task.FromResult<ISamplingSession<double>>(new InMemoryNoiseSession(mockAverage));
        }

        private sealed class InMemoryNoiseSession : ISamplingSession<double>
        {
            private readonly double _mockAverage;
            private readonly List<double> _samples = new();
            private bool _disposed;

            public InMemoryNoiseSession(double mockAverage)
            {
                _mockAverage = mockAverage;
                Result = new SamplerStatisticsResult<double> { Channel = "Noise" };
            }

            public SamplerStatisticsResult<double> Result { get; private set; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Analyze();
            }

            internal void AddSample(double value) => _samples.Add(value);

            private void Analyze()
            {
                if (_samples.Count == 0)
                {
                    _samples.Add(_mockAverage);
                }

                Result.Max = _samples.Max();
                Result.Min = _samples.Min();
                Result.Average = Math.Round(_samples.Average(), 3, MidpointRounding.AwayFromZero);
                Result.Success = true;
                Result.Samples = _samples
                    .Select(v => new SensingWindowSample<double>(v, "mock"))
                    .ToList();
            }
        }
    }
}
