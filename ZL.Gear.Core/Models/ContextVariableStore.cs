using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ZL.Gear.Core.Models
{

    /// <summary>
    /// 统一的上下文变量与信令存储容器。
    /// 负责管理流程中的动态数据、中间结果以及并行步骤间的同步信令。
    /// </summary>
    public class ContextVariableStore : IDisposable
    {
        private readonly ContextVariableStore _parent;
        private readonly ConcurrentDictionary<string, object> _store = new ConcurrentDictionary<string, object>();

        // === 命名空间常量 (防止 Key 冲突) ===
        private const string PREFIX_SIGNAL_START = "$SYS:SIG:START:";
        private const string PREFIX_SIGNAL_END = "$SYS:SIG:END:";

        public ContextVariableStore(ContextVariableStore parent = null)
        {
            _parent = parent;
        }

        /// <summary>
        /// 创建子作用域。
        /// </summary>
        public ContextVariableStore CreateChildScope() => new ContextVariableStore(this);

        /// <summary>
        /// 设置变量。
        /// 注意：设置操作始终在当前作用域生效，不会修改父作用域（写入隔离）。
        /// </summary>
        public void Set<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            _store[key] = value;
        }

        /// <summary>
        /// 尝试获取变量，支持向父作用域递归查找。
        /// </summary>
        public bool TryGet<T>(string key, out T value)
        {
            // 1. 先尝试在当前作用域查找
            if (_store.TryGetValue(key, out var obj))
            {
                if (TryCast<T>(obj, out value)) return true;
            }

            // 2. 如果没找到且存在父作用域，递归查找
            if (_parent != null) return _parent.TryGet<T>(key, out value);

            value = default;
            return false;
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            return TryGet<T>(key, out var val) ? val : defaultValue;
        }

        private bool TryCast<T>(object obj, out T result)
        {
            if (obj is T tVal) { result = tVal; return true; }
            if (obj != null && typeof(IConvertible).IsAssignableFrom(obj.GetType()) && typeof(IConvertible).IsAssignableFrom(typeof(T)))
            {
                try { result = (T)Convert.ChangeType(obj, typeof(T)); return true; } catch { }
            }
            result = default; return false;
        }

        /// <summary>
        /// 获取当前作用域的所有平铺字典（用于表达式评估，可见性向上收缩）
        /// </summary>
        public IDictionary<string, object> AsDictionary()
        {
            var dict = _parent?.AsDictionary() ?? new Dictionary<string, object>();
            foreach (var kvp in _store) dict[kvp.Key] = kvp.Value;
            return dict;
        }

        // ==========================================================
        // 2. 关键信令管理部分 (信令始终在全局/根存储中管理，以保证跨作用域同步)
        // ==========================================================
        private ContextVariableStore GetRoot() => _parent?.GetRoot() ?? this;

        public void RegisterSignalPairFor(string masterStepKey)
        {
            var root = GetRoot();
            root._store.GetOrAdd(GetStartKey(masterStepKey), _ =>
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

            root._store.GetOrAdd(GetEndKey(masterStepKey), _ => new CancellationTokenSource());
        }

        public bool TryGetSignalPair(string stepKey, out StepSignalPair signals)
        {
            signals = null;
            var root = GetRoot();

            if (root._store.TryGetValue(GetStartKey(stepKey), out var startObj) &&
                root._store.TryGetValue(GetEndKey(stepKey), out var endObj))
            {
                if (startObj is TaskCompletionSource<bool> tcs &&
                    endObj is CancellationTokenSource cts)
                {
                    signals = new StepSignalPair(tcs, cts);
                    return true;
                }
            }
            return false;
        }

        private static string GetStartKey(string stepKey) => PREFIX_SIGNAL_START + stepKey;
        private static string GetEndKey(string stepKey) => PREFIX_SIGNAL_END + stepKey;

        public void Dispose()
        {
            // 递归清理当前层和所有父作用域
            DisposeRecursive(this);
            _store.Clear();
        }

        private static void DisposeRecursive(ContextVariableStore store)
        {
            if (store == null) return;

            // 先递归清理父作用域
            if (store._parent != null)
            {
                DisposeRecursive(store._parent);
            }

            // 清理当前层
            foreach (var kvp in store._store)
            {
                if (kvp.Value is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { /* Dispose 不应抛出异常 */ }
                }
            }
            store._store.Clear();
        }
    }
}
