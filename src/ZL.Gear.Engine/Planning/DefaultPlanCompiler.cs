using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Planning;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Planning
{
    /// <summary>
    /// 默认计划编译器：Clone → 结构校验 → Profile → Normalize → Handler/Condition 预检。
    /// </summary>
    public sealed class DefaultPlanCompiler : IPlanCompiler
    {
        /// <inheritdoc />
        public PlanCompileResult Compile(IReadOnlyList<StepConfig> sourceSteps, PlanCompileContext context)
        {
            context ??= new PlanCompileContext();
            var options = context.Options ?? new PlanCompileOptions();
            var diagnostics = new List<PlanCompileDiagnostic>();

            if (sourceSteps == null || sourceSteps.Count == 0)
            {
                diagnostics.Add(PlanCompileDiagnostic.Error("PLAN_EMPTY", "计划为空：至少需要一个顶层步骤。"));
                return PlanCompileResult.Failed(diagnostics);
            }

            var planHash = ComputePlanHash(sourceSteps);
            List<StepConfig> compiledSteps;

            try
            {
                compiledSteps = sourceSteps.Select(s => (StepConfig)s.Clone()).ToList();
            }
            catch (Exception ex)
            {
                diagnostics.Add(PlanCompileDiagnostic.Error("PLAN_CLONE_FAILED", $"计划深拷贝失败: {ex.Message}"));
                return PlanCompileResult.Failed(diagnostics);
            }

            ValidateStructure(compiledSteps, diagnostics);

            var deviceRoles = context.DeviceRoleMap ?? new Dictionary<string, object>();
            var profile = context.ProfileMap ?? new Dictionary<string, string>();

            foreach (var root in compiledSteps)
            {
                try
                {
                    ApplyProfileRecursive(root, profile);
                    StepConfigNormalizer.Normalize(root, deviceRoles);
                    BridgeHandlerMetadataRecursive(root, context.HandlerRegistry);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(PlanCompileDiagnostic.Error("PLAN_NORMALIZE_FAILED", ex.Message, root.StepKey));
                }
            }

            ValidateHandlerResolution(compiledSteps, context, options, diagnostics);
            ValidateConditionSyntax(compiledSteps, context.WorkflowEvaluator, options, diagnostics);

            if (diagnostics.Any(d => d.Level == PlanCompileDiagnosticLevel.Error))
            {
                return PlanCompileResult.Failed(diagnostics);
            }

            var plan = new CompiledTestPlan(compiledSteps, planHash, DateTimeOffset.UtcNow);
            return new PlanCompileResult(plan, diagnostics);
        }

        private static void ValidateStructure(IEnumerable<StepConfig> roots, ICollection<PlanCompileDiagnostic> diagnostics)
        {
            var enabledKeys = roots
                .SelectMany(r => StepKit.FlattenSteps(r))
                .Where(s => s.Enable && !string.IsNullOrWhiteSpace(s.StepKey))
                .Select(s => s.StepKey)
                .ToList();

            foreach (var dup in enabledKeys
                         .GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1)
                         .Select(g => g.Key))
            {
                diagnostics.Add(PlanCompileDiagnostic.Error(
                    "DUPLICATE_STEP_KEY",
                    $"重复的 StepKey: '{dup}'（启用步骤的 StepKey 必须唯一）。",
                    dup));
            }

            foreach (var step in roots.SelectMany(r => StepKit.FlattenSteps(r)).Where(s => s.Enable))
            {
                if (string.IsNullOrWhiteSpace(step.StepKey))
                {
                    diagnostics.Add(PlanCompileDiagnostic.Warning(
                        "MISSING_STEP_KEY",
                        $"步骤 '{step.StepName ?? step.Command ?? "(未命名)"}' 缺少 StepKey，追溯与 Profile 注入可能受限。"));
                }

                var isGroup = string.Equals(step.StepType, "GROUP", StringComparison.OrdinalIgnoreCase);
                var hasChildren = step.SubSteps != null && step.SubSteps.Count > 0;
                if (string.IsNullOrWhiteSpace(step.Command) && !isGroup && !hasChildren)
                {
                    diagnostics.Add(PlanCompileDiagnostic.Warning(
                        "EMPTY_STEP",
                        $"步骤 '{step.StepKey ?? step.StepName}' 无 Command 且无子步骤，运行时将空转。",
                        step.StepKey));
                }
            }
        }

        private static void ValidateHandlerResolution(
            IEnumerable<StepConfig> roots,
            PlanCompileContext context,
            PlanCompileOptions options,
            ICollection<PlanCompileDiagnostic> diagnostics)
        {
            var registry = context.HandlerRegistry;
            var lookup = context.HandlerLookup;
            if (registry == null && lookup == null)
            {
                return;
            }

            var registered = registry != null
                ? new HashSet<string>(registry.GetRegisteredCommands() ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var step in roots.SelectMany(r => StepKit.FlattenSteps(r)).Where(s => s.Enable))
            {
                if (string.IsNullOrWhiteSpace(step.Command))
                {
                    continue;
                }

                if (IsHandlerResolvable(step, lookup, registered))
                {
                    continue;
                }

                var message = $"命令 '{step.Command}' 未注册且无可用的 DSL 模板。";
                var code = "MISSING_HANDLER";
                if (options.StrictMissingHandlerCheck)
                {
                    diagnostics.Add(PlanCompileDiagnostic.Error(code, message, step.StepKey));
                }
                else
                {
                    diagnostics.Add(PlanCompileDiagnostic.Warning(
                        code,
                        message + " 运行时将走 GenericFallback。",
                        step.StepKey));
                }
            }
        }

        /// <summary>Strict 模式下 GenericFallback 不算可解析（对标 OpenTAP 必须有明确 Step 类型）。</summary>
        private static bool IsHandlerResolvable(StepConfig step, IStepHandlerLookup lookup, HashSet<string> registered)
        {
            if (registered != null && registered.Contains(step.Command))
            {
                return true;
            }

            if (lookup == null)
            {
                return false;
            }

            return lookup.TryGetHandler(step, out _) || lookup.TryGetTemplateHandler(step, out _);
        }

        private static void ValidateConditionSyntax(
            IEnumerable<StepConfig> roots,
            IWorkflowEvaluator evaluator,
            PlanCompileOptions options,
            ICollection<PlanCompileDiagnostic> diagnostics)
        {
            if (!options.ValidateConditionSyntax || evaluator == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in roots)
            {
                // 提取编译期已知变量表：WorkflowDefinition.Variables + step.Parameters
                var knownVariables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (step.Parameters != null)
                {
                    // 1. 提取 WorkflowDefinition.Variables
                    if (step.Parameters.TryGetValue("WorkflowDefinition", out var defObj))
                    {
                        ExtractVariablesFromWorkflowDefinition(defObj, knownVariables);
                    }

                    // 2. 提取 step.Parameters 的所有键（值可能为表达式，不提取值）
                    foreach (var kvp in step.Parameters)
                    {
                        if (!knownVariables.ContainsKey(kvp.Key))
                        {
                            knownVariables[kvp.Key] = null; // 键存在，值未知
                        }
                    }
                }

                foreach (var (stepKey, condition, isWaitUntilCondition) in ConditionExpressionCollector.CollectFromSteps(new[] { step }))
                {
                    // WaitUntil.Condition 属于执行期轮询条件，运行期才注入变量，跳过构建期语法检查
                    if (isWaitUntilCondition)
                    {
                        continue;
                    }

                    var dedupeKey = (stepKey ?? "") + "\0" + condition;
                    if (!seen.Add(dedupeKey))
                    {
                        continue;
                    }

                    if (!evaluator.TryValidateConditionSyntax(condition, knownVariables, out var error))
                    {
                        diagnostics.Add(PlanCompileDiagnostic.Error(
                            "INVALID_CONDITION",
                            $"条件表达式语法错误: {error}（表达式: {condition}）",
                            stepKey));
                    }
                }
            }
        }

        private static void ExtractVariablesFromWorkflowDefinition(object defObj, Dictionary<string, object> target)
        {
            if (defObj is IDictionary<string, object> defDict && defDict.TryGetValue("Variables", out var varsObj) && varsObj is IDictionary<string, object> varsDict)
            {
                foreach (var kvp in varsDict)
                {
                    if (!target.ContainsKey(kvp.Key))
                    {
                        target[kvp.Key] = kvp.Value;
                    }
                }
            }
            else if (defObj is JObject defJObj && defJObj["Variables"] is JToken varsToken && varsToken.Type == JTokenType.Object)
            {
                foreach (var prop in varsToken.Children<JProperty>())
                {
                    if (!target.ContainsKey(prop.Name))
                    {
                        target[prop.Name] = prop.Value.Type == JTokenType.Object ? prop.Value.ToString() : prop.Value.ToObject<object>();
                    }
                }
            }
        }

        private static void ApplyProfileRecursive(StepConfig step, IDictionary<string, string> profile)
        {
            if (step == null)
            {
                return;
            }

            step.BindProfile(profile);

            if (step.SubSteps == null)
            {
                return;
            }

            foreach (var sub in step.SubSteps)
            {
                ApplyProfileRecursive(sub, profile);
            }
        }

        private static void BridgeHandlerMetadataRecursive(StepConfig step, IStepHandlerRegistry registry)
        {
            if (step == null)
            {
                return;
            }

            if (!step.EvaluateResult.HasValue
                && registry != null
                && !string.IsNullOrEmpty(step.Command))
            {
                step.EvaluateResult = registry.GetEvaluateResult(step.Command);
            }

            if (step.SubSteps == null)
            {
                return;
            }

            foreach (var sub in step.SubSteps)
            {
                BridgeHandlerMetadataRecursive(sub, registry);
            }
        }

        internal static string ComputePlanHash(IReadOnlyList<StepConfig> sourceSteps)
        {
            var json = JsonConvert.SerializeObject(sourceSteps);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
