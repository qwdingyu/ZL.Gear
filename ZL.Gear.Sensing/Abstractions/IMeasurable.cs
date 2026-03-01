using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing.Abstractions
{
    /// <summary>
    /// 定义一个可被编排器执行的、独立的测量操作。
    /// 这是设备类与编排器之间的核心契约。
    /// </summary>
    public interface IMeasurable
    {
        /// <summary>
        /// 执行具体的测量逻辑。
        /// </summary>
        /// <param name="token">由编排器提供的、已经包含了超时和主从联动逻辑的CancellationToken。</param>
        /// <returns>测量的执行结果。</returns>
        Task<ExecutionResultBase> MeasureAsync(CancellationToken token);
    }

}
