using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 定义一个原子操作的委托，这是构成工作流的基本单元。
    /// </summary>
    public delegate Task<ExecutionResultBase> ActionDelegate(StepConfig step, StepContext context);
    /// <summary>
    /// 定义一个专门用于测量的原子操作委托，它强类型返回一个 Measurement 对象。
    /// </summary>
    public delegate Task<Measurement> MeasurementActionDelegate(StepConfig step, StepContext context);

    /// <summary>
    /// 定义动作注册时的冲突处理策略
    /// </summary>
    public enum RegistrationPolicy
    {
        /// <summary>
        /// 如果已存在同名动作，抛出异常（严格模式，推荐）
        /// </summary>
        ThrowIfExists,

        /// <summary>
        /// 如果已存在，则覆盖（适用于热更新或插件修正基础行为）
        /// </summary>
        Overwrite,

        /// <summary>
        /// 如果已存在，则忽略本次注册（保留原有行为）
        /// </summary>
        Ignore
    }
    /// <summary>
    /// 定义获取动作的能力（读）
    /// </summary>
    public interface IActionResolver
    {
        /// <summary>
        /// 根据名称获取动作
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        ActionDelegate ResolveAction(string name);
        /// <summary>
        /// 根据名称获取测量动作
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        MeasurementActionDelegate ResolveMeasurement(string name);
    }

    /// <summary>
    /// 动作注册中心接口
    /// 定义注册动作的能力（写）
    /// </summary>
    public interface IActionRegistry
    {
        void RegisterAction(string name, Func<StepConfig, StepContext, Task<(bool Success, string Message)>> setupFunc, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists);

        /// <summary>
        /// 注册一个普通动作（无返回值/仅返回成功失败）
        /// </summary>
        /// <param name="name">动作唯一名称（不区分大小写）</param>
        /// <param name="action">动作委托</param>
        /// <param name="policy">冲突策略</param>
        void RegisterAction(string name, ActionDelegate action, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists);

        /// <summary>
        /// 注册一个测量动作（返回 Measurement 数据）
        /// </summary>
        void RegisterMeasurement(string name, MeasurementActionDelegate action, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists);

        /// <summary>
        /// 获取所有已注册的动作名称（用于启动期冲突扫描）。
        /// </summary>
        /// <returns>已注册动作的只读集合。</returns>
        IEnumerable<string> GetRegisteredActions();

        ///// <summary>
        ///// 检查某个动作是否已注册
        ///// </summary>
        //bool HasAction(string name);
        //ActionDelegate GetAction(string name);
        //MeasurementActionDelegate GetMeasurementAction(string name);
    }

    /// <summary>
    /// 工作流动作提供者接口
    /// 任何想要扩展 MicroWorkflow 能力的模块（插件、驱动）都需要实现此接口
    /// </summary>
    public interface IWorkflowActionProvider
    {
        /// <summary>
        /// 在此处执行具体的注册逻辑
        /// </summary>
        void RegisterActions(IActionRegistry registry);
    }
}
