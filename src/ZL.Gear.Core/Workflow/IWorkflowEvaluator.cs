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
        /// 评估一个布尔表达式（如 "Ready" 或 "Margin > 0"；逃逸仍可用 Vars）。
        /// 官方方言见 docs/133。
        /// </summary>
        bool EvaluateCondition(string expression, IDictionary<string, object> variables);

        /// <summary>
        /// 评估一个值表达式（如 "@LimitOhm * 1.5"）。失败时返回原输入（兼容插值宽松路径）。
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

        /// <summary>
        /// 编译期布尔条件语法预检（不依赖运行时变量值；变量未定义时仅校验表达式结构）。
        /// </summary>
        /// <param name="expression">Parameters.Condition 等布尔表达式。</param>
        /// <param name="errorMessage">失败时的错误描述。</param>
        /// <returns>语法可解析且目标类型为 bool 时返回 true。</returns>
        bool TryValidateConditionSyntax(string expression, out string errorMessage);
    }
}
