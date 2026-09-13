using DynamicExpresso;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Workflow
{
    /// <summary>
    /// 工作流表达式求值器（docs/133）：过滤裸标识符提升 + Vars 逃逸字典。
    /// </summary>
    public class WorkflowEvaluator : IWorkflowEvaluator
    {
        private readonly Interpreter _interpreter;
        private static readonly Regex InterpolationRegex = new Regex(@"\$\{(.+?)\}", RegexOptions.Compiled);
        private static readonly Regex IdentRegex = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Vars", "Math", "Convert", "TimeSpan",
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
            "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
            "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
            "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
            "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        public WorkflowEvaluator()
        {
            _interpreter = new Interpreter();
            _interpreter.Reference(typeof(Math));
            _interpreter.Reference(typeof(Convert));
            _interpreter.Reference(typeof(TimeSpan));
        }

        public bool EvaluateCondition(string expression, IDictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(expression)) return true;

            try
            {
                var result = _interpreter.Eval(expression, BuildParameters(variables));
                return result is bool b && b;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Evaluator Error] Condition: {expression}, Error: {ex.Message}");
                return false;
            }
        }

        public object EvaluateValue(object input, IDictionary<string, object> variables)
        {
            if (input is string str && str.StartsWith("@", StringComparison.Ordinal))
            {
                try
                {
                    return _interpreter.Eval(str.Substring(1), BuildParameters(variables));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Evaluator Error] Value Expr: {str}, Error: {ex.Message}");
                    return input;
                }
            }
            return input;
        }

        /// <summary>
        /// 求值表达式；失败抛出（供 Calculate 等必须拿到真值的路径）。
        /// </summary>
        public object EvaluateExpression(string expression, IDictionary<string, object> variables)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("表达式为空。", nameof(expression));

            var expr = expression.StartsWith("@", StringComparison.Ordinal) ? expression.Substring(1) : expression;
            return _interpreter.Eval(expr, BuildParameters(variables));
        }

        public string Interpolate(string text, IDictionary<string, object> variables)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var dict = NormalizeVars(variables);
            return InterpolationRegex.Replace(text, match =>
            {
                var key = match.Groups[1].Value;
                return dict.TryGetValue(key, out var val) ? (val?.ToString() ?? "null") : match.Value;
            });
        }

        /// <inheritdoc />
        public bool TryValidateConditionSyntax(string expression, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return true;
            }

            try
            {
                // 空变量表：仅验证 DynamicExpresso 能否解析为 bool；未定义标识符在运行期再绑定
                _interpreter.Parse(expression, typeof(bool), BuildParameters(new Dictionary<string, object>()));
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>供测试/论证：列出将被提升为裸标识符的键。</summary>
        public static List<string> ListPromotableKeys(IDictionary<string, object> variables)
        {
            return EnumeratePromotions(NormalizeVars(variables)).Select(p => p.Name).OrderBy(x => x).ToList();
        }

        private Parameter[] BuildParameters(IDictionary<string, object> variables)
        {
            var normalized = NormalizeVars(variables);
            var list = new List<Parameter>
            {
                new Parameter("Vars", typeof(IDictionary<string, object>), normalized)
            };
            foreach (var p in EnumeratePromotions(normalized))
                list.Add(p);
            return list.ToArray();
        }

        private static IEnumerable<Parameter> EnumeratePromotions(IDictionary<string, object> normalized)
        {
            foreach (var kvp in normalized)
            {
                if (!IdentRegex.IsMatch(kvp.Key)) continue;
                if (Reserved.Contains(kvp.Key)) continue;
                if (!TryPromote(kvp.Value, out var type, out var value)) continue;
                yield return new Parameter(kvp.Key, type, value);
            }
        }

        private static bool TryPromote(object raw, out Type type, out object value)
        {
            type = typeof(object);
            value = null;
            var v = Unwrap(raw);
            if (v == null)
            {
                type = typeof(object);
                value = null;
                return true;
            }

            if (v is bool b)
            {
                type = typeof(bool);
                value = b;
                return true;
            }

            if (v is string s)
            {
                type = typeof(string);
                value = s;
                return true;
            }

            if (v is IDictionary<string, object> || v is JObject || v is JArray)
                return false;

            if (v is System.Collections.IEnumerable && !(v is string))
                return false;

            if (v is Delegate || v is Task || (v.GetType().IsGenericType
                && v.GetType().GetGenericTypeDefinition().FullName == "System.Threading.Tasks.TaskCompletionSource`1"))
                return false;

            if (v is IConvertible && IsNumeric(v))
            {
                type = typeof(double);
                value = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return true;
            }

            if (!v.GetType().IsPrimitive && v.GetType() != typeof(decimal) && v.GetType() != typeof(DateTime))
                return false;

            return false;
        }

        private static bool IsNumeric(object v)
        {
            switch (Type.GetTypeCode(v.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static object Unwrap(object value) => value is JValue jv ? jv.Value : value;

        private static IDictionary<string, object> NormalizeVars(IDictionary<string, object> variables)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (variables == null) return dict;
            foreach (var kvp in variables)
                dict[kvp.Key] = Unwrap(kvp.Value);
            return dict;
        }
    }
}
