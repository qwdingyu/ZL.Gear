using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Metadata;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Utils;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Core.Services
{
    public class MetadataDiscoveryService
    {
        // 缓存元数据，避免频繁反射
        private List<ActionMetadataDto> _cachedMetadata;
        /// <summary>
        /// 获取最终元数据（支持热重载补丁）
        /// </summary>
        public List<ActionMetadataDto> GetFinalMetadata(string patchFilePath, params Assembly[] assemblies)
        {
            // 1. 扫描代码 (基准)
            var codeMetadata = DiscoverAll(assemblies);

            // 2. 应用补丁 (皮肉)
            if (File.Exists(patchFilePath))
            {
                try
                {
                    var json = File.ReadAllText(patchFilePath);
                    var patchMetadata = JsonConvert.DeserializeObject<List<ActionMetadataDto>>(json);
                    MergeMetadata(codeMetadata, patchMetadata);
                }
                catch (Exception ex)
                {
                    LogKit.Error($"[Metadata] 加载补丁失败: {ex.Message}");
                }
            }

            _cachedMetadata = codeMetadata; // 更新缓存
            return codeMetadata;
        }
        /// <summary>
        /// 扫描程序集，生成完整的元数据列表
        /// </summary>
        public List<ActionMetadataDto> DiscoverAll(Assembly[] assemblies)
        {
            var result = new List<ActionMetadataDto>();

            foreach (var assembly in assemblies)
            {
                // 1. 扫描 StepHandlers (类) -> Usage: StepHandler
                var handlerTypes = assembly.GetTypes()
                    .Where(t => typeof(IStepHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in handlerTypes)
                {
                    // 注意：这里的 Attribute 类型要用修正后的 ConfigParameterAttribute
                    var dto = ParseFromAttribute(type, type.GetCustomAttribute<WorkflowActionAttribute>(), type.GetCustomAttributes<ConfigParameterAttribute>());
                    if (dto != null)
                    {
                        if (string.IsNullOrEmpty(dto.Command))
                            dto.Command = type.Name.EndsWith("Handler") ? type.Name.Substring(0, type.Name.Length - 7) : type.Name;

                        dto.Usage = "StepHandler"; // 标记为顶层步骤
                        result.Add(dto);
                    }
                }

                // 2. 扫描 Providers (方法) -> Usage: AtomicAction
                var providers = assembly.GetTypes()
                    .Where(t => typeof(IWorkflowActionProvider).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var providerType in providers)
                {
                    var methods = providerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(m => m.GetCustomAttributes<WorkflowActionAttribute>().Any());

                    foreach (var method in methods)
                    {
                        var dto = ParseFromAttribute(method, method.GetCustomAttribute<WorkflowActionAttribute>(), method.GetCustomAttributes<ConfigParameterAttribute>());
                        if (dto != null)
                        {
                            dto.Usage = "AtomicAction"; // 标记为原子动作
                            result.Add(dto);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// [核心升级] 验证配置有效性 (支持递归验证 DynamicWorkflow)
        /// </summary>
        public (bool IsValid, string Message) ValidateConfig(StepConfig config, List<ActionMetadataDto> metadataList = null)
        {
            var metaList = metadataList ?? _cachedMetadata ?? new List<ActionMetadataDto>();

            // 1. 如果是 DynamicFlow，进行特殊递归校验
            if (config.Command == "DynamicFlow")
            {
                return ValidateDynamicFlow(config, metaList);
            }

            // 2. 普通 Step 的校验
            var metadata = metaList.FirstOrDefault(m => m.Command.Equals(config.Command, StringComparison.OrdinalIgnoreCase));
            if (metadata == null) return (true, ""); // 未知命令跳过校验

            return ValidateParameters(config.StepName, config.Command, config.Parameters, metadata);
        }

        /// <summary>
        /// 递归校验 DynamicWorkflow 内部的所有节点
        /// </summary>
        private (bool IsValid, string Message) ValidateDynamicFlow(StepConfig config, List<ActionMetadataDto> metaList)
        {
            if (config.Parameters == null || !config.Parameters.TryGetValue("WorkflowDefinition", out var defObj))
                return (false, $"步骤 '{config.StepName}' 是动态流程，但缺少 WorkflowDefinition 参数");

            try
            {
                var json = defObj is string s ? s : JsonConvert.SerializeObject(defObj);
                var flowConfig = JsonConvert.DeserializeObject<DynamicWorkflowConfig>(json);

                // 递归校验序列
                if (flowConfig.Sequence != null)
                {
                    foreach (var node in flowConfig.Sequence)
                    {
                        var res = ValidateNodeRecursively(node, metaList, config.StepName);
                        if (!res.IsValid) return res;
                    }
                }
                // 递归校验 Finally
                if (flowConfig.Finalizers != null)
                {
                    foreach (var node in flowConfig.Finalizers)
                    {
                        var res = ValidateNodeRecursively(node, metaList, config.StepName);
                        if (!res.IsValid) return res;
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"步骤 '{config.StepName}' 的 WorkflowDefinition 格式错误: {ex.Message}");
            }

            return (true, "");
        }

        private (bool IsValid, string Message) ValidateNodeRecursively(WorkflowNode node, List<ActionMetadataDto> metaList, string parentStepName)
        {
            // 1. 如果是 Action 或 Measure，需要校验参数
            if (node.Type == WorkflowNodeType.Action || node.Type == WorkflowNodeType.Measure)
            {
                var metadata = metaList.FirstOrDefault(m => m.Command.Equals(node.ActionKey, StringComparison.OrdinalIgnoreCase));
                if (metadata != null) // 如果找到了元数据，就必须通过校验
                {
                    var res = ValidateParameters($"{parentStepName}->{node.Description}", node.ActionKey, node.Args, metadata);
                    if (!res.IsValid) return res;
                }
            }

            // 2. 递归校验子节点 (针对 Parallel, Group, Retry 等容器)
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    var res = ValidateNodeRecursively(child, metaList, parentStepName);
                    if (!res.IsValid) return res;
                }
            }
            return (true, "");
        }

        private (bool IsValid, string Message) ValidateParameters(string contextName, string command, Dictionary<string, object> args, ActionMetadataDto metadata)
        {
            var currentArgs = args ?? new Dictionary<string, object>();

            foreach (var paramDef in metadata.Parameters)
            {
                if (paramDef.IsRequired && !currentArgs.ContainsKey(paramDef.Key))
                {
                    // 检查是否有默认值，如果有默认值，则视为通过（运行时会补全）
                    if (paramDef.DefaultValue == null)
                    {
                        return (false, $"[{contextName}] 调用 '{command}' 时缺少必填参数: {paramDef.Key} ({paramDef.DisplayName})");
                    }
                }
            }
            return (true, "");
        }
        private ActionMetadataDto ParseFromAttribute(MemberInfo member, WorkflowActionAttribute actionAttr, IEnumerable<ConfigParameterAttribute> paramAttrs)
        {
            if (actionAttr == null && !paramAttrs.Any()) return null;

            var dto = new ActionMetadataDto
            {
                Command = actionAttr?.Name,
                Description = actionAttr?.Description ?? member.Name,
                Group = actionAttr?.Group ?? "Default",
                ExposedProperties = actionAttr?.ExposedProps?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>()
            };

            foreach (var p in paramAttrs)
            {
                dto.Parameters.Add(new ActionParameterDto
                {
                    Key = p.Key,
                    DisplayName = p.DisplayName,
                    Editor = p.Editor,
                    DataType = p.DataType,
                    DefaultValue = p.DefaultValue,
                    Unit = p.Unit,
                    Increment = p.Increment > 0 ? p.Increment : (double?)null,
                    RequiredLevel = p.RequiredLevel,
                    Options = p.Options,
                    IsRequired = p.IsRequired
                });
            }
            return dto;
        }
        private void MergeMetadata(List<ActionMetadataDto> baseList, List<ActionMetadataDto> patchList)
        {
            if (patchList == null) return;

            foreach (var baseItem in baseList)
            {
                // 查找补丁中有没有对应的 Command
                var patchItem = patchList.FirstOrDefault(p => p.Command == baseItem.Command);
                if (patchItem == null) continue;

                // --- 覆盖 Step 级别的软属性 ---
                if (!string.IsNullOrEmpty(patchItem.Description)) baseItem.Description = patchItem.Description;
                if (!string.IsNullOrEmpty(patchItem.Group)) baseItem.Group = patchItem.Group;

                // --- 覆盖 Parameter 级别的软属性 ---
                foreach (var baseParam in baseItem.Parameters)
                {
                    var patchParam = patchItem.Parameters.FirstOrDefault(p => p.Key == baseParam.Key);
                    if (patchParam == null) continue;

                    // 允许修改显示名称 (例如：代码里叫 "上限"，现场想改成 "最大电流")
                    if (!string.IsNullOrEmpty(patchParam.DisplayName)) baseParam.DisplayName = patchParam.DisplayName;

                    // 允许修改默认值 (这是最常用的)
                    if (patchParam.DefaultValue != null) baseParam.DefaultValue = patchParam.DefaultValue;

                    // 允许修改单位
                    if (!string.IsNullOrEmpty(patchParam.Unit)) baseParam.Unit = patchParam.Unit;

                    // 允许修改编辑器类型 (例如：代码默认String，现场想改成 TextArea)
                    if (!string.IsNullOrEmpty(patchParam.Editor)) baseParam.Editor = patchParam.Editor;

                    // 允许修改权限等级
                    if (!string.IsNullOrEmpty(patchParam.RequiredLevel)) baseParam.RequiredLevel = patchParam.RequiredLevel;

                    // 注意：Key 和 DataType 不允许覆盖，因为这会破坏代码逻辑
                }
            }
        }
    }
}
