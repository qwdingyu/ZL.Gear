using System.Collections.Generic;

namespace ZL.Gear.Sensing.Dto
{
    public enum ExeStatus
    {
        NotStarted,
        Completed,
        TimedOut,
        Cancelled,
        Failed
    }
    public class ExeResult<T>
    {
        public T Value { get; }
        public IReadOnlyList<T> AllSamples { get; }
        public bool IsSuccess => Status == ExeStatus.Completed;
        public bool SpecPassed { get; }
        public ExeStatus Status { get; }
        public string Message { get; }

        private ExeResult(ExeStatus status, T value, IReadOnlyList<T> allSamples, bool specPassed, string message)
        {
            Status = status;
            Value = value;
            AllSamples = allSamples ?? new List<T>().AsReadOnly();
            SpecPassed = specPassed;
            Message = message;
        }

        // --- 工厂方法，用于创建不同状态的结果 ---

        public static ExeResult<T> Success(T value, IReadOnlyList<T> samples, bool specPassed, string message = "操作成功完成")
        {
            return new ExeResult<T>(ExeStatus.Completed, value, samples, specPassed, message);
        }

        public static ExeResult<T> TimedOut(IReadOnlyList<T> collectedSamples, string message)
        {
            return new ExeResult<T>(ExeStatus.TimedOut, default, collectedSamples, false, message);
        }

        public static ExeResult<T> Cancelled(IReadOnlyList<T> collectedSamples)
        {
            return new ExeResult<T>(ExeStatus.Cancelled, default, collectedSamples, false, "操作被用户取消。");
        }

        public static ExeResult<T> Failed(string message, IReadOnlyList<T> collectedSamples = null)
        {
            return new ExeResult<T>(ExeStatus.Failed, default, collectedSamples, false, message);
        }
    }
}
