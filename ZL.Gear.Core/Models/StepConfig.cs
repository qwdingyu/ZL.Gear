using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 步骤配置实体类 (DTO)
    /// 仅包含数据定义，复杂的业务逻辑和更新逻辑已剥离至 StepKit。
    /// </summary>
    public class StepConfig : ICloneable
    {
        public string Id { get; set; }
        
        /// <summary>
        /// 步骤的业务唯一标识符
        /// </summary>
        public string StepKey { get; set; }
        public string StepName { get; set; }
        public string Description { get; set; }

        [DefaultValue(StepExecutionType.Execute)]
        public StepExecutionType ExecutionType { get; set; } = StepExecutionType.Execute;

        /// <summary>
        /// 子步骤执行模式
        /// </summary>
        public StepExecutionMode ExecutionMode { get; set; } = StepExecutionMode.Serial;

        /// <summary>
        /// 步骤类型：Group 或 Standard
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [DefaultValue("Standard")]
        public string StepType { get; set; } = "Standard";

        /// <summary>
        /// 目标设备名称
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// 执行命令
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// 获取或设置一个值，指示该步骤的结果是否已由 Handler 自行判定。
        /// 如果为 true，SequenceExecutor 将跳过 ResultEvaluator，直接采用 Handler 的 Success/Failure 作为最终结果。
        /// 默认值为 null，表示使用全局默认策略。
        /// </summary>
        public bool? EvaluateResult { get; set; }

        /// <summary>
        /// 额外依赖的设备
        /// </summary>
        public List<string> AdditionalTargets { get; set; } = new List<string>();

        /// <summary>
        /// 设备映射字典 (运行时解析，线程安全)
        /// </summary>
        [JsonIgnore]
        public IDictionary<string, object> TargetDict { get; set; } = new ConcurrentDictionary<string, object>();

        /// <summary>
        /// 配置参数字典
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// 期望结果规范列表
        /// </summary>
        public List<ExpectedSpec> ExpectedResults { get; set; } = new List<ExpectedSpec>();

        [JsonIgnore]
        public ExpectedSpec ExpectedResult
        {
            get => ExpectedResults?.FirstOrDefault();
            set
            {
                ExpectedResults.Clear();
                if (value != null) ExpectedResults.Add(value);
            }
        }

        public int TimeoutMs { get; set; } = 30000;
        public bool Enable { get; set; } = true;

        public string PromptString1 { get; set; }
        public string PromptString2 { get; set; }
        
        /// <summary>
        /// 作业指示图片路径
        /// </summary>
        public string PicturePath { get; set; }

        public bool StopByFail { get; set; } = false;

        // --- 常用参数快捷访问 (语义化扩展，减少 Hardcoding) ---

        [JsonIgnore]
        public bool IsMaster => GetParam("IsMaster", false);

        [JsonIgnore]
        public int DurationMs => GetParam("DurationMs", 5000);

        [JsonIgnore]
        public string DependsOn => GetParam("DependsOn", "");

        [JsonIgnore]
        public string MeasurementKey => GetParam("MeasurementKey", StepName);

        [JsonIgnore]
        public object LCL => GetParam<object>("LCL", "");

        [JsonIgnore]
        public object UCL => GetParam<object>("UCL", "");

        [JsonIgnore]
        public object Offset => GetParam<object>("Offset", "");

        [JsonIgnore]
        public object Unit => GetParam<object>("Unit", "");

        private T GetParam<T>(string key, T defaultValue)
        {
            if (Parameters != null && Parameters.TryGetValue(key, out var val))
            {
                try { return (T)Convert.ChangeType(val, typeof(T)); } catch { }
            }
            return defaultValue;
        }

        // --- 结构定义 ---

        public int StartDelayMs { get; set; } = 0;
        public List<StepConfig> SubSteps { get; set; } = new List<StepConfig>();
        public bool CanSingleTest { get; set; } = true;

        public override string ToString() => StepName ?? base.ToString();

        /// <summary>
        /// 显式实现 ICloneable，实际逻辑交由 StepKit 处理。
        /// </summary>
        /// <summary>
        /// 绑定设备配置文件，将逻辑名称（如"PLC"、"MainPower"）解析为物理ID（如"plc_1"、"ktdy_1"）并填充到 TargetDict。
        /// </summary>
        /// <param name="profile">设备配置文件 (DeviceProfile)</param>
        public void BindProfile(IDictionary<string, string> profile)
        {
            if (profile == null) return;

            // 1. 绑定主 Target (全逻辑名支持)
            // 如果 Target 是逻辑名称（如 "MainPower"），则从 Profile 解析出物理 ID（如 "ktdy_1"）
            // 我们将其存入 TargetDict，Key 为 "Main"，同时也保留原名为 Key。
            if (!string.IsNullOrEmpty(Target))
            {
                if (profile.TryGetValue(Target, out var physicalId))
                {
                    TargetDict["Main"] = physicalId;      // 标准入口
                    TargetDict[Target] = physicalId;      // 兼容入口 (GetTargetId("MainPower"))
                    
                    // 【重要】运行时修正：为了让底层 Driver 无感，我们通常希望 Target 属性本身变成物理ID。
                    // 但为了保留原始配置信息不被覆盖，建议 Execute 时优先使用 GetTargetId("Main")。
                    // 不过，许多旧代码直接读 .Target 属性。为了最大兼容性，我们在这里做一个“运行时覆盖”。
                    // 注意：这会修改内存中的 Config 实例，但不影响原始 JSON 文件。
                    Target = physicalId; 
                }
                else
                {
                    // 如果 Profile 里没有，说明可能已经是物理ID，或者配置漏了。
                    TargetDict["Main"] = Target;
                }
            }

            // 2. 绑定 AdditionalTargets
            if (AdditionalTargets != null)
            {
                foreach (var role in AdditionalTargets)
                {
                    if (profile.TryGetValue(role, out var deviceId))
                    {
                        TargetDict[role] = deviceId;
                    }
                    else
                    {
                        TargetDict[role] = role;
                    }
                }
            }

            // 3. 【深度解耦】参数动态注入 (Parameter Injection from Profile)
            // 扫描 Profile，查找所有以 "{StepKey}." 开头的键
            // 允许 Profile 覆盖 StepCatalog 中的默认阈值或参数
            if (!string.IsNullOrEmpty(StepKey))
            {
                string prefix = StepKey + ".";
                foreach (var kv in profile)
                {
                    if (kv.Key.StartsWith(prefix))
                    {
                        string paramName = kv.Key.Substring(prefix.Length);
                        Parameters[paramName] = kv.Value;
                        // 记录日志，方便排查参数来源
                        // LogKit.Debug($"[Profile] 覆盖步骤 '{StepKey}' 的参数 '{paramName}' = '{kv.Value}'");
                    }
                }
            }
        }

        /// <summary>
        /// 获取指定角色的目标设备ID。
        /// 优先级：
        /// 1. Parameters["{roleKey}"] (显式指定)
        /// 2. TargetDict["{roleKey}"] (Profile 映射 - 兼容旧模式)
        /// 3. 如果 roleKey == StepType/Main，返回 Target
        /// 4. 如果 AdditionalTargets 含有 roleKey，且未在上述找到，则假设该 roleKey 本身就是物理ID (Direct Mode)
        /// </summary>
        /// <param name="roleKey">角色名称 (如 "PLC", "Main")</param>
        /// <returns>设备ID，如果未找到则返回 null</returns>
        public string GetTargetId(string roleKey)
        {
            // 1. 优先从参数中获取 (Self-Contained Mode)
            // 例如: Parameters: { "PLC": "plc_1" }
            if (Parameters != null && Parameters.TryGetValue(roleKey, out var paramVal))
            {
                return paramVal?.ToString();
            }

            // 2. 从绑定字典获取 (Profile Mode)
            if (TargetDict != null && TargetDict.TryGetValue(roleKey, out var dictVal))
            {
                return dictVal?.ToString();
            }

            // 3. Fallback: Main Target
            if (roleKey == "Main" || roleKey == StepType) 
            {
                return Target;
            }

            // 4. Fallback: Direct Physical ID Mode
            // 如果 roleKey (如 "plc_1") 存在于 AdditionalTargets 列表里，但没有映射值，
            // 说明它可能本身就是一个物理ID。
            if (AdditionalTargets != null && AdditionalTargets.Contains(roleKey))
            {
                return roleKey;
            }
            
            return null;
        }

        public object Clone() {
            // 1. 基础值类型拷贝 (int, bool, string, enum 等)
            // 此时 clone 中的引用类型字段(List, Dictionary) 仍然指向原对象的内存地址
            var clone = (StepConfig)this.MemberwiseClone();

            //// 2.【关键】清空事件订阅
            //// 如果不加这一行，克隆对象属性变化时，会触发原对象绑定的UI刷新，导致报错或逻辑混乱
            //clone.PropertyChanged = null;

            // 3. 补全：字符串列表的深拷贝
            if (this.AdditionalTargets != null)
            {
                clone.AdditionalTargets = new List<string>(this.AdditionalTargets);
            }
            else
            {
                clone.AdditionalTargets = new List<string>();
            }

            // 4. 补全：参数字典的拷贝 (处理 Parameters)
            // 那些基于 Parameters 的属性 (如 IsMaster, DurationMs) 也会因此自动生效
            if (this.Parameters != null)
            {
                clone.Parameters = new Dictionary<string, object>(this.Parameters);
            }
            else
            {
                clone.Parameters = new Dictionary<string, object>();
            }

            // 5. 补全：TargetDict 字典拷贝 (之前代码漏掉的)
            if (this.TargetDict != null)
            {
                clone.TargetDict = new Dictionary<string, object>(this.TargetDict);
            }
            else
            {
                clone.TargetDict = new Dictionary<string, object>();
            }

            // 6. 递归：ExpectedResults 深拷贝
            // 前提：你的 ExpectedSpec 类必须也要实现 Clone 方法！
            if (this.ExpectedResults != null)
            {
                clone.ExpectedResults = this.ExpectedResults
                    .Select(spec => (ExpectedSpec)spec.Clone())
                    .ToList();
            }
            else
            {
                clone.ExpectedResults = new List<ExpectedSpec>();
            }

            // 7. 递归：SubSteps 深拷贝
            if (this.SubSteps != null)
            {
                clone.SubSteps = this.SubSteps
                    .Select(sub => (StepConfig)sub.Clone())
                    .ToList();
            }
            else
            {
                clone.SubSteps = new List<StepConfig>();
            }

            return clone;
        }
    }
}
