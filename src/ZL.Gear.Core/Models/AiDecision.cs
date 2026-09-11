using System.Collections.Generic;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// AI 决策结果模型。
    /// </summary>
    public class AiDecision
    {
        /// <summary>
        /// 下一步建议的动作。
        /// 例如: "Retry", "Continue", "Abort", "AdjustParam", "ExecuteStep"
        /// </summary>
        public string NextAction { get; set; }

        /// <summary>
        /// 建议的新参数（如果有）。
        /// 用于动态调整设备参数或流程变量。
        /// </summary>
        public Dictionary<string, object> NewParameters { get; set; }

        /// <summary>
        /// 决策理由（用于日志或调试）。
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// 决策置信度 (0.0 - 1.0)。
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// 创建一个成功的决策。
        /// </summary>
        public static AiDecision Make(string action, string reason, Dictionary<string, object> parameters = null)
        {
            return new AiDecision
            {
                NextAction = action,
                Reason = reason,
                NewParameters = parameters ?? new Dictionary<string, object>()
            };
        }
    }
}
