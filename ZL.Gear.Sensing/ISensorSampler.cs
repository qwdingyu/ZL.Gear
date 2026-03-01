using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 传感器采样器接口，利用响应式流提供数据
    /// </summary>
    public interface ISensorSampler : IDisposable
    {
        string Name { get; }
        
        /// <summary>
        /// 数据流
        /// </summary>
        IObservable<ZL.Gear.Core.Models.Measurement> DataStream { get; }

        /// <summary>
        /// 开始采样
        /// </summary>
        void Start();

        /// <summary>
        /// 停止采样
        /// </summary>
        void Stop();
    }
}
