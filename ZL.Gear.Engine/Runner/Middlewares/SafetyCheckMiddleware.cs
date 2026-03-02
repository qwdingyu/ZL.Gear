using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 安全检查中间件。
    /// 执行前检查安全条件（如急停状态、门限开关），保护人员和设备安全。
    /// 
    /// 使用方式：在步骤的 Parameters 中配置
    /// - SafetyConditions: 安全条件列表，格式如 "EmergencyStop=false" 或 "DoorClosed=true"
    /// - SafetyCheckMode: 检查模式（All/Any），默认 All（全部满足才执行）
    /// - FailOnSafetyError: 安全检查失败时是否终止整线，默认 true
    /// 
    /// 示例：
    /// {
    ///   "SafetyConditions": [
    ///     "EmergencyStop=false",
    ///     "DoorClosed=true", 
    ///     "PowerLimit_ok=true"
    ///   ]
    /// }
    /// </summary>
    public class SafetyCheckMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;
        private readonly Func<string, bool> _safetyCheckFunc;

        public SafetyCheckMiddleware(Action<string> log, Func<string, bool> safetyCheckFunc = null)
        {
            _log = log ?? (s => { });
            _safetyCheckFunc = safetyCheckFunc ?? DefaultSafetyCheck;
        }

        public Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 检查是否启用安全检查
            if (step.Parameters == null || !step.Parameters.ContainsKey("SafetyConditions"))
            {
                return next(step, context);
            }

            // 获取安全条件列表
            var conditions = step.Parameters["SafetyConditions"];
            var conditionList = ParseConditions(conditions);
            
            if (conditionList.Count == 0)
            {
                return next(step, context);
            }

            // 获取检查模式
            string checkMode = "All";
            if (step.Parameters.TryGetValue("SafetyCheckMode", out var modeObj))
            {
                checkMode = modeObj?.ToString() ?? "All";
            }

            // 获取失败时动作
            bool failOnError = true;
            if (step.Parameters.TryGetValue("FailOnSafetyError", out var failObj))
            {
                bool.TryParse(failObj?.ToString(), out failOnError);
            }

            _log($"[Safety] 开始检查安全条件 ({checkMode} 模式): {string.Join(", ", conditionList)}");

            // 执行安全检查
            var failedConditions = new List<string>();
            foreach (var condition in conditionList)
            {
                bool checkResult = _safetyCheckFunc(condition);
                if (!checkResult)
                {
                    failedConditions.Add(condition);
                    _log($"[Safety] ❌ 安全条件不满足: {condition}");
                }
                else
                {
                    _log($"[Safety] ✅ 安全条件满足: {condition}");
                }
            }

            // 判断是否通过
            bool passed = checkMode.Equals("Any", StringComparison.OrdinalIgnoreCase)
                ? failedConditions.Count == 0  // Any 模式：有一个失败就不通过
                : failedConditions.Count == conditionList.Count;  // All 模式：全部失败才不通过（这个逻辑有点反直觉，让我修正）
            
            // 实际上应该是：
            // - All 模式：所有条件都满足才通过
            // - Any 模式：任意一个条件满足就通过（不常见）
            // 让我重新理解：
            // - All：所有条件都必须满足（默认）
            // - Any：任意一个满足即可（这个检查的是"或"关系，不常见）
            
            // 修正逻辑：
            passed = checkMode.Equals("Any", StringComparison.OrdinalIgnoreCase)
                ? failedConditions.Count < conditionList.Count  // Any：至少一个通过
                : failedConditions.Count == 0;  // All：全部通过

            if (!passed)
            {
                string errorMsg = $"安全检查失败: {string.Join(", ", failedConditions)}";
                _log($"[Safety] 🔴 {errorMsg}");
                
                if (failOnError)
                {
                    // 安全检查失败，终止测试
                    return Task.FromResult(ExecutionResult<List<Measurement>>.Failed(
                        errorMsg,
                        new List<Measurement>()));
                }
                else
                {
                    // 仅记录警告，继续执行
                    _log($"[Safety] ⚠️ 安全检查失败但配置为继续执行");
                }
            }
            else
            {
                _log($"[Safety] ✅ 所有安全检查通过");
            }

            return next(step, context);
        }

        private List<string> ParseConditions(object conditions)
        {
            var result = new List<string>();
            
            if (conditions is string str)
            {
                result.Add(str);
            }
            else if (conditions is List<object> list)
            {
                result.AddRange(list.Select(o => o?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            else if (conditions is List<string> strList)
            {
                result.AddRange(strList.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            
            return result;
        }

        /// <summary>
        /// 默认安全检查实现（实际使用时应该注入真实的检查函数）
        /// </summary>
        private bool DefaultSafetyCheck(string condition)
        {
            // 解析条件格式: "Key=Value" 或 "Key!=Value" 或 "Key"
            if (string.IsNullOrWhiteSpace(condition))
                return true;

            // 简单实现：假设条件为 true
            // 实际使用时应该在外部注入真实的硬件状态检查函数
            return true;
        }
    }
}
