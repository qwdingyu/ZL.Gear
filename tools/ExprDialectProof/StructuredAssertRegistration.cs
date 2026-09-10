using System;
using System.Globalization;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Workflow;

namespace ZL.Gear.ExprDialectProof
{
    /// <summary>
    /// 论证用 Assert：L0 结构化 + L1 Check 反糖化（docs/133 增补）。
    /// </summary>
    public static class StructuredAssertRegistration
    {
        public static void RegisterOverwrite(WorkflowActionService registry)
        {
            registry.RegisterAction("Assert", async (step, ctx) =>
            {
                object condRaw = null;
                object checkRaw = null;
                var p = step.Parameters;
                var hasLeft = p != null && p.ContainsKey("Left");
                var hasOp = p != null && p.ContainsKey("Op");
                var hasCheck = p != null
                    && p.TryGetValue("Check", out checkRaw)
                    && checkRaw != null
                    && !(checkRaw is string ch && string.IsNullOrWhiteSpace(ch));
                var hasCondition = p != null
                    && p.TryGetValue("Condition", out condRaw)
                    && condRaw != null
                    && !(condRaw is string cs && string.IsNullOrWhiteSpace(cs));

                // L1 Check
                if (hasCheck)
                {
                    if (hasLeft || hasOp || hasCondition)
                        return ExecutionResult.Failed("L1 Check 禁止与 Left/Op/Condition 同时出现。");

                    if (!AssertCheckParser.TryParse(Convert.ToString(checkRaw), out var d, out var perr))
                        return ExecutionResult.Failed("Check 解析失败: " + perr);

                    var message = ctx.Get<string>("Message") ?? "断言失败";
                    return await Task.FromResult(EvaluateL0(ctx, d.Left, d.Op, d.HasRightVar, d.RightVar, d.RightLiteral, message))
                        .ConfigureAwait(false);
                }

                // L0
                if (hasLeft && hasOp)
                {
                    if (hasCondition)
                        return ExecutionResult.Failed("结构化 Assert 禁止与 Condition 同时出现。");

                    var leftName = ctx.Get<string>("Left");
                    var opRaw = ctx.Get<string>("Op");
                    if (!AssertCheckParser.TryNormalizeOp(opRaw, out var op))
                        return ExecutionResult.Failed("未知 Op: " + opRaw);

                    var message = ctx.Get<string>("Message") ?? "断言失败";
                    var hasRight = p.ContainsKey("Right");
                    var hasRightVar = p.ContainsKey("RightVar");
                    if (hasRight == hasRightVar)
                        return ExecutionResult.Failed("结构化 Assert 须且仅指定 Right 或 RightVar 之一。");

                    string rightVar = hasRightVar ? ctx.Get<string>("RightVar") : null;
                    object rightLit = hasRight ? ctx.Get<object>("Right") : null;
                    return await Task.FromResult(EvaluateL0(ctx, leftName, op, hasRightVar, rightVar, rightLit, message))
                        .ConfigureAwait(false);
                }

                if (hasLeft || hasOp)
                    return ExecutionResult.Failed("结构化 Assert 需要同时提供 Left 与 Op（或改用 Check）。");

                // L2 自由 Condition 逃逸
                bool ok = true;
                if (hasCondition)
                {
                    if (condRaw is bool b) ok = b;
                    else if (condRaw is string expr) ok = ctx.Evaluate(expr);
                    else
                    {
                        try { ok = Convert.ToBoolean(condRaw); }
                        catch { ok = ctx.Evaluate(Convert.ToString(condRaw)); }
                    }
                }

                if (!ok)
                    return ExecutionResult.Failed(ctx.Get<string>("Message") ?? "断言失败");
                return ExecutionResult.Succeeded();
            }, RegistrationPolicy.Overwrite);
        }

        private static ExecutionResultBase EvaluateL0(
            StepContext ctx,
            string leftName,
            string op,
            bool hasRightVar,
            string rightVar,
            object rightLiteral,
            string message)
        {
            if (string.IsNullOrWhiteSpace(leftName) || string.IsNullOrWhiteSpace(op))
                return ExecutionResult.Failed("结构化 Assert 需要 Left 与 Op。");

            if (!ctx.Variables.TryGet<object>(leftName, out var leftVal) || leftVal == null)
                return ExecutionResult.Failed($"结构化 Assert 缺少变量: {leftName}");

            object rightVal;
            if (hasRightVar)
            {
                if (string.IsNullOrWhiteSpace(rightVar)
                    || !ctx.Variables.TryGet<object>(rightVar, out rightVal)
                    || rightVal == null)
                    return ExecutionResult.Failed($"结构化 Assert 缺少变量: {rightVar}");
            }
            else
            {
                rightVal = rightLiteral;
            }

            if (!TryCompare(op, leftVal, rightVal, out var pass, out var err))
                return ExecutionResult.Failed(err ?? message);

            return pass ? ExecutionResult.Succeeded() : ExecutionResult.Failed(message);
        }

        private static bool TryCompare(string op, object left, object right, out bool pass, out string error)
        {
            pass = false;
            error = null;
            if (!AssertCheckParser.TryNormalizeOp(op, out var opNorm))
            {
                error = "未知 Op: " + op;
                return false;
            }

            if (left is bool lb && right is bool rb && (opNorm == "Eq" || opNorm == "Neq"))
            {
                pass = opNorm == "Eq" ? lb == rb : lb != rb;
                return true;
            }

            if (left is string ls && right is string rs && (opNorm == "Eq" || opNorm == "Neq"))
            {
                pass = opNorm == "Eq"
                    ? string.Equals(ls, rs, StringComparison.Ordinal)
                    : !string.Equals(ls, rs, StringComparison.Ordinal);
                return true;
            }

            double ld, rd;
            try
            {
                ld = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                rd = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                error = "结构化 Assert 无法转为数值: " + ex.Message;
                return false;
            }

            switch (opNorm)
            {
                case "Gt": pass = ld > rd; return true;
                case "Gte": pass = ld >= rd; return true;
                case "Lt": pass = ld < rd; return true;
                case "Lte": pass = ld <= rd; return true;
                case "Eq": pass = Math.Abs(ld - rd) < 1e-9; return true;
                case "Neq": pass = Math.Abs(ld - rd) >= 1e-9; return true;
                default:
                    error = "未知 Op: " + op;
                    return false;
            }
        }
    }
}
