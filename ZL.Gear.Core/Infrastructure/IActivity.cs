using System;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 分布式链路追踪接口（轻量级，.NET Standard 2.0 兼容）。
    /// 用于包装步骤执行为 Activity，支持跨进程/跨服务追踪。
    /// </summary>
    public interface IActivity
    {
        /// <summary>
        /// 启动一个 Activity，返回可释放的 scope。
        /// </summary>
        /// <param name="name">Activity 名称，如 "step.execute"。</param>
        /// <param name="tags">标签键值对。</param>
        /// <returns>Activity scope，释放时结束 Activity。</returns>
        IDisposable StartActivity(string name, params (string Key, string Value)[] tags);
    }
}
