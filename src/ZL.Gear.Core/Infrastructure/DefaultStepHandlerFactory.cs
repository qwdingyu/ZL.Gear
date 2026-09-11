using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 默认步骤处理器工厂。
    /// 职责：使用反射创建 <see cref="IStepHandler"/> 实例，无任何外部 NuGet 依赖。
    /// 设计要点：
    /// 1. 缓存构造函数信息，避免重复反射查询；
    /// 2. 优先使用无参构造函数；否则回退到"全部参数均带默认值"的构造函数（如 <c>Handler(Action&lt;string&gt; log = null)</c>），
    ///    以 <see cref="Type.Missing"/> 无参激活，避免 <see cref="ConstructorInfo.Invoke(object[])"/> 抛出 Parameter count mismatch；
    /// 3. 不存在可无参激活的构造函数时，抛出带指引的清晰异常，杜绝静默失败；
    /// 4. 可在 .NET Framework 4.6.1+ 环境下直接运行，兼容工控 legacy 宿主。
    /// </summary>
    public class DefaultStepHandlerFactory : IStepHandlerFactory
    {
        /// <summary>
        /// 构造函数缓存，按 Handler 类型缓存其构造信息，减少反射开销。
        /// 线程安全：<see cref="ConcurrentDictionary{TKey, TValue}"/> 支持并发读写。
        /// </summary>
        private readonly ConcurrentDictionary<Type, ConstructorInfo> _constructorCache
            = new ConcurrentDictionary<Type, ConstructorInfo>();

        /// <summary>
        /// 创建 Handler 实例（优先使用无参或全默认参数构造函数）。
        /// </summary>
        /// <param name="handlerType">Handler 类型，必须实现 <see cref="IStepHandler"/>。</param>
        /// <returns>Handler 实例。</returns>
        /// <exception cref="ArgumentNullException">handlerType 为 null。</exception>
        /// <exception cref="ArgumentException">handlerType 未实现 IStepHandler。</exception>
        /// <exception cref="InvalidOperationException">
        /// handlerType 没有可无参激活的构造函数（无无参构造，且不存在全部参数均带默认值的构造）。
        /// </exception>
        public IStepHandler CreateHandler(Type handlerType)
        {
            if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
            if (!typeof(IStepHandler).IsAssignableFrom(handlerType))
                throw new ArgumentException($"类型 {handlerType.FullName} 未实现 IStepHandler", nameof(handlerType));

            var ctor = _constructorCache.GetOrAdd(handlerType, ResolveActivatableConstructor);

            // 无参构造直接调用；全默认参数构造以 Type.Missing 填充，由反射绑定器应用默认值
            var parameters = ctor.GetParameters();
            object[] args = parameters.Length == 0 ? null : BuildMissingArguments(parameters.Length);

            return (IStepHandler)ctor.Invoke(args);
        }

        /// <summary>
        /// 解析可无参激活的构造函数：优先无参构造，其次全部参数带默认值的构造。
        /// </summary>
        /// <param name="handlerType">Handler 类型。</param>
        /// <returns>可无参激活的构造函数。</returns>
        /// <exception cref="InvalidOperationException">不存在可无参激活的构造函数。</exception>
        private static ConstructorInfo ResolveActivatableConstructor(Type handlerType)
        {
            var defaultCtor = handlerType.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null) return defaultCtor;

            // 全部参数均带默认值的构造（如 PlcStepHandler(Action<string> log = null)）可无参激活，
            // 选择参数最少者以减少绑定开销
            var optionalCtor = handlerType.GetConstructors()
                .Where(c => c.GetParameters().All(p => p.IsOptional))
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (optionalCtor != null) return optionalCtor;

            // 需要显式依赖的构造无法通过反射工厂无参创建。
            // 给出清晰指引而不是运行时 Invoke 崩溃（此前表现为被上层 catch 吞掉的静默失败）。
            throw new InvalidOperationException(
                $"类型 {handlerType.FullName} 没有无参或全默认参数构造函数，无法自动创建实例。" +
                "请为 Handler 提供无参构造函数（或将依赖参数声明为可选参数），" +
                "或改用 CreateHandler(type, args) / DiStepHandlerFactory 显式提供依赖。");
        }

        /// <summary>
        /// 构造用于激活全默认参数构造函数的参数数组，全部以 <see cref="Type.Missing"/> 占位。
        /// </summary>
        /// <param name="parameterCount">参数个数。</param>
        /// <returns>参数数组。</returns>
        private static object[] BuildMissingArguments(int parameterCount)
        {
            var args = new object[parameterCount];
            for (int i = 0; i < parameterCount; i++)
            {
                args[i] = Type.Missing;
            }
            return args;
        }

        /// <summary>
        /// 创建 Handler 实例（带参数）。
        /// 注意：此方法不会缓存构造函数，因为参数类型组合空间太大。
        /// 仅在确实需要依赖注入参数时使用，常规场景请使用无参版本。
        /// </summary>
        /// <param name="handlerType">Handler 类型，必须实现 <see cref="IStepHandler"/>。</param>
        /// <param name="args">构造函数参数。</param>
        /// <returns>Handler 实例。</returns>
        /// <exception cref="ArgumentNullException">handlerType 为 null。</exception>
        /// <exception cref="ArgumentException">handlerType 未实现 IStepHandler。</exception>
        /// <exception cref="InvalidOperationException">handlerType 没有匹配的构造函数。</exception>
        public IStepHandler CreateHandler(Type handlerType, params object[] args)
        {
            if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
            if (!typeof(IStepHandler).IsAssignableFrom(handlerType))
                throw new ArgumentException($"类型 {handlerType.FullName} 未实现 IStepHandler", nameof(handlerType));

            var ctor = handlerType.GetConstructor(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                Array.ConvertAll(args, a => a?.GetType() ?? typeof(object)),
                null);

            if (ctor == null)
                throw new InvalidOperationException($"类型 {handlerType.FullName} 没有匹配的构造函数");

            return (IStepHandler)ctor.Invoke(args);
        }
    }
}
