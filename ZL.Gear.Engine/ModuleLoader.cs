using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 专职负责扫描程序集、加载插件、识别 Bootstrapper
    /// </summary>
    internal class ModuleLoader
    {
        private readonly IStepHandlerRegistry _registry;
        private readonly IActionRegistry _actionRegistry;
        private readonly Action<string> _log;

        public ModuleLoader(IStepHandlerRegistry registry, IActionRegistry actionRegistry, Action<string> log)
        {
            _registry = registry;
            _actionRegistry = actionRegistry;
            _log = log;

            // 注册内置的通用 Handler
            RegisterBuiltInHandlers();
        }

        private void RegisterBuiltInHandlers()
        {
            _log("[ModuleLoader] 注册内置 Handler...");

            // PLC 通用操作（所有项目都可使用）
            RegisterHandlerIfExists("ZL.Gear.Drivers", "PlcStepHandler",
                "SetupPlcRelay", "StopPlcRelay", "WriteToPlc", "ReadFromPlc", "PlcDelay");

            _log("[ModuleLoader] 内置 Handler 注册完成");
        }

        private void RegisterHandlerIfExists(string assemblyName, string className, params string[] commandNames)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName);

                if (assembly == null)
                {
                    _log($"[ModuleLoader] 未找到程序集: {assemblyName}");
                    return;
                }

                var type = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == className && typeof(IStepHandler).IsAssignableFrom(t));

                if (type == null)
                {
                    _log($"[ModuleLoader] 未找到类型: {assemblyName}.{className}");
                    return;
                }

                var handler = (IStepHandler)Activator.CreateInstance(type);
                if (handler == null)
                {
                    _log($"[ModuleLoader] 无法创建实例: {assemblyName}.{className}");
                    return;
                }

                foreach (var cmd in commandNames)
                {
                    _registry.RegisterHandler(cmd, handler);
                    _log($"[ModuleLoader] 已注册内置 Handler: {cmd} -> {className}");
                }
            }
            catch (Exception ex)
            {
                _log($"[ModuleLoader] 注册 Handler 失败: {ex.Message}");
            }
        }

        public void Load(object[] sources)
        {
            if (sources == null) return;

            foreach (var source in sources)
            {
                if (source is string path && Directory.Exists(path))
                {
                    LoadDirectory(path);
                }
                else if (source is Assembly assembly)
                {
                    LoadAssembly(assembly);
                }
                else if (source is IGearExtension extension)
                {
                    extension.Initialize(_registry);
                }
            }
        }

        private void LoadDirectory(string path)
        {
            var dlls = Directory.GetFiles(path, "ZL.Gear.Extension.*.dll");
            foreach (var dll in dlls)
            {
                try { LoadAssembly(Assembly.LoadFrom(dll)); }
                catch (Exception ex) { _log($"加载插件DLL失败: {dll}, {ex.Message}"); }
            }
        }

        private void LoadAssembly(Assembly assembly)
        {
            var types = assembly.GetTypes().Where(t => !t.IsInterface && !t.IsAbstract).ToList();
            ScanAndRegisterWorkflowActionProvider(types);
            // 策略 A: 优先寻找显式 Bootstrapper (IGearExtension)
            // 如果找到了 Bootstrapper，就只运行它，不再进行隐式扫描 (避免重复注册)
            var bootstrappers = types.Where(t => typeof(IGearExtension).IsAssignableFrom(t)).ToList();
            if (bootstrappers.Any())
            {
                foreach (var bType in bootstrappers)
                {
                    var bootstrapper = (IGearExtension)Activator.CreateInstance(bType);
                    bootstrapper.Initialize(_registry);
                }
                return; // ★ 关键策略：有显式入口，就只用显式入口
            }

            // 策略 B: 如果没有 GearExtension，则退化为“隐式自动扫描”模式 (兼容旧代码)
            ScanAndRegisterHandler(types);
        }
        private void ScanAndRegisterWorkflowActionProvider(List<Type> types)
        {
            //  扫描 ActionProviders (MicroWorkflow 原子动作级别)
            foreach (var type in types.Where(t => typeof(IWorkflowActionProvider).IsAssignableFrom(t)))
            {
                try
                {
                    var provider = (IWorkflowActionProvider)Activator.CreateInstance(type);
                    // 让 Provider 把动作注册到我们的 Service 中
                    provider.RegisterActions(_actionRegistry);
                }
                catch { }
            }
        }

        private void ScanAndRegisterHandler(List<Type> types)
        {
            // 扫描 Handlers
            foreach (var type in types.Where(t => typeof(IStepHandler).IsAssignableFrom(t)))
            {
                var cmdName = type.Name.EndsWith("Handler") ? type.Name.Substring(0, type.Name.Length - 7) : type.Name;
                try
                {
                    var instance = (IStepHandler)Activator.CreateInstance(type);
                    _registry.RegisterHandler(cmdName, instance);
                }
                catch { }
            }
        }
    }
}
