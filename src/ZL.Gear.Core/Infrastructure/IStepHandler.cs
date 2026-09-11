using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Infrastructure
{
    // 所有处理器的统一接口，无论是原子操作还是复合操作
    public interface IStepHandler
    {
        Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context);
    }
}
