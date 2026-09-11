using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing.Abstractions
{
    public interface ICommandHandler
    {
        /// <summary>
        /// 检查是否包含对应的处理函数
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        bool CanHandle(string command);

        /// <summary>
        /// 业务处理类
        /// </summary>
        /// <param name="command"></param>
        /// <param name="args"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        Task<ExecutionResultBase> HandleAsync(string command, Dictionary<string, object> args, StepContext context);
    }

}
