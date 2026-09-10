using DynamicExpresso;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ZL.Gear.Core.Workflow
{
    public class WorkflowEvaluator : IWorkflowEvaluator
    {
        private readonly Interpreter _interpreter;
        private static readonly Regex _interpolationRegex = new Regex(@"\$\{(.+?)\}", RegexOptions.Compiled);

        public WorkflowEvaluator()
        {
            _interpreter = new Interpreter();
            // 注册常用类型以便在表达式中使用
            _interpreter.Reference(typeof(Math));
            _interpreter.Reference(typeof(Convert));
            _interpreter.Reference(typeof(TimeSpan));
        }

        public bool EvaluateCondition(string expression, IDictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(expression)) return true;

            try
            {
                // Vars 字典取值为 object：比较/算术请在表达式内 Convert.ToDouble / Convert.ToBoolean；键名用双引号。
                var parameters = new[] { new Parameter("Vars", typeof(IDictionary<string, object>), NormalizeVars(variables)) };
                var result = _interpreter.Eval(expression, parameters);
                return result is bool b && b;
            }
            catch (Exception ex)
            {
                // 这里可以注入日志记录
                System.Diagnostics.Debug.WriteLine($"[Evaluator Error] Condition: {expression}, Error: {ex.Message}");
                return false;
            }
        }

        public object EvaluateValue(object input, IDictionary<string, object> variables)
        {
            if (input is string str && str.StartsWith("@"))
            {
                var expression = str.Substring(1);
                try
                {
                    var parameters = new[] { new Parameter("Vars", typeof(IDictionary<string, object>), NormalizeVars(variables)) };
                    return _interpreter.Eval(expression, parameters);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Evaluator Error] Value Expr: {expression}, Error: {ex.Message}");
                    return input;
                }
            }
            return input;
        }

        /// <summary>
        /// 求值表达式；失败抛出（供 Calculate 等必须拿到真值的路径，避免静默把公式字符串当结果）。
        /// </summary>
        public object EvaluateExpression(string expression, IDictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("表达式为空。", nameof(expression));
            }

            var expr = expression.StartsWith("@", StringComparison.Ordinal) ? expression.Substring(1) : expression;
            var parameters = new[] { new Parameter("Vars", typeof(IDictionary<string, object>), NormalizeVars(variables)) };
            return _interpreter.Eval(expr, parameters);
        }

        /// <summary>
        /// 将 Vars 规范为 IDictionary，并展开 JValue，保证 DynamicExpresso 算术/比较可用。
        /// </summary>
        private static IDictionary<string, object> NormalizeVars(IDictionary<string, object> variables)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (variables == null)
            {
                return dict;
            }

            foreach (var kvp in variables)
            {
                var v = kvp.Value;
                if (v is Newtonsoft.Json.Linq.JValue jv)
                {
                    v = jv.Value;
                }

                dict[kvp.Key] = v;
            }

            return dict;
        }

        public string Interpolate(string text, IDictionary<string, object> variables)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return _interpolationRegex.Replace(text, match =>
            {
                var key = match.Groups[1].Value;
                if (variables.TryGetValue(key, out var val))
                {
                    return val?.ToString() ?? "null";
                }
                return match.Value; // 没找到则保持原样
            });
        }
    }
}
