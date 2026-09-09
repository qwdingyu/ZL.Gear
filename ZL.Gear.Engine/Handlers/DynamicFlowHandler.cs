using Newtonsoft.Json;
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
            // 1. 解析 JSON 定义
            DynamicWorkflowConfig flowConfig = null;
            if (step.Parameters != null && step.Parameters.TryGetValue("WorkflowDefinition", out var defObj))
            {
                try
                {
                    // 兼容 string、JObject 或 Dictionary/object
                    string json = defObj is string s ? s : JsonConvert.SerializeObject(defObj);
                    flowConfig = JsonConvert.DeserializeObject<DynamicWorkflowConfig>(json);
                }
                catch (Exception ex)
                {
                    return ExecutionResult.Failed($"工作流定义解析失败: {ex.Message}");
                }
            }
            else if (step.Parameters != null && (step.Parameters.ContainsKey("Sequence") || step.Parameters.ContainsKey("sequence")))
            {
                try
                {
                    string json = JsonConvert.SerializeObject(step.Parameters);
                    flowConfig = JsonConvert.DeserializeObject<DynamicWorkflowConfig>(json);
                }
                catch (Exception ex)
                {
                    return ExecutionResult.Failed($"工作流配置反序列化失败: {ex.Message}");
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

            // 2. 启动 MicroWorkflow (传入隔离的作用域)
            var flowContext = context.CreateChildContext(step, flowVariables);
            await using (var workflow = MicroWorkflow.Start(step, flowContext).AsMaster())
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
                    if (node.Children != null && node.Children.Count > 0)
                    {
                        var parallelTasks = node.Children.Select(child => (child.Description, WrapActionWithArgs(child, ctx.ActionResolver))).ToArray();
                        flow.Parallel(node.Description, parallelTasks);
                    }
                    break;

                case WorkflowNodeType.ParallelMeasure:
                    if (node.Children != null && node.Children.Count > 0)
                    {
                        var parallelTasks = node.Children.Select(child => (child.Description, WrapMeasurementWithArgs(child, ctx.ActionResolver))).ToArray();
                        flow.ParallelMeasure(node.Description, parallelTasks);
                    }
                    break;

                case WorkflowNodeType.Group:
                    if (node.Children != null)
                    {
                        foreach (var child in node.Children) BuildNode(flow, child, ctx);
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
        /// 【参数注入黑科技】
        /// 创建一个闭包，在执行具体动作前，动态合并 JSON 中的 Args 到 StepConfig 中
        /// </summary>
        private ActionDelegate WrapActionWithArgs(WorkflowNode node, IActionResolver resolver)
        {
            var targetAction = resolver.ResolveAction(node.ActionKey);

            // 策略增强：如果找不到普通动作，尝试查找同名的测量动作并包装
            if (targetAction == null)
            {
                var targetMeasure = resolver.ResolveMeasurement(node.ActionKey);
                if (targetMeasure != null)
                {
                    targetAction = async (s, c) => {
                        var m = await targetMeasure(s, c);
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
                        return await this.ExecuteAsync(subConfig, c);
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
            var targetAction = resolver.ResolveMeasurement(node.ActionKey);

            return async (originalStep, originalCtx) =>
            {
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
                var result = await targetAction(runtimeConfig, nodeCtx);

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
    }
}
