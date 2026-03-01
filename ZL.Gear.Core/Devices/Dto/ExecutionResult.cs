using System;

namespace ZL.Gear.Core.Devices
{
    public enum ExecutionStatus
    {
        NotStarted,
        Completed,
        TimedOut,
        Cancelled,
        Failed
    }
    // 新增：一个简单的内部接口，用于在非泛型基类上获取 object 类型的值
    public interface IValueProvider
    {
        object? GetValueAsObject();
    }
    /// <summary>
    /// [内部工具] 所有执行结果的基类。
    /// 主要用于在系统内部传递操作状态。
    /// </summary>
    public abstract class ExecutionResultBase
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "OK";
        public int SamplesCollected { get; set; } = 1;

        // ★ 新增：一个抽象属性，用于获取值的类型
        public abstract Type ValueType { get; }
        // ★ 新增：一个抽象属性，用于以 object 形式获取值
        public abstract object? GetValueAsObject();
        // 构造函数设为 protected，强制通过静态工厂方法创建
        protected ExecutionResultBase() { }
    }
    /// <summary>
    /// [内部工具] 表示无返回值的操作结果。
    /// </summary>
    public sealed class ExecutionResult : ExecutionResultBase
    {
        private ExecutionResult() { }
        public override Type ValueType => typeof(void); // void 表示没有值
        public override object? GetValueAsObject() => null;
        public static ExecutionResult Succeeded(string message = "OK")
            => new() { Success = true, Message = message };
        public static ExecutionResult Failed(string errorMessage)
            => new() { Success = false, Message = errorMessage, SamplesCollected = 0 };
    }
    /// <summary>
    /// [内部工具] 表示带有强类型返回值的操作结果。
    /// </summary>
    /// <typeparam name="T">返回值的类型。</typeparam>
    public sealed class ExecutionResult<T> : ExecutionResultBase, IValueProvider
    {
        public T Value { get; private set; }
        private ExecutionResult() { }
        public override Type ValueType => typeof(T);
        public override object? GetValueAsObject() => Value;
        // 工厂方法简化
        public static ExecutionResult<T> Succeeded(T value, int sampleCount = 1, string message = "OK")
            => new() { Success = true, Value = value, SamplesCollected = sampleCount, Message = message };
        public static ExecutionResult<T> Failed(string errorMessage, T? defaultValue = default)
            => new() { Success = false, Message = errorMessage, Value = defaultValue!, SamplesCollected = 0 };

        public static ExecutionResult<T> Failed(string errorMessage, T value, int sampleCount = 1)
            => new() { Success = false, Message = errorMessage, Value = value!, SamplesCollected = sampleCount };
    } 
}
