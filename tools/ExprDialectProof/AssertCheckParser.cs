using System;
using System.Globalization;
using System.Text;

namespace ZL.Gear.ExprDialectProof
{
    /// <summary>
    /// L1：将 Check 一行比较式反糖化为 L0（Left/Op/Right|RightVar）。
    /// 文法：Ident Cmp (Ident | Number | true|false | "string")
    /// Cmp：>=|<=|==|!=|>|&lt;（长的优先）；亦接受 &lt;&gt;、单 =（归一为 Neq/Eq）
    /// 标识符仅 ASCII（与过滤提升规则一致）；禁止算术/函数/括号/科学计数法。
    /// </summary>
    public static class AssertCheckParser
    {
        /// <summary>单条 Check 最大长度，防止异常超长配置。</summary>
        public const int MaxCheckLength = 256;

        public sealed class Desugared
        {
            public string Left { get; set; }
            /// <summary>规范枚举名：Gt/Gte/Lt/Lte/Eq/Neq</summary>
            public string Op { get; set; }
            public object RightLiteral { get; set; }
            public string RightVar { get; set; }
            public bool HasRightVar => !string.IsNullOrEmpty(RightVar);
        }

        public static bool TryParse(string check, out Desugared result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(check))
            {
                error = "Check 为空。";
                return false;
            }

            var s = check.Trim();
            if (s.Length > MaxCheckLength)
            {
                error = "Check 超过最大长度 " + MaxCheckLength + "。";
                return false;
            }

            var i = 0;
            if (!TryReadIdent(s, ref i, out var left))
            {
                error = "Check 左侧须为 ASCII 标识符（[A-Za-z_][A-Za-z0-9_]*）。";
                return false;
            }

            SkipWs(s, ref i);
            if (!TryReadOp(s, ref i, out var opSym))
            {
                error = "Check 缺少比较符（>=|<=|==|!=|>|<）。";
                return false;
            }

            SkipWs(s, ref i);
            if (i >= s.Length)
            {
                error = "Check 缺少右侧操作数。";
                return false;
            }

            object rightLit = null;
            string rightVar = null;

            if (s[i] == '"')
            {
                if (!TryReadQuoted(s, ref i, out var q, out var qerr))
                {
                    error = qerr ?? "Check 字符串字面量无效。";
                    return false;
                }
                rightLit = q;
            }
            else if (TryReadIdent(s, ref i, out var idOrKeyword))
            {
                if (idOrKeyword.Equals("true", StringComparison.OrdinalIgnoreCase))
                    rightLit = true;
                else if (idOrKeyword.Equals("false", StringComparison.OrdinalIgnoreCase))
                    rightLit = false;
                else
                    rightVar = idOrKeyword;
            }
            else if (i < s.Length && (IsAsciiDigit(s[i]) || s[i] == '+' || s[i] == '-' || s[i] == '.'))
            {
                if (!TryReadNumber(s, ref i, out var num, out var nerr))
                {
                    error = nerr ?? "Check 数字无效。";
                    return false;
                }
                rightLit = num;
            }
            else
            {
                error = "Check 右侧须为标识符、数字、true/false 或 \"字符串\"。";
                return false;
            }

            SkipWs(s, ref i);
            if (i != s.Length)
            {
                error = "Check 含多余字符（禁止算术/函数/括号/科学计数法后缀）。";
                return false;
            }

            if (!TryNormalizeOp(opSym, out var opEnum))
            {
                error = "未知比较符: " + opSym;
                return false;
            }

            result = new Desugared
            {
                Left = left,
                Op = opEnum,
                RightLiteral = rightLit,
                RightVar = rightVar
            };
            return true;
        }

        /// <summary>L0 Op 字段：枚举名或符号别名 → 规范枚举。</summary>
        public static bool TryNormalizeOp(string op, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(op)) return false;
            var t = op.Trim();
            switch (t)
            {
                case ">":
                case "Gt":
                case "gt":
                case "GT":
                    normalized = "Gt"; return true;
                case ">=":
                case "Gte":
                case "gte":
                case "GTE":
                    normalized = "Gte"; return true;
                case "<":
                case "Lt":
                case "lt":
                case "LT":
                    normalized = "Lt"; return true;
                case "<=":
                case "Lte":
                case "lte":
                case "LTE":
                    normalized = "Lte"; return true;
                case "==":
                case "=":
                case "Eq":
                case "eq":
                case "EQ":
                    normalized = "Eq"; return true;
                case "!=":
                case "<>":
                case "Neq":
                case "neq":
                case "NEQ":
                    normalized = "Neq"; return true;
                default:
                    return false;
            }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static bool IsAsciiLetter(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

        private static bool TryReadIdent(string s, ref int i, out string ident)
        {
            ident = null;
            SkipWs(s, ref i);
            if (i >= s.Length || !(IsAsciiLetter(s[i]) || s[i] == '_')) return false;
            var start = i;
            i++;
            while (i < s.Length && (IsAsciiLetter(s[i]) || IsAsciiDigit(s[i]) || s[i] == '_')) i++;
            ident = s.Substring(start, i - start);
            return true;
        }

        private static bool TryReadOp(string s, ref int i, out string op)
        {
            op = null;
            SkipWs(s, ref i);
            if (i >= s.Length) return false;
            // 长的优先；单 = 允许（归一 Eq），避免把 => 误读：先看 = 后一字符
            string[] ops = { ">=", "<=", "==", "!=", "<>", ">", "<", "=" };
            foreach (var candidate in ops)
            {
                if (i + candidate.Length <= s.Length
                    && string.CompareOrdinal(s, i, candidate, 0, candidate.Length) == 0)
                {
                    // 拒绝 =>（非本文法）
                    if (candidate == "=" && i + 1 < s.Length && s[i + 1] == '>')
                        return false;
                    op = candidate;
                    i += candidate.Length;
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadQuoted(string s, ref int i, out string value, out string error)
        {
            value = null;
            error = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '"')
                {
                    i++;
                    value = sb.ToString();
                    return true;
                }
                if (c == '\\')
                {
                    if (i + 1 >= s.Length)
                    {
                        error = "Check 字符串转义不完整。";
                        return false;
                    }
                    var n = s[i + 1];
                    if (n == '"' || n == '\\')
                    {
                        sb.Append(n);
                        i += 2;
                        continue;
                    }
                    error = "Check 字符串仅允许转义 \\\" 与 \\\\。";
                    return false;
                }
                sb.Append(c);
                i++;
            }
            error = "Check 字符串字面量未闭合。";
            return false;
        }

        private static bool TryReadNumber(string s, ref int i, out double num, out string error)
        {
            num = 0;
            error = null;
            SkipWs(s, ref i);
            var start = i;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            var anyDigit = false;
            while (i < s.Length && IsAsciiDigit(s[i])) { anyDigit = true; i++; }
            if (i < s.Length && s[i] == '.')
            {
                i++;
                var frac = false;
                while (i < s.Length && IsAsciiDigit(s[i])) { frac = true; anyDigit = true; i++; }
                if (!frac)
                {
                    i = start;
                    error = "Check 数字格式无效（小数点后须有数字）。";
                    return false;
                }
            }
            if (!anyDigit) { i = start; return false; }

            // 拒绝科学计数法后缀，避免 1e2 被截成 1 + 多余字符的含糊失败
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                error = "Check 不支持科学计数法，请写普通小数。";
                return false;
            }

            var token = s.Substring(start, i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out num))
            {
                error = "Check 数字无法解析: " + token;
                return false;
            }
            return true;
        }
    }
}
