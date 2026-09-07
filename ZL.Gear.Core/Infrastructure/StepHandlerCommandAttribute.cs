using System;
using System.Collections.Generic;
using System.Linq;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 标记类为步骤处理器，并指定其命令名称及行为元数据。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class StepHandlerCommandAttribute : Attribute
    {
        /// <summary>
        /// 命令名称
        /// </summary>
        public string Command { get; }

        /// <summary>
        /// 是否允许覆盖已注册的处理器
        /// </summary>
        public bool AllowOverwrite { get; set; } = true;

        /// <summary>
        /// 命令别名列表，用于兼容旧命令名或多入口调用。
        /// </summary>
        public string[] Aliases { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 获取或设置一个值，指示该步骤的结果是否已由 Handler 自行判定。
        /// 如果为 true，SequenceExecutor 将跳过 ResultEvaluator，直接采用 Handler 的 Success/Failure 作为最终结果。
        /// 仅当 <see cref="HasEvaluateResult"/> 为 true 时有效。
        /// </summary>
        public bool EvaluateResult
        {
            get => _evaluateResult;
            set
            {
                _evaluateResult = value;
                HasEvaluateResult = true;
            }
        }
        private bool _evaluateResult;

        /// <summary>
        /// 是否已通过 attribute 用法显式设置 <see cref="EvaluateResult"/>。
        /// C# attribute 参数不支持可空值类型（CS0655），因此用 bool 值 + 本标志表达三态；
        /// 标志为 false（未显式设置）时语义等同 null，即使用全局默认评估策略。
        /// 该属性由 <see cref="EvaluateResult"/> 的 setter 自动置位，调用方只读。
        /// </summary>
        public bool HasEvaluateResult { get; private set; }

        /// <summary>
        /// 命令的中文或业务描述，可用于文档生成或日志展示。
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 参数 schema 描述，用于文档生成、IDE 提示或启动期参数校验。
        /// 约定格式示例："LoadN:int(500) 施加载荷(N); HoldMs:int(3000) 保持时长(ms)"
        /// </summary>
        public string ParameterSchema { get; set; }

        /// <summary>
        /// 创建步骤处理器命令特性。
        /// </summary>
        /// <param name="command">命令名称</param>
        public StepHandlerCommandAttribute(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("命令名称不能为空。", nameof(command));

            Command = command;
        }
    }
}
