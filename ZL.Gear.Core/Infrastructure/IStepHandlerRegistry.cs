namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 1. 注册表接口：定义“谁”有资格接收命令注册
    /// StepDispatcher 将实现这个接口
    /// </summary>
    public interface IStepHandlerRegistry
    {
        void RegisterHandler(string command, IStepHandler handler, bool allowOverwrite = true);
        // 如果需要，也可以暴露原子动作的注册
        // void RegisterAction(string name, ActionDelegate action);
    }

    /// <summary>
    /// 2. 扩展模块启动接口：每个扩展DLL必须实现这个接口，作为入口点
    /// </summary>
    public interface IGearExtension
    {
        /// <summary>
        /// 扩展初始化时调用
        /// </summary>
        /// <param name="registry">核心框架传入的注册能力</param>
        void Initialize(IStepHandlerRegistry registry);
    }
}
