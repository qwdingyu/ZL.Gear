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
                var parameters = new[] { new Parameter("Vars", variables.GetType(), variables) };
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
                    var parameters = new[] { new Parameter("Vars", variables.GetType(), variables) };
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
