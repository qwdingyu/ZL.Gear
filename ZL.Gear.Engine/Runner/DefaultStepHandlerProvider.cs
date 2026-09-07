using System;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 默认步骤处理器提供者。
    /// 职责：封装模板 Handler 和回退 Handler 的创建逻辑，将 <see cref="RegistryStepHandlerLookup"/> 内部硬编码的策略外部化。
    /// 设计要点：
    /// 1. 集中管理模板路径解析逻辑，避免散落在各调用方；
    /// 2. 允许通过构造函数注入自定义实现，替换默认的 <see cref="TemplateFlowHandler"/> 或 <see cref="CommonHandler"/>；
    /// 3. 保持与现有行为完全兼容，默认行为与原 <see cref="RegistryStepHandlerLookup"/> 一致。
    /// </summary>
    public class DefaultStepHandlerProvider : IStepHandlerProvider
    {
        /// <summary>
        /// 模板 Handler，负责解析 JSON DSL 模板并执行。
        /// </summary>
        private readonly TemplateFlowHandler _templateHandler;

        /// <summary>
        /// 回退 Handler，当没有专用 Handler 和模板时使用。
        /// </summary>
        private readonly IStepHandler _fallbackHandler;

        /// <summary>
        /// 使用默认模板路径和 CommonHandler 回退初始化。
        /// 模板路径解析逻辑：优先使用运行目录下的 Templates，其次使用项目 docs/Templates。
        /// </summary>
        public DefaultStepHandlerProvider()
            : this(ResolveDefaultTemplatePath())
        {
        }

        /// <summary>
        /// 使用指定模板路径初始化。
        /// </summary>
        /// <param name="templateRootDir">模板根目录，为 null 时使用默认路径。</param>
        public DefaultStepHandlerProvider(string templateRootDir)
        {
            // 模板路径解析逻辑集中在此，避免散落在各调用方
            var resolvedPath = string.IsNullOrEmpty(templateRootDir)
                ? ResolveDefaultTemplatePath()
                : templateRootDir;

            _templateHandler = new TemplateFlowHandler(resolvedPath);
            _fallbackHandler = new CommonHandler();
        }

        /// <summary>
        /// 使用自定义模板 Handler 和回退 Handler 初始化。
        /// 允许完全替换策略实现，例如用自定义 DSL 引擎替换 TemplateFlowHandler。
        /// </summary>
        /// <param name="templateHandler">模板 Handler 实例。</param>
        /// <param name="fallbackHandler">回退 Handler 实例。</param>
        /// <exception cref="ArgumentNullException">templateHandler 或 fallbackHandler 为 null。</exception>
        public DefaultStepHandlerProvider(TemplateFlowHandler templateHandler, IStepHandler fallbackHandler)
        {
            _templateHandler = templateHandler ?? throw new ArgumentNullException(nameof(templateHandler));
            _fallbackHandler = fallbackHandler ?? throw new ArgumentNullException(nameof(fallbackHandler));
        }

        /// <summary>
        /// 尝试获取模板 Handler。
        /// </summary>
        /// <param name="step">当前步骤配置，用于判断是否有可用模板。</param>
        /// <param name="handler">如果找到模板，输出模板 Handler。</param>
        /// <returns>是否找到可用模板。</returns>
        public bool TryGetTemplateHandler(StepConfig step, out IStepHandler handler)
        {
            if (_templateHandler.HasTemplate(step.Command))
            {
                handler = _templateHandler;
                return true;
            }

            handler = null;
            return false;
        }

        /// <summary>
        /// 获取通用回退 Handler。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <returns>回退 Handler 实例。</returns>
        public IStepHandler GetFallbackHandler(StepConfig step)
        {
            return _fallbackHandler;
        }

        /// <summary>
        /// 解析默认模板路径。
        /// 优先级：运行目录下的 Templates > 项目 docs/Templates。
        /// </summary>
        /// <returns>模板目录绝对路径。</returns>
        private static string ResolveDefaultTemplatePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string templatePath = System.IO.Path.Combine(baseDir, "Templates");
            if (!System.IO.Directory.Exists(templatePath))
            {
                var parent = System.IO.Directory.GetParent(baseDir)?.Parent?.Parent;
                if (parent != null) templatePath = System.IO.Path.Combine(parent.FullName, "docs", "Templates");
            }
            return templatePath;
        }
    }
}
