using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Devices;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 自动从文件系统加载 DSL 模板并执行。
    /// 实现从 C# 硬编码到 JSON 零代码的平滑过渡。
    /// </summary>
    public class TemplateFlowHandler : IStepHandler
    {
        private static readonly ConcurrentDictionary<string, string> _templateCache = new ConcurrentDictionary<string, string>();
        private readonly string _templateRootDir;
        private readonly DynamicFlowHandler _flowHandler = new DynamicFlowHandler();

        public TemplateFlowHandler(string templateRootDir)
        {
            _templateRootDir = templateRootDir;
        }

        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            string command = step.Command;
            if (string.IsNullOrEmpty(command)) return ExecutionResult.Failed("Command 为空");

            // 1. 寻找模板文件 (查找顺序: 动作库 -> 通用库)
            string templateJson = GetTemplateJson(command);
            if (templateJson == null)
            {
                return ExecutionResult.Failed($"未找到命令 '{command}' 对应的 DSL 模板文件。");
            }

            // 2. 注入模板到 Parameters 中，伪装成 DynamicFlow
            // 这样可以直接利用 DynamicFlowHandler 的成熟逻辑
            var stepClone = (StepConfig)step.Clone();
            stepClone.Parameters["WorkflowDefinition"] = templateJson;

            return await _flowHandler.ExecuteAsync(stepClone, context);
        }

        private string GetTemplateJson(string command)
        {
            if (_templateCache.TryGetValue(command, out var cached)) return cached;

            // 查找路径规则：
            // 1. {RootDir}/Actions/{Command}.json
            // 2. {RootDir}/{Command}.json
            string[] probePaths = new[]
            {
                Path.Combine(_templateRootDir, "Actions", $"{command}.json"),
                Path.Combine(_templateRootDir, $"{command}.json")
            };

            foreach (var path in probePaths)
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    _templateCache.TryAdd(command, content);
                    return content;
                }
            }

            return null;
        }

        /// <summary>
        /// 清除缓存（调试用）
        /// </summary>
        public static void ClearCache() => _templateCache.Clear();
    }
}
