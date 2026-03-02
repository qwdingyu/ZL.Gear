using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 审计日志中间件。
    /// 记录关键操作审计日志，满足合规要求（FDA、ISO等）。
    /// </summary>
    public class AuditLogMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;
        private static readonly ConcurrentQueue<AuditRecord> _auditQueue = new();
        private string _auditLogPath = "./logs/audit.log";
        private string _auditLevel = "StepsOnly";
        private string _currentOperator = "System";
        private string _currentModel = "";
        private string _currentBarcode = "";

        public AuditLogMiddleware(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        public void SetOperator(string operatorName)
        {
            _currentOperator = operatorName ?? "System";
        }

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
            var startTime = DateTime.Now;
            var result = await next(step, context);
            var endTime = DateTime.Now;
            var duration = (endTime - startTime).TotalMilliseconds;

            bool shouldAudit = _auditLevel switch
            {
                "All" => true,
                "StepsOnly" => true,
                "ErrorsOnly" => !result.Success,
                _ => true
            };

            if (shouldAudit)
            {
                var record = new AuditRecord
                {
                    Timestamp = startTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    Operator = _currentOperator,
                    Model = _currentModel,
                    Barcode = _currentBarcode,
                    StepName = step.StepName ?? "",
                    StepCommand = step.Command ?? "",
                    Target = step.Target ?? "",
                    Result = result.Success ? "PASS" : "FAIL",
                    Message = result.Message ?? "",
                    DurationMs = (int)duration,
                    MeasurementCount = result.GetValueAsObject() is List<Measurement> m ? m.Count : 0,
                    ErrorCode = result.Success ? "" : ExtractErrorCode(result.Message)
                };

                _auditQueue.Enqueue(record);
                _log($"[Audit] 记录审计日志: {record.StepName} - {record.Result}");
                FlushToFile(record);
            }

            return result;
        }

        private void FlushToFile(AuditRecord record)
        {
            try
            {
                var dir = Path.GetDirectoryName(_auditLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var line = $"{record.Timestamp}|{record.Operator}|{record.Model}|{record.Barcode}|" +
                          $"{record.StepName}|{record.StepCommand}|{record.Target}|" +
                          $"{record.Result}|{record.Message}|{record.DurationMs}ms|{record.ErrorCode}";
                
                File.AppendAllText(_auditLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log($"[Audit] 写入审计日志失败: {ex.Message}");
            }
        }

        private string ExtractErrorCode(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            if (message.Length > 50) return message.Substring(0, 50);
            return message;
        }

        public static string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("           ZL.Gear 审计日志报告");
            sb.AppendLine("==================================================");
            
            var records = _auditQueue.ToArray();
            foreach (var r in records.OrderByDescending(x => x.Timestamp).Take(100))
            {
                sb.AppendLine($"| {r.Timestamp} | {r.StepName} | {r.Result} | {r.DurationMs}ms |");
            }
            
            return sb.ToString();
        }

        private class AuditRecord
        {
            public string Timestamp { get; set; } = "";
            public string Operator { get; set; } = "";
            public string Model { get; set; } = "";
            public string Barcode { get; set; } = "";
            public string StepName { get; set; } = "";
            public string StepCommand { get; set; } = "";
            public string Target { get; set; } = "";
            public string Result { get; set; } = "";
            public string Message { get; set; } = "";
            public int DurationMs { get; set; }
            public int MeasurementCount { get; set; }
            public string ErrorCode { get; set; } = "";
        }
    }
}
