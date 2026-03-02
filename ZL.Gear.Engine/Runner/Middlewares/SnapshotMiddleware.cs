using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 失败快照中间件。
    /// 步骤执行失败时自动保存现场，方便快速复现问题。
    /// 
    /// 保存内容：
    /// - 步骤配置 (StepConfig)
    /// - 上下文变量 (Context Variables)
    /// - 测量数据 (Measurements)
    /// - 错误信息 (Error Message)
    /// - 时间戳 (Timestamp)
    /// 
    /// 使用方式：在步骤的 Parameters 中配置
    /// - SnapshotEnabled: 是否启用快照，默认 true（仅失败时）
    /// - SnapshotPath: 快照保存目录，默认 "./snapshots/"
    /// - SnapshotMaxCount: 最大保存快照数，默认 100
    /// </summary>
    public class SnapshotMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;
        
        private string _snapshotPath = "./snapshots/";
        private int _maxSnapshotCount = 100;
        private string _currentModel = "";
        private string _currentBarcode = "";

        public SnapshotMiddleware(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        /// <summary>
        /// 设置当前测试上下文
        /// </summary>
        public void SetContext(string model, string barcode)
        {
            _currentModel = model ?? "";
            _currentBarcode = barcode ?? "";
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 获取快照配置
            bool snapshotEnabled = true;
            
            if (step.Parameters != null && step.Parameters.TryGetValue("SnapshotEnabled", out var seObj))
            {
                bool.TryParse(seObj?.ToString(), out snapshotEnabled);
            }

            if (step.Parameters != null && step.Parameters.TryGetValue("SnapshotPath", out var spObj))
            {
                _snapshotPath = spObj?.ToString() ?? _snapshotPath;
            }

            if (step.Parameters != null && step.Parameters.TryGetValue("SnapshotMaxCount", out var smcObj))
            {
                int.TryParse(smcObj?.ToString(), out _maxSnapshotCount);
            }

            // 执行步骤
            var result = await next(step, context);

            // 如果失败且启用快照，则保存现场
            if (!result.Success && snapshotEnabled)
            {
                _log($"[Snapshot] ⚠️ 步骤 '{step.StepName}' 执行失败，正在保存快照...");
                
                try
                {
                    SaveSnapshot(step, context, result);
                    _log($"[Snapshot] ✅ 快照已保存到: {_snapshotPath}");
                }
                catch (Exception ex)
                {
                    _log($"[Snapshot] ⚠️ 保存快照失败: {ex.Message}");
                }
            }

            return result;
        }

        private void SaveSnapshot(StepConfig step, StepContext context, ExecutionResult<List<Measurement>> result)
        {
            // 确保目录存在
            if (!Directory.Exists(_snapshotPath))
            {
                Directory.CreateDirectory(_snapshotPath);
            }

            // 生成文件名
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var safeStepName = SanitizeFileName(step.StepName ?? step.Command ?? "unknown");
            var fileName = $"snapshot_{timestamp}_{safeStepName}_{_currentBarcode}.json";
            var filePath = Path.Combine(_snapshotPath, fileName);

            // 构建快照数据
            var snapshot = new
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Model = _currentModel,
                Barcode = _currentBarcode,
                Step = new
                {
                    step.StepName,
                    step.StepKey,
                    step.Command,
                    step.Target,
                    step.TimeoutMs,
                    step.Parameters,
                    step.ExpectedResults
                },
                Context = new
                {
                    Variables = context.Variables.AsDictionary()
                },
                Result = new
                {
                    Success = result.Success,
                    Message = result.Message,
                    SamplesCollected = result.SamplesCollected,
                    Measurements = result.GetValueAsObject() is List<Measurement> measurements 
                        ? measurements.Select(m => new 
                        {
                            m.Key,
                            m.Value,
                            m.Success,
                            m.Message,
                            m.Unit
                        }).ToList() 
                        : null
                },
                Error = new
                {
                    Message = result.Message,
                    StackTrace = "" // 可以在调用处传入
                }
            };

            // 序列化为 JSON
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);

            // 写入文件
            File.WriteAllText(filePath, json, Encoding.UTF8);

            // 清理旧快照
            CleanupOldSnapshots();
        }

        private void CleanupOldSnapshots()
        {
            try
            {
                var files = Directory.GetFiles(_snapshotPath, "snapshot_*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(_maxSnapshotCount)
                    .ToList();

                foreach (var file in files)
                {
                    File.Delete(file);
                    _log($"[Snapshot] 已清理旧快照: {Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                _log($"[Snapshot] 清理旧快照失败: {ex.Message}");
            }
        }

        private string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// 列出所有快照
        /// </summary>
        public static string ListSnapshots(string snapshotPath = "./snapshots/")
        {
            if (!Directory.Exists(snapshotPath))
            {
                return "快照目录不存在";
            }

            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           ZL.Gear 失败快照列表");
            sb.AppendLine("==================================================");

            var files = Directory.GetFiles(snapshotPath, "snapshot_*.json")
                .OrderByDescending(f => File.GetCreationTime(f))
                .Take(20);

            foreach (var file in files)
            {
                var info = new FileInfo(file);
                sb.AppendLine($"{info.CreationTime:yyyy-MM-dd HH:mm:ss} | {info.Name}");
            }

            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }
}
