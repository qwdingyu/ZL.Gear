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
        /// 评估一个值表达式（如 "@Vars['V1'] * 1.5"）。失败时返回原输入（兼容插值宽松路径）。
        /// </summary>
        object EvaluateValue(object input, IDictionary<string, object> variables);

        /// <summary>
        /// 求值算术/对象表达式；失败抛出。供 Calculate 等必须得到真值的路径。
        /// </summary>
        object EvaluateExpression(string expression, IDictionary<string, object> variables);

        /// <summary>
        /// 解析字符串中的插值（如 "当前电压: ${Volt} V"）。
        /// </summary>
        string Interpolate(string text, IDictionary<string, object> variables);
    }
}
