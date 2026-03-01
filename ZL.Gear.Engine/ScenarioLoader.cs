using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 场景加载器
    /// 从 JSON 文件加载测试场景，转换为 StepConfig 列表
    /// </summary>
    public static class ScenarioLoader
    {
        /// <summary>
        /// 从 JSON 文件加载场景
        /// </summary>
        /// <param name="jsonPath">JSON 文件路径</param>
        /// <returns>步骤配置列表</returns>
        public static List<StepConfig> Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                throw new FileNotFoundException($"场景文件不存在: {jsonPath}");
            }

            var json = File.ReadAllText(jsonPath);
            return LoadFromJson(json);
        }

        /// <summary>
        /// 从 JSON 字符串加载场景
        /// </summary>
        /// <param name="json">JSON 字符串</param>
        /// <returns>步骤配置列表</returns>
        public static List<StepConfig> LoadFromJson(string json)
        {
            var jObject = JObject.Parse(json);

            // 如果是 DynamicFlow 格式（包含 Sequence），转换为 StepConfig
            if (jObject["Parameters"]?["Sequence"] != null)
            {
                return ParseDynamicFlow(jObject);
            }

            // 标准 StepConfig 数组格式
            var steps = new List<StepConfig>();
            var stepsArray = jObject["Steps"] as JArray;
            if (stepsArray != null)
            {
                foreach (var item in stepsArray)
                {
                    var step = item.ToObject<StepConfig>(JsonSerializer.Create());
                    if (step != null)
                    {
                        steps.Add(step);
                    }
                }
            }
            else
            {
                // 单个 StepConfig
                var step = jObject.ToObject<StepConfig>(JsonSerializer.Create());
                if (step != null)
                {
                    steps.Add(step);
                }
            }

            return steps;
        }

        /// <summary>
        /// 解析 DynamicFlow 格式的 JSON
        /// </summary>
        private static List<StepConfig> ParseDynamicFlow(JObject jObject)
        {
            var steps = new List<StepConfig>();

            var stepConfig = new StepConfig
            {
                StepKey = jObject["StepKey"]?.ToString() ?? "DynamicFlow",
                StepName = jObject["StepName"]?.ToString() ?? "Dynamic Flow Test",
                Command = "DynamicFlow",
                Target = jObject["Target"]?.ToString(),
                Parameters = new Dictionary<string, object>()
            };

            var parameters = jObject["Parameters"] as JObject;
            if (parameters != null)
            {
                // 提取 Variables
                var variables = parameters["Variables"] as JObject;
                if (variables != null)
                {
                    foreach (var prop in variables.Properties())
                    {
                        stepConfig.Parameters[prop.Name] = prop.Value?.ToString();
                    }
                }

                // 将完整的 Parameters 保存
                foreach (var prop in parameters.Properties())
                {
                    if (prop.Name != "Variables" && prop.Name != "Sequence" && prop.Name != "Finally")
                    {
                        stepConfig.Parameters[prop.Name] = prop.Value?.ToString();
                    }
                }
            }

            steps.Add(stepConfig);
            return steps;
        }

        /// <summary>
        /// 获取目录下所有场景文件
        /// </summary>
        /// <param name="scenariosDir">场景目录</param>
        /// <returns>场景文件路径列表</returns>
        public static List<string> GetAllScenarioFiles(string scenariosDir)
        {
            var files = new List<string>();

            if (!Directory.Exists(scenariosDir))
            {
                return files;
            }

            foreach (var file in Directory.GetFiles(scenariosDir, "*.json"))
            {
                // 排除设备配置文件和 Profile 映射文件
                if (IsValidScenarioFile(file))
                {
                    files.Add(file);
                }
            }

            return files;
        }

        /// <summary>
        /// 验证文件是否为有效的测试场景文件
        /// 排除设备配置文件和 Profile 映射文件
        /// </summary>
        private static bool IsValidScenarioFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            // 排除已知的配置文件
            if (fileName.EndsWith("devices.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Profile.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("Profile_Sample.json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var jObject = JObject.Parse(json);

                // 检查是否为有效的测试场景（必须有 StepKey 或 Command 字段）
                var hasStepKey = !string.IsNullOrEmpty(jObject["StepKey"]?.ToString());
                var hasCommand = !string.IsNullOrEmpty(jObject["Command"]?.ToString());

                // 如果既没有 StepKey 也没有 Command，则可能是配置文件
                if (!hasStepKey && !hasCommand)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                // 解析失败，保守处理为非场景文件
                return false;
            }
        }
    }
}
