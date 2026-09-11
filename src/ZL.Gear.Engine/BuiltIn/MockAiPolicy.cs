using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Abstractions;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.BuiltIn
{
    /// <summary>
    /// 模拟 AI 决策策略。
    /// 用于测试和演示，基于简单的随机逻辑返回决策。
    /// </summary>
    public class MockAiPolicy : IAiDecisionPolicy
    {
        private readonly Random _random = new Random();

        public Task<AiDecision> DecideAsync(StepContext context, CancellationToken token)
        {
            // 模拟思考时间
            Thread.Sleep(100);

            // 简单的模拟逻辑：
            // 检查上下文中是否有 "Voltage" 变量，如果 > 12 则建议降压，否则保持
            double voltage = context.Variables.Get<double>("Voltage");
            
            if (voltage > 12.0)
            {
                return Task.FromResult(new AiDecision
                {
                    NextAction = "AdjustParam",
                    Reason = $"电压过高 ({voltage}V)，建议降低",
                    Confidence = 0.95,
                    NewParameters = new Dictionary<string, object>
                    {
                        { "TargetVoltage", 11.5 }
                    }
                });
            }
            else
            {
                return Task.FromResult(new AiDecision
                {
                    NextAction = "Continue",
                    Reason = "电压正常",
                    Confidence = 0.99
                });
            }
        }
    }
}
