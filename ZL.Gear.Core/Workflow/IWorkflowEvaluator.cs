using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 工作流逻辑评估器接口。
    /// 负责解析表达式、计算值以及处理字符串插值。
    /// </summary>
    public interface IWorkflowEvaluator
    {
        /// <summary>
        /// 评估一个布尔表达式（如 "Vars['Volt'] > 12.0"）。
        /// </summary>
        bool EvaluateCondition(string expression, IDictionary<string, object> variables);

        /// <summary>
        /// 评估一个值表达式（如 "@Vars['V1'] * 1.5"）。
        /// </summary>
        object EvaluateValue(object input, IDictionary<string, object> variables);

        /// <summary>
        /// 解析字符串中的插值（如 "当前电压: ${Volt} V"）。
        /// </summary>
        string Interpolate(string text, IDictionary<string, object> variables);
    }
}
