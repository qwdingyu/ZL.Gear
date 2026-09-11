using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Engine.Workflow
{
    /// <summary>
    /// 负责创建和提供 MicroWorkflow 所需的不同类型的委托。
    /// </summary>
    public class WorkflowActionService : IActionRegistry, IActionResolver
    {
        //private static readonly Lazy<WorkflowActionService> _instance = new Lazy<WorkflowActionService>(() => new WorkflowActionService(), LazyThreadSafetyMode.ExecutionAndPublication);

        ///// <summary>
        ///// 单例实例
        ///// </summary>
        //public static WorkflowActionService Instance => _instance.Value;
        // 注册表1: 存储通用动作委托 (ActionDelegate)
        private readonly ConcurrentDictionary<string, ActionDelegate> _actions = new(StringComparer.OrdinalIgnoreCase);

        // 注册表2: 存储测量动作委托 (MeasurementActionDelegate)
        private readonly ConcurrentDictionary<string, MeasurementActionDelegate> _measurements = new(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _log;
        public WorkflowActionService(Action<string> log)
        {
            _log = log ?? (s => { });
        }
            /// <summary>
            /// 注册执行动作
            /// </summary>
            /// <param name="name"></param>
            /// <param name="setupFunc"></param>
            /// <param name="policy"></param>
            public void RegisterAction(string name, Func<StepConfig, StepContext, Task<(bool Success, string Message)>> setupFunc, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists)
        {
            ActionDelegate action = CreateAction(setupFunc);
            RegisterInternal(_actions, "Action", name, action, policy);
        }
        /// <summary>
        /// 注册执行动作
        /// </summary>
        /// <param name="name"></param>
        /// <param name="action"></param>
        /// <param name="policy"></param>
        public void RegisterAction(string name, ActionDelegate action, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists)
        {
            RegisterInternal(_actions, "Action", name, action, policy);
        }
        /// <summary>
        /// 注册测量类
        /// </summary>
        /// <param name="name"></param>
        /// <param name="action"></param>
        /// <param name="policy"></param>
        public void RegisterMeasurement(string name, MeasurementActionDelegate action, RegistrationPolicy policy = RegistrationPolicy.ThrowIfExists)
        {
            RegisterInternal(_measurements, "Measurement", name, action, policy);
        }

        //public bool HasAction(string name) => _actions.ContainsKey(name);
        ///// <summary>
        ///// 获取一个通用的 ActionDelegate，用于 .Then() 方法。
        ///// </summary>
        ///// <param name="name">操作的唯一名称。</param>
        //public ActionDelegate GetAction(string name)
        //{
        //    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        //    if (_actions.TryGetValue(name, out var action)) return action;

        //    throw new KeyNotFoundException($"[WorkflowActionService] 未找到名为 '{name}' 的动作。请检查拼写或确认相关插件是否已加载。");
        //}
        ///// <summary>
        ///// 获取一个测量的 MeasurementActionDelegate，用于 .ThenMeasure() 方法。
        ///// </summary>
        ///// <param name="name">测量的唯一名称。</param>
        //public MeasurementActionDelegate GetMeasurementAction(string name)
        //{
        //    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        //    if (_measurements.TryGetValue(name, out var action)) return action;

        //    throw new KeyNotFoundException($"[WorkflowActionService] 未找到名为 '{name}' 的测量动作。");
        //}
        #region Private Adapter Methods
        private void RegisterInternal<T>(ConcurrentDictionary<string, T> dict, string typeTag, string name, T item, RegistrationPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _log($"[Register] 尝试注册 {typeTag} 失败：名称不能为空。");
                return; // 或者 throw ArgumentNullException
            }
            if (item == null)
            {
                _log($"[Register] 尝试注册 {typeTag} '{name}' 失败：委托不能为空。");
                throw new ArgumentNullException(nameof(item));
            }

            // 检查存在性
            bool exists = dict.ContainsKey(name);

            if (exists)
            {
                switch (policy)
                {
                    case RegistrationPolicy.ThrowIfExists:
                        throw new InvalidOperationException($"[Register] 重复注册错误：{typeTag} '{name}' 已经存在。");

                    case RegistrationPolicy.Ignore:
                        _log($"[Register] 忽略重复注册：{typeTag} '{name}' 已存在，保留原值。");
                        return;

                    case RegistrationPolicy.Overwrite:
                        dict[name] = item;
                        _log($"[Register] 覆盖注册：{typeTag} '{name}' 已被更新。");
                        break;
                }
            }
            else
            {
                // 尝试添加（并发安全）
                if (!dict.TryAdd(name, item))
                {
                    // 极少数情况：在检查和添加之间被其他线程抢占
                    if (policy == RegistrationPolicy.ThrowIfExists)
                        throw new InvalidOperationException($"[Register] 并发冲突：{typeTag} '{name}' 添加失败。");
                }
            }
        }
        private static ActionDelegate CreateAction(Func<StepConfig, StepContext, Task<(bool Success, string Message)>> setupFunc)
        {
            return async (step, context) =>
            {
                var (success, message) = await setupFunc(step, context);
                return success ? ExecutionResult.Succeeded() : ExecutionResult.Failed(message);
            };
        }
        #endregion 
        // --- IActionResolver 实现 (解析) ---
        public ActionDelegate ResolveAction(string name)
        {
            if (_actions.TryGetValue(name, out var action)) return action;
            throw new KeyNotFoundException($"动作 '{name}' 未注册。请检查插件加载情况。");
        }

        public MeasurementActionDelegate ResolveMeasurement(string name)
        {
            if (_measurements.TryGetValue(name, out var action)) return action;
            throw new KeyNotFoundException($"测量动作 '{name}' 未注册。");
        }

        public IEnumerable<string> GetRegisteredActions()
        {
            return _actions.Keys.ToList();
        }
    }
}
