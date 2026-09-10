using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using ZL.Gear.Core.Devices;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 步骤参数读取来源（解决「Args / Variables / Global」混读导致的产线漏检）。
    /// </summary>
    public enum StepArgSource
    {
        /// <summary>
        /// 仅本步 Args（<see cref="StepConfig.Parameters"/> 须显式含键）。
        /// 用于 RecipeId、LimitOhm 等 fail-closed 必填，禁止从流程 Variables 静默兜底。
        /// </summary>
        ArgsOnly,

        /// <summary>
        /// 先 Args，再流程 Variables（如前序 ApplyRecipe SetShared 的值）。
        /// </summary>
        ArgsThenVariables,

        /// <summary>
        /// 与 <see cref="StepContext.Get{T}"/> 相同：Args → Variables → GlobalContext。
        /// </summary>
        All,

        /// <summary>
        /// 仅流程 Variables（前序 SetShared / Calculate OutputKey / Measure 写入），不读本步 Args。
        /// 用于 MarkComplete 读 RecipeId、报表/日志取前序状态；禁止用 Args 同名键误覆盖。
        /// </summary>
        VariablesOnly,

        /// <summary>
        /// 仅 GlobalContext（条码/型号等会话输入），不读 Args 与 Variables。
        /// 避免 <see cref="All"/> 把工艺变量与 Global 同名键混读。
        /// </summary>
        GlobalOnly
    }

    /// <summary>
    /// Handler 参数读取器：统一类型转换、来源策略与中文错误信息，减少业务 Handler 中的样板代码。
    /// </summary>
    /// <remarks>
    /// <para><see cref="StepHandlerCommandAttribute.ParameterSchema"/> 目前<strong>仅</strong>用于文档与启动期 schema 冲突校验（docs/121），
    /// 尚未自动绑定；本类是当前推荐的运行时写法，格式与 schema 字符串语义对齐。详见 docs/140。</para>
    /// <para><strong>污染分级</strong>（与 docs/136 对齐）：</para>
    /// <list type="bullet">
    /// <item>写：仅 <see cref="SetShared"/> 提升流程级；禁止 Handler 内 <c>Variables.Set</c>（节点级，后继读不到且易漂移）。</item>
    /// <item>读限值/配方：用 <see cref="StepArgSource.ArgsOnly"/>，禁止 <see cref="StepArgSource.All"/> 静默兜底 Variables/Global。</item>
    /// <item>读前序工艺状态：用 <see cref="StepArgSource.VariablesOnly"/> / <see cref="GetFlowString"/>。</item>
    /// <item>读条码/型号：用 <see cref="StepArgSource.GlobalOnly"/> / <see cref="GetGlobalString"/>。</item>
    /// <item>Parallel 分支同键 SetShared：末写覆盖（竞态），Parallel 内各用不同键或 Sequence 串行。</item>
    /// </list>
    /// </remarks>
    public sealed class StepArgsReader
    {
        private readonly StepConfig _step;
        private readonly StepContext _context;
        /// <summary>错误信息前缀（见 <see cref="FormatMissing"/> / <see cref="FormatNotFound"/> / <see cref="FormatInvalid"/>）。</summary>
        private readonly string _commandLabel;

        private StepArgsReader(StepConfig step, StepContext context, string commandLabel)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _commandLabel = string.IsNullOrWhiteSpace(commandLabel) ? step.Command : commandLabel;
        }

        /// <summary>
        /// 从当前步骤与上下文创建读取器。
        /// </summary>
        /// <param name="step">步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <param name="commandLabel">错误信息中的命令名（默认 step.Command）。</param>
        public static StepArgsReader From(StepConfig step, StepContext context, string commandLabel = null)
        {
            return new StepArgsReader(step, context, commandLabel);
        }

        /// <summary>
        /// 读取必填字符串。
        /// </summary>
        public bool TryRequireString(string key, StepArgSource source, out string value, out string error)
        {
            if (!TryGetRaw(key, source, requireArgKey: source == StepArgSource.ArgsOnly, out var raw, out error))
            {
                value = null;
                return false;
            }

            value = raw?.ToString()?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                error = FormatInvalid(key, "不能为空。");
                value = null;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 读取必填正数（double）。
        /// </summary>
        public bool TryRequirePositiveDouble(string key, StepArgSource source, out double value, out string error)
        {
            if (!TryGetDouble(key, source, out value, out error, source == StepArgSource.ArgsOnly))
            {
                return false;
            }

            if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                error = FormatInvalid(key, $"须为正数，当前为 {value.ToString(CultureInfo.InvariantCulture)}。");
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 读取可选 double；缺失或无法解析时返回 defaultValue。
        /// </summary>
        public double GetOptionalDouble(string key, double defaultValue, StepArgSource source = StepArgSource.All)
        {
            return TryGetDouble(key, source, out var value, out _, requireArgKey: false)
                ? value
                : defaultValue;
        }

        /// <summary>
        /// 读取可选字符串；缺失时返回 defaultValue。
        /// </summary>
        public string GetOptionalString(string key, string defaultValue, StepArgSource source = StepArgSource.All)
        {
            if (!TryGetRaw(key, source, requireArgKey: false, out var raw, out _) || raw == null)
            {
                return defaultValue;
            }

            var text = raw.ToString()?.Trim();
            return string.IsNullOrEmpty(text) ? defaultValue : text;
        }

        /// <summary>
        /// 读取流程变量字符串（仅 Variables，等同 <c>Variables.TryGet ? : default</c> 的语法糖）。
        /// </summary>
        public string GetFlowString(string key, string defaultValue = "?")
        {
            return GetOptionalString(key, defaultValue, StepArgSource.VariablesOnly);
        }

        /// <summary>
        /// 读取流程变量数值（仅 Variables）；缺失或无法解析时返回 defaultValue。
        /// </summary>
        public double GetFlowDouble(string key, double defaultValue = 0)
        {
            return TryGetDouble(key, StepArgSource.VariablesOnly, out var value, out _)
                ? value
                : defaultValue;
        }

        /// <summary>
        /// 流程变量必填（前序应已 SetShared）；缺失返回错误信息。
        /// </summary>
        public bool TryRequireFlowString(string key, out string value, out string error)
        {
            if (!TryGetRaw(key, StepArgSource.VariablesOnly, requireArgKey: false, out var raw, out error))
            {
                value = null;
                return false;
            }

            value = raw?.ToString()?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                error = FormatFlowMissing(key);
                value = null;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 尝试读取 double（可指定是否必须在 Args 中出现键）。
        /// </summary>
        public bool TryGetDouble(
            string key,
            StepArgSource source,
            out double value,
            out string error,
            bool requireArgKey = false)
        {
            if (!TryGetRaw(key, source, requireArgKey, out var raw, out error))
            {
                value = 0;
                return false;
            }

            if (!TryToDouble(raw, out value))
            {
                error = FormatInvalid(key, "无法解析为数值。");
                return false;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = FormatInvalid(key, "数值非法（NaN/Infinity）。");
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 将值写入流程级共享变量（DynamicFlow 后继节点可读）。
        /// </summary>
        /// <remarks>
        /// 实现为 <see cref="ContextVariableStore.SetShared"/>：从节点子作用域写到<strong>直接父级</strong>（通常 flowVariables），
        /// 不穿透到 Session 根/Global。Parallel 同键并发写属末写覆盖，见 docs/140 §四。
        /// </remarks>
        public void SetShared<T>(string key, T value)
        {
            _context.Variables.SetShared(key, value);
        }

        /// <summary>
        /// 读取 GlobalContext 字符串（条码、型号等），不读 Args/Variables。
        /// </summary>
        public string GetGlobalString(string key, string defaultValue = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }

            if (_context.GlobalContext != null
                && _context.GlobalContext.TryGetValue(key, out var raw)
                && raw != null)
            {
                var text = raw.ToString()?.Trim();
                return string.IsNullOrEmpty(text) ? defaultValue : text;
            }

            return defaultValue;
        }

        /// <summary>
        /// 快捷失败结果（配合 early return）。
        /// </summary>
        public static ExecutionResultBase Fail(string message)
        {
            return ExecutionResult.Failed(message);
        }

        private bool TryGetRaw(
            string key,
            StepArgSource source,
            bool requireArgKey,
            out object raw,
            out string error)
        {
            raw = null;
            error = null;

            if (string.IsNullOrWhiteSpace(key))
            {
                error = FormatInvalid("?", "键名为空。");
                return false;
            }

            switch (source)
            {
                case StepArgSource.ArgsOnly:
                    if (_step.Parameters == null || !_step.Parameters.ContainsKey(key))
                    {
                        error = FormatMissing(key);
                        return false;
                    }
                    raw = _step.Parameters[key];
                    return true;

                case StepArgSource.ArgsThenVariables:
                    if (_step.Parameters != null
                        && _step.Parameters.TryGetValue(key, out var fromArgs)
                        && fromArgs != null)
                    {
                        raw = fromArgs;
                        return true;
                    }
                    if (_context.Variables.TryGet<object>(key, out var fromVars))
                    {
                        raw = fromVars;
                        return true;
                    }
                    error = requireArgKey ? FormatMissing(key) : FormatNotFound(key);
                    return false;

                case StepArgSource.All:
                    if (requireArgKey && (_step.Parameters == null || !_step.Parameters.ContainsKey(key)))
                    {
                        error = FormatMissing(key);
                        return false;
                    }
                    // StepContext.Get 已含 Args → Variables → Global 与 JToken 转换
                    if (_context.Get<object>(key) is { } got && got != null)
                    {
                        raw = got;
                        return true;
                    }
                    error = FormatNotFound(key);
                    return false;

                case StepArgSource.VariablesOnly:
                    if (_context.Variables.TryGet<object>(key, out var flowVal))
                    {
                        raw = flowVal;
                        return true;
                    }
                    error = requireArgKey ? FormatFlowMissing(key) : FormatFlowNotFound(key);
                    return false;

                case StepArgSource.GlobalOnly:
                    if (_context.GlobalContext != null
                        && _context.GlobalContext.TryGetValue(key, out var globalVal)
                        && globalVal != null)
                    {
                        raw = globalVal;
                        return true;
                    }
                    error = FormatGlobalNotFound(key);
                    return false;

                default:
                    error = FormatInvalid(key, "未知参数来源。");
                    return false;
            }
        }

        private static bool TryToDouble(object raw, out double value)
        {
            if (raw is double d)
            {
                value = d;
                return true;
            }

            if (raw is float f)
            {
                value = f;
                return true;
            }

            if (raw is int i)
            {
                value = i;
                return true;
            }

            if (raw is long l)
            {
                value = l;
                return true;
            }

            if (raw is JValue jv && jv.Value != null)
            {
                return TryToDouble(jv.Value, out value);
            }

            return double.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private string FormatMissing(string key)
        {
            return $"{_commandLabel} 缺少必填 Args 参数 '{key}'。";
        }

        private string FormatNotFound(string key)
        {
            return $"{_commandLabel} 未找到参数或变量 '{key}'。";
        }

        private string FormatFlowNotFound(string key)
        {
            return $"{_commandLabel} 未找到流程变量 '{key}'（前序步骤应已 SetShared）。";
        }

        private string FormatFlowMissing(string key)
        {
            return $"{_commandLabel} 缺少流程变量 '{key}'（前序步骤应已 SetShared）。";
        }

        private string FormatGlobalNotFound(string key)
        {
            return $"{_commandLabel} 未找到 GlobalContext 键 '{key}'。";
        }

        private string FormatInvalid(string key, string detail)
        {
            return $"{_commandLabel} 参数 '{key}' {detail}";
        }
    }
}
