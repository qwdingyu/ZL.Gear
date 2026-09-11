using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ZL.Gear.Core.Abstractions;
using ZL.Gear.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace ZL.Gear.Engine.BuiltIn
{
    /// <summary>
    /// 基于 Microsoft.SemanticKernel 的 AI 决策策略。
    /// 利用 Semantic Kernel 的抽象能力，支持无缝切换 OpenAI, Azure OpenAI, HuggingFace 等多种模型提供商。
    /// </summary>
    public class LlmAiPolicy : IAiDecisionPolicy
    {
        private readonly Kernel _kernel;

        /// <summary>
        /// 构造函数注入 Kernel。
        /// Kernel 应该在 Startup 中配置好（包括模型、密钥、Endpoint）。
        /// </summary>
        public LlmAiPolicy(Kernel kernel)
        {
            _kernel = kernel;
        }

        public async Task<AiDecision> DecideAsync(StepContext context, CancellationToken token)
        {
            var chatService = _kernel.GetRequiredService<IChatCompletionService>();

            // 1. 构建上下文
            var observation = new {
                Step = context.StepConfig.StepName,
                Variables = context.Variables.AsDictionary()
            };

            // 2. 获取提示词配置 (支持从 JSON 参数中动态注入)
            // 默认提示词
            string systemPrompt = "You are an industrial decision assistant. Output JSON only.";
            string userPromptTemplate = "Current State: {0}";

            // 从步骤参数中覆盖
            if (context.StepConfig.Parameters.TryGetValue("SystemPrompt", out var spObj))
            {
                systemPrompt = spObj.ToString();
            }
            if (context.StepConfig.Parameters.TryGetValue("UserPromptTemplate", out var upObj))
            {
                userPromptTemplate = upObj.ToString();
            }

            // 3. 构建 Prompt
            var history = new ChatHistory();
            history.AddSystemMessage(systemPrompt);
            history.AddUserMessage(string.Format(userPromptTemplate, JsonConvert.SerializeObject(observation)));

            // 4. 配置执行设置 (JSON Mode)
            var settings = new OpenAIPromptExecutionSettings { 
                // ResponseFormat = "json_object", // 移除以兼容旧版本或避免类型错误
                Temperature = 0.1 
            };

            // 4. 调用模型
            var result = await chatService.GetChatMessageContentAsync(history, settings, _kernel, token);
            string jsonContent = result.Content;

            // 5. 反序列化
            try 
            {
                return JsonConvert.DeserializeObject<AiDecision>(jsonContent);
            }
            catch (Exception ex)
            {
                return AiDecision.Make("Abort", $"AI 响应解析失败: {ex.Message}");
            }
        }
    }
}
