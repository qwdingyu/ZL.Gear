using System;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Sensing.Orchestration;

namespace ZL.Gear.Engine
{
    public class UniversalActionProvider : IWorkflowActionProvider
    {
        public void RegisterActions(IActionRegistry registry)
        {
            // 注册万能测量动作 "GenericMeasure"
            // 参数约定：
            // - Target: 设备名称 (必须)
            // - Command: 协议命令 (可选，默认为 READ)
            // - Sampling: SamplingConfigModel 对象 (可选)
            registry.RegisterMeasurement("GenericMeasure", async (step, ctx) =>
            {
                var stepName = step.StepName;
                ctx.Log($"[{stepName}] 执行通用测量 (GenericMeasure)...");

                // 1. 获取采样配置
                // 优先从 Parameters["Sampling"] 获取，如果没有则尝试构造默认值
                SamplingConfigModel config = ctx.Get<SamplingConfigModel>("Sampling");
                if (config == null)
                {
                    // 尝试从散装参数构建简单的 Duration 模式
                    config = new SamplingConfigModel
                    {
                        Mode = "Duration",
                        TimeoutMs = step.TimeoutMs > 0 ? step.TimeoutMs : 5000,
                        SampleIntervalMs = ctx.Get<int>("Interval", 100)
                    };
                }

                // 2. 创建万能测量器
                var measurer = new UniversalMeasurer(ctx, config, ctx.Log);

                // 3. 执行
                var result = await measurer.MeasureAsync(ctx.CancellationToken);

                // 4. 转换结果
                if (result.Success)
                {
                    var val = result.GetValueAsObject();
                    // 确保返回的是 Measurement 对象
                    if (val is Measurement m) return m;
                    
                    // 如果是 double 等原始值，包装一下
                    return Measurement.Create(step.MeasurementKey, val, true, result.Message, "", result.SamplesCollected);
                }
                
                return Measurement.Create(step.MeasurementKey, result.GetValueAsObject(), false, result.Message, "", result.SamplesCollected);
            });
        }
    }
}
