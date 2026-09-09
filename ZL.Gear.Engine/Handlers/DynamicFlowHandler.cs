using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Engine
{
    /*
{
  "StepName": "自动电流测试_LIN",
  "Command": "DynamicFlow", 
  "Description": "这是一个低代码生成的动态流程",
  "StepType": "Standard",
  "Parameters": {
    "WorkflowDefinition": {
      "Version": "1.0",
      "Finalizers": [
        {
          "Id": "final_1",
          "Type": "Action",
          "ActionKey": "StopLinCmd",
          "Description": "关闭LIN",
          "Args": {} 
        },
        {
          "Id": "final_2",
          "Type": "Action",
          "ActionKey": "StopPlcRelay",
          "Description": "停止继电器",
          "Args": {}
        }
      ],
      "Sequence": [
        {
          "Id": "node_1",
          "Type": "Action",
          "ActionKey": "SetupPlcRelay",
          "Description": "切换继电器",
          "Args": {
            "plc.id": "502",
            "plc.value": true
          }
        },
        {
          "Id": "node_2",
          "Type": "Delay",
          "Description": "等待稳定",
          "DelayMs": 500
        },
        {
          "Id": "node_3",
          "Type": "Action",
          "ActionKey": "StartLinCmd",
          "Description": "发报文",
          "Args": {
             "BaudRate": 19200 
          }
        },
        {
          "Id": "node_4",
          "Type": "Measure",
          "ActionKey": "MeasureCurrent",
          "Description": "读电流",
          "Args": {
             "Retries": 3
          }
        }
      ]
    }
  }
}
     */
    public class DynamicFlowHandler : IStepHandler
    {
        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            // 1. 解析 JSON 定义（WorkflowTimeoutMs 非法时清洗为缺省，不因脏超时字段整单失败）
            DynamicWorkflowConfig flowConfig = null;
            if (step.Parameters != null && step.Parameters.TryGetValue("WorkflowDefinition", out var defObj))
            {
                if (!TryParseFlowConfig(defObj, out flowConfig, out var parseError))
                {
                    return ExecutionResult.Failed($"工作流定义解析失败: {parseError}");
                }
            }
            else if (step.Parameters != null && (step.Parameters.ContainsKey("Sequence") || step.Parameters.ContainsKey("sequence")))
            {
                if (!TryParseFlowConfig(step.Parameters, out flowConfig, out var parseError))
                {
                    return ExecutionResult.Failed($"工作流配置反序列化失败: {parseError}");
                }
            }
            else
            {
                return ExecutionResult.Failed("缺少 WorkflowDefinition 参数或完整的流程配置");
            }

            // 1.5 创建“流程级”子作用域，隔离外部变量污染
            var flowVariables = context.Variables.CreateChildScope();
            
            // 重要：将当前步骤的 Parameters 注入作用域，使 DSL 可以直接引用
            if (step.Parameters != null)
            {
                foreach (var kvp in step.Parameters)
                    flowVariables.Set(kvp.Key, kvp.Value);
            }

            if (flowConfig.Variables != null)
            {
                foreach (var kvp in flowConfig.Variables)
                {
                    flowVariables.Set(kvp.Key, kvp.Value);
                }
            }

            // 2. 启动 MicroWorkflow（可选流程级超时：缺省/非法 → 0 → 不启用，不报错）
            var flowContext = context.CreateChildContext(step, flowVariables);
            var workflowTimeoutMs = ResolveWorkflowTimeoutMs(flowConfig, step, msg =>
            {
                try { context.Log(msg); } catch { /* 日志失败不影响执行 */ }
            });
            await using (var workflow = MicroWorkflow.Start(step, flowContext, workflowTimeoutMs).AsMaster())
            {
                // 3. 注册清理动作 (Finalizers)
                if (flowConfig.Finalizers != null)
                {
                    foreach (var node in flowConfig.Finalizers)
                    {
                        workflow.Finally(node.Description, WrapActionWithArgs(node, flowContext.ActionResolver));
                    }
                }

                // 4. 递归构建主序列
                if (flowConfig.Sequence != null)
                {
                    foreach (var node in flowConfig.Sequence)
                    {
                        BuildNode(workflow, node, flowContext);
                    }
                }

                return await workflow.GetResultAsync();
            }
        }

        /// <summary>
        /// 【核心递归构建器】将 JSON 节点转换为 MicroWorkflow 调用
        /// </summary>
        private void BuildNode(MicroWorkflow flow, WorkflowNode node, StepContext ctx)
        {
            // --- [逻辑大脑：分支检查] ---
            // 非 WaitUntil：节点 Condition 为构建期守卫（仅初始 Variables），不在执行期重评。
            // 依赖前序 OutputKey 的运行时条件 → 使用 Assert Args.Condition；轮询到位 → WaitUntil。
            if (!string.IsNullOrEmpty(node.Condition) && node.Type != WorkflowNodeType.WaitUntil)
            {
                if (!ctx.Evaluate(node.Condition))
                {
                    return;
                }
            }

            switch (node.Type)
            {
                case WorkflowNodeType.Delay:
                    flow.Delay(node.DelayMs ?? 1000, node.Description);
                    break;

                case WorkflowNodeType.Action:
                    // 包装参数并添加步骤
                    flow.Then(node.Description, WrapActionWithArgs(node, ctx.ActionResolver));
                    break;

                case WorkflowNodeType.Measure:
                    flow.ThenMeasure(node.Description, WrapMeasurementWithArgs(node, ctx.ActionResolver));
                    break;

                case WorkflowNodeType.Sequence:
                case WorkflowNodeType.Group:
                    // Group 是 DSL 视觉/逻辑分组别名，运行时与 Sequence 相同：扁平展开 Children
                    if (node.Children != null)
                    {
                        foreach (var child in node.Children) BuildNode(flow, child, ctx);
                    }
                    break;

                case WorkflowNodeType.Retry:
                    // Retry 也是包裹一个子动作（通常 Children[0] 或直接指定 ActionKey）
                    // 这里假设 Retry 节点本身定义了 ActionKey
                    if (!string.IsNullOrEmpty(node.ActionKey))
                    {
                        var action = WrapActionWithArgs(node, ctx.ActionResolver);
                        flow.Retry(node.Description, action, node.RetryCount ?? 3, node.IntervalMs ?? 500);
                    }
                    break;

                case WorkflowNodeType.WaitUntil:
                    // 策略 1: 轮询一个动作
                    if (!string.IsNullOrEmpty(node.ActionKey))
                    {
                        var conditionAction = WrapActionWithArgs(node, ctx.ActionResolver);
                        flow.WaitUntil(node.Description, async (s, c) =>
                        {
                            var res = await conditionAction(s, c);
                            return res.Success;
                        }, node.TimeoutMs ?? 5000, node.IntervalMs ?? 200);
                    }
                    // 策略 2: 轮询一个表达式 (New!)
                    else if (!string.IsNullOrEmpty(node.Condition))
                    {
                        flow.WaitUntil(node.Description, (s, c) =>
                        {
                            bool ok = c.Evaluate(node.Condition);
                            return Task.FromResult(ok);
                        }, node.TimeoutMs ?? 5000, node.IntervalMs ?? 200);
                    }
                    else
                    {
                         ctx.Log($"[警告] WaitUntil 节点既没有 ActionKey 也没有 Condition，命令将被跳过。");
                    }
                    break;

                case WorkflowNodeType.Parallel:
                    // Parallel 只承载「无测量载荷」的动作分支（或 Sequence/Group 容器）。
                    // 注意：容器内 Measure 的结果在子 MicroWorkflow 内裁决，默认不汇入父 _measurements；
                    // 跨分支数据请用 OutputKey→Variables；父步骤 Verify 请用 ParallelMeasure 或显式汇总。
                    if (node.Children != null && node.Children.Count > 0)
                    {
                        if (TryGetIllegalParallelChild(node.Children, ctx.ActionResolver, out var illegalReason))
                        {
                            flow.Then(node.Description ?? "Parallel", (s, c) =>
                                Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed(illegalReason)));
                            break;
                        }

                        var parallelTasks = node.Children.Select(child => (child.Description, WrapActionWithArgs(child, ctx.ActionResolver))).ToArray();
                        flow.Parallel(node.Description, parallelTasks);
                    }
                    break;

                case WorkflowNodeType.ParallelMeasure:
                    // 并行测量：子节点应为 Measure；成功结果写入 MicroWorkflow 测量集合。
                    if (node.Children != null && node.Children.Count > 0)
                    {
                        var illegal = node.Children.Find(c => c != null && c.Type != WorkflowNodeType.Measure);
                        if (illegal != null)
                        {
                            var badName = string.IsNullOrWhiteSpace(illegal.Description) ? illegal.ActionKey : illegal.Description;
                            flow.Then(node.Description ?? "ParallelMeasure", (s, c) =>
                                Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed(
                                    $"配置错误：ParallelMeasure 子节点必须为 Measure（发现 '{badName}' Type={illegal.Type}）。")));
                            break;
                        }

                        var parallelTasks = node.Children.Select(child => (child.Description, WrapMeasurementWithArgs(child, ctx.ActionResolver))).ToArray();
                        flow.ParallelMeasure(node.Description, parallelTasks);
                    }
                    break;
            }

            // --- [节点级清理] ---
            if (node.Finally != null && node.Finally.Count > 0)
            {
                foreach (var finNode in node.Finally)
                {
                    flow.Finally(finNode.Description, WrapActionWithArgs(finNode, ctx.ActionResolver));
                }
            }
        }

        /// <summary>
        /// 检查 Parallel 子节点是否非法：
        /// 1) 显式 Measure；
        /// 2) ActionKey 可解析为测量（含 Read/Query 等「Action+Measurement 双注册」——走 Action 会丢测量数据）。
        /// 容器子节点（Sequence/Group，无 ActionKey）允许，由其内部自行编排 Measure。
        /// </summary>
        private static bool TryGetIllegalParallelChild(
            IReadOnlyList<WorkflowNode> children,
            IActionResolver resolver,
            out string reason)
        {
            reason = null;
            if (children == null) return false;

            foreach (var child in children)
            {
                if (child == null) continue;

                if (child.Type == WorkflowNodeType.Measure)
                {
                    var badName = string.IsNullOrWhiteSpace(child.Description) ? child.ActionKey : child.Description;
                    reason = $"配置错误：Parallel 不能包含 Measure 子节点（'{badName}'）。请改用 ParallelMeasure。";
                    return true;
                }

                // Sequence/Group 等容器无 ActionKey，允许作为 Parallel 分支（内部可再挂 Measure）
                if (string.IsNullOrWhiteSpace(child.ActionKey) || resolver == null) continue;

                MeasurementActionDelegate measure = null;
                try
                {
                    measure = resolver.ResolveMeasurement(child.ActionKey);
                }
                catch (KeyNotFoundException)
                {
                    measure = null;
                }
                catch
                {
                    // 解析器异常留给后续执行路径
                    continue;
                }

                // 只要能解析为测量，禁止直接挂在 Parallel 下（双注册时 Action 路径会丢 Measurement）
                if (measure != null)
                {
                    reason =
                        $"配置错误：Parallel 子节点 '{child.ActionKey}' 注册为测量动作（或 Action/Measurement 双注册）。" +
                        "放入 Parallel 会丢失测量数据，请改用 ParallelMeasure，或放入 Sequence 容器内编排。";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 【参数注入】创建闭包，在执行具体动作前动态合并 JSON 中的 Args 到 StepConfig。
        /// </summary>
        private ActionDelegate WrapActionWithArgs(WorkflowNode node, IActionResolver resolver)
        {
            ActionDelegate targetAction = null;
            try
            {
                targetAction = resolver.ResolveAction(node.ActionKey);
            }
            catch (KeyNotFoundException)
            {
                targetAction = null;
            }

            // 策略增强：如果找不到普通动作，尝试查找同名的测量动作并包装
            if (targetAction == null)
            {
                MeasurementActionDelegate targetMeasure = null;
                try
                {
                    targetMeasure = resolver.ResolveMeasurement(node.ActionKey);
                }
                catch (KeyNotFoundException)
                {
                    targetMeasure = null;
                }

                if (targetMeasure != null)
                {
                    targetAction = async (s, c) => {
                        var m = await targetMeasure(s, c).ConfigureAwait(false);
                        return m.Success ? ExecutionResult.Succeeded(m.Message) : ExecutionResult.Failed(m.Message);
                    };
                }
                // 策略增强 2：如果是复合节点（如 Sequence/Group），支持递归执行
                else if (node.Children != null && node.Children.Count > 0)
                {
                    targetAction = async (s, c) => {
                        // 创建一个临时配置，将当前节点的子节点作为序列传入
                        var subConfig = (StepConfig)s.Clone();
                        subConfig.Parameters["Sequence"] = node.Children;
                        // 递归调用当前的 Handler
                        return await this.ExecuteAsync(subConfig, c).ConfigureAwait(false);
                    };
                }
            }

            return async (originalStep, originalCtx) =>
            {
                if (targetAction == null) return ExecutionResult.Failed($"无法解析动作: {node.ActionKey}");

                // 1. 创建节点的"执行级"子作用域
                var nodeVariables = originalCtx.Variables.CreateChildScope();

                // 2. 执行逻辑元数据：Inputs (显式映射)
                if (node.Inputs != null)
                {
                    foreach (var input in node.Inputs)
                    {
                        var dynamicValue = originalCtx.EvaluateValue(input.Value);
                        nodeVariables.Set(input.Key, dynamicValue);
                    }
                }

                // 3. 构建临时运行时配置
                var runtimeConfig = (StepConfig)originalStep.Clone();
                runtimeConfig.Command = node.ActionKey;
                
                // 3.1 处理主设备 Target
                if (!string.IsNullOrEmpty(node.Target))
                {
                    runtimeConfig.Target = originalCtx.ResolveText(node.Target);
                }

                // 3.2 【核心修复】处理 AdditionalTargets 多设备调用
                // 将 AdditionalTargets 中的所有设备解析并填充到 TargetDict
                if (originalStep.AdditionalTargets != null && originalStep.AdditionalTargets.Count > 0)
                {
                    foreach (var role in originalStep.AdditionalTargets)
                    {
                        // 获取设备ID（支持 Profile 映射和直接物理ID模式）
                        var deviceId = runtimeConfig.GetTargetId(role);
                        
                        if (!string.IsNullOrEmpty(deviceId))
                        {
                            runtimeConfig.TargetDict[role] = deviceId;
                            
                            // 尝试租赁额外设备（如果尚未租赁）
                            try
                            {
                                var device = originalCtx.GetDevice<IDevice>(deviceId);
                                
                                // 由于 ActiveDevices 是只读的，我们需要使用反射或包装类来添加
                                // 这里使用一种简单的方式：将设备临时存储在 TargetDict 中
                                runtimeConfig.TargetDict[$"_device_{role}"] = device;
                            }
                            catch (Exception ex)
                            {
                                originalCtx.Log($"[DynamicFlow] 访问额外设备 {role}({deviceId}) 失败: {ex.Message}");
                            }
                        }
                    }
                }

                // 4. 合并静态参数
                foreach (var kvp in node.Args)
                {
                    runtimeConfig.Parameters[kvp.Key] = originalCtx.EvaluateValue(kvp.Value);
                }

                // 5. 创建带隔离作用域的子上下文
                var nodeCtx = originalCtx.CreateChildContext(runtimeConfig, nodeVariables);

                // 6. 执行原子动作
                var result = await targetAction(runtimeConfig, nodeCtx);

                // 7. 执行逻辑元数据：Outputs (结果回写)
                if (result.Success)
                {
                    // 旧逻辑兼容: OutputKey
                    if (!string.IsNullOrEmpty(node.OutputKey))
                        originalCtx.Variables.Set(node.OutputKey, result.GetValueAsObject());

                    // 新逻辑: 显式映射
                    if (node.Outputs != null)
                    {
                        foreach (var output in node.Outputs)
                        {
                            originalCtx.Variables.Set(output.Value, result.GetValueAsObject());
                        }
                    }
                }

                return result;
            };
        }

        /// <summary>
        /// 针对 Measure 的参数包装版本
        /// </summary>
        private MeasurementActionDelegate WrapMeasurementWithArgs(WorkflowNode node, IActionResolver resolver)
        {
            MeasurementActionDelegate targetAction = null;
            try
            {
                targetAction = resolver.ResolveMeasurement(node.ActionKey);
            }
            catch (KeyNotFoundException)
            {
                targetAction = null;
            }

            return async (originalStep, originalCtx) =>
            {
                if (targetAction == null)
                {
                    return Measurement.Failed(node.ActionKey ?? "unknown", $"无法解析测量动作: {node.ActionKey}");
                }

                // 1. 作用域隔离
                var nodeVariables = originalCtx.Variables.CreateChildScope();

                // 2. Inputs 映射
                if (node.Inputs != null)
                {
                    foreach (var input in node.Inputs)
                    {
                        var dynamicValue = originalCtx.EvaluateValue(input.Value);
                        nodeVariables.Set(input.Key, dynamicValue);
                    }
                }

                // 3. 配置注入
                var runtimeConfig = (StepConfig)originalStep.Clone();
                runtimeConfig.Command = node.ActionKey;

                if (!string.IsNullOrEmpty(node.Target))
                {
                    runtimeConfig.Target = originalCtx.ResolveText(node.Target);
                }

                foreach (var kvp in node.Args)
                {
                    runtimeConfig.Parameters[kvp.Key] = originalCtx.EvaluateValue(kvp.Value);
                }

                // 4. 执行
                var nodeCtx = originalCtx.CreateChildContext(runtimeConfig, nodeVariables);
                var result = await targetAction(runtimeConfig, nodeCtx).ConfigureAwait(false);

                // 5. Outputs 回写 (测量结果回写)
                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(node.OutputKey))
                    {
                        originalCtx.Variables.Set(node.OutputKey, result.Value);
                        originalCtx.Log($"[DynamicFlow] Variables 回写: {node.OutputKey} = {result.Value}");
                    }

                    if (node.Outputs != null)
                    {
                        foreach (var output in node.Outputs)
                        {
                            originalCtx.Variables.Set(output.Value, result.Value);
                        }
                    }
                }

                return result;
            };
        }

        /// <summary>
        /// 解析流程配置：先清洗非法 WorkflowTimeoutMs，再反序列化。其它字段仍按原规则失败。
        /// </summary>
        private static bool TryParseFlowConfig(object raw, out DynamicWorkflowConfig config, out string error)
        {
            config = null;
            error = null;
            try
            {
                JObject jobj;
                if (raw is string s)
                {
                    jobj = JObject.Parse(s);
                }
                else if (raw is JObject jo)
                {
                    jobj = (JObject)jo.DeepClone();
                }
                else
                {
                    jobj = JObject.FromObject(raw);
                }

                SanitizeWorkflowTimeoutMsToken(jobj);
                config = jobj.ToObject<DynamicWorkflowConfig>() ?? new DynamicWorkflowConfig();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 非法/非正 WorkflowTimeoutMs 从 JSON 移除，避免 int? 反序列化抛错；合法正整数保留或规范化。
        /// </summary>
        private static void SanitizeWorkflowTimeoutMsToken(JObject jobj)
        {
            if (jobj == null)
            {
                return;
            }

            JToken token = jobj["WorkflowTimeoutMs"] ?? jobj["workflowTimeoutMs"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }

            if (TryCoercePositiveMs(token.Type == JTokenType.String ? (object)token.Value<string>() : token.ToObject<object>(), out var ms))
            {
                jobj["WorkflowTimeoutMs"] = ms;
                jobj.Remove("workflowTimeoutMs");
                return;
            }

            jobj.Remove("WorkflowTimeoutMs");
            jobj.Remove("workflowTimeoutMs");
        }

        /// <summary>
        /// 解析流程级超时：缺省 / null / ≤0 / 无法解析 → 返回 0（不启用，不抛错）。
        /// 优先级：WorkflowDefinition.WorkflowTimeoutMs → Parameters.WorkflowTimeoutMs。
        /// </summary>
        private static int ResolveWorkflowTimeoutMs(
            DynamicWorkflowConfig flowConfig,
            StepConfig step,
            Action<string> log)
        {
            if (TryCoercePositiveMs(flowConfig?.WorkflowTimeoutMs, out var fromCfg))
            {
                return fromCfg;
            }

            if (step?.Parameters == null)
            {
                return 0;
            }

            object raw = null;
            if (!step.Parameters.TryGetValue("WorkflowTimeoutMs", out raw)
                && !step.Parameters.TryGetValue("workflowTimeoutMs", out raw))
            {
                return 0;
            }

            if (TryCoercePositiveMs(raw, out var fromParam))
            {
                return fromParam;
            }

            // 键存在但值非法：忽略，保持无超时（鲁棒，不因配置脏数据失败）
            log?.Invoke("[DynamicFlow] WorkflowTimeoutMs 无效或非正，已忽略（不启用流程级超时）。");
            return 0;
        }

        /// <summary>
        /// 将任意对象尝试转为正整数毫秒；失败返回 false。
        /// </summary>
        private static bool TryCoercePositiveMs(object value, out int ms)
        {
            ms = 0;
            if (value == null)
            {
                return false;
            }

            if (value is int i)
            {
                if (i > 0) { ms = i; return true; }
                return false;
            }

            if (value is long l)
            {
                if (l > 0 && l <= int.MaxValue) { ms = (int)l; return true; }
                return false;
            }

            if (value is double d)
            {
                if (d > 0 && d <= int.MaxValue) { ms = (int)d; return true; }
                return false;
            }

            if (int.TryParse(Convert.ToString(value), out var parsed) && parsed > 0)
            {
                ms = parsed;
                return true;
            }

            return false;
        }
    }
}
