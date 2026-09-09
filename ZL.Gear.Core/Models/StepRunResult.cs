using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.StepHandler
{
    /// <summary>
    /// 代表一个测试步骤在运行时的快照信息和最终结果。
    /// 这个对象是动态的，用于在执行过程中传递状态、更新UI，并最终用于聚合和保存。
    /// 它将 StepConfig (配置) 与运行时的易变数据 (状态、结果) 分离。
    /// </summary>
    public class StepRunResult : INotifyPropertyChanged
    {
        // INotifyPropertyChanged 实现，用于驱动UI更新
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private StepExecutionStatus _status;
        private StepOutcome _outcome;
        private string _message;
        private string _durationSeconds;

        public StepConfig StepConfig { get; }
        /// <summary>
        /// 关联的步骤业务标识符 (来自 StepConfig.StepKey)，用于唯一识别此结果对应的步骤。
        /// </summary>
        public string StepKey { get; }

        /// <summary>
        /// 步骤的显示名称 (来自 StepConfig.StepName)，用于UI展示。
        /// </summary>
        public string StepName { get; }

        /// <summary>
        /// 步骤的当前执行状态 (例如：Running, Completed)。
        /// </summary>
        public StepExecutionStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 步骤完成后的最终评估结果 (例如：Passed, Failed)。
        /// </summary>
        public StepOutcome Outcome
        {
            get => _outcome;
            set => SetProperty(ref _outcome, value);
        }

        public  string ResultString        {   get => Outcome == StepOutcome.Passed ? "OK" : "NG";    }

        /// <summary>
        /// 附加的执行信息或错误消息。
        /// </summary>
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        /// <summary>
        /// 步骤的执行耗时（秒）。
        /// </summary>
        public string DurationSeconds
        {
            get => _durationSeconds;
            set => SetProperty(ref _durationSeconds, value);
        }

        /// <summary>
        /// 步骤开始执行的时间戳。
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 步骤执行完成的时间戳。
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 此步骤执行后产生的所有测量结果的列表。
        /// 一个步骤可能产生多个测量值（例如，同时测量电压和电流）。
        /// </summary>
        // 新增明确的数据分离
        public List<Measurement> StepMeasurements { get; set; } = new();

        // 新增清晰的聚合视图
        public List<Measurement> AllMeasurements => StepMeasurements.Concat(SubStepResults.SelectMany(r => r.AllMeasurements)).ToList();
        /// <summary>
        /// 此步骤的子步骤的运行结果列表，构成了一个结果树。
        /// </summary>
        public List<StepRunResult> SubStepResults { get; } = new List<StepRunResult>();

        /// <summary>
        /// 扩展元数据，用于存储步骤特定的运行数据（如传感器原始状态等）
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// 构造函数，通常由执行器根据 StepConfig 创建。
        /// </summary>
        public StepRunResult(StepConfig config)
        {
            StepConfig = config;
            StepKey = config.StepKey;
            StepName = config.StepName;
            Status = StepExecutionStatus.Pending;
            Outcome = StepOutcome.NotEvaluated;
            Message = string.Empty;

            // 递归地为所有子步骤创建对应的 StepRunResult
            if (config.SubSteps != null && config.SubSteps.Any())
            {
                // 修复: 直接使用 LINQ 过滤启用的子步骤
                SubStepResults = config.SubSteps
                    .Where(sub => sub != null && sub.Enable)
                    .Select(sub => new StepRunResult(sub))
                    .ToList();
            }
        }

        /// <summary>
         /// 递归地判断此步骤及其所有子步骤是否都成功（Passed 或 Skipped）。
         /// 这是判断整个分支成功与否的最佳方式。
         /// </summary>
        public bool IsBranchSuccessful
        {
            get
            {
                // 1. 首先检查当前步骤自身的结果 -- 暂时不检测 中间节点（Group）
                // GROUP 容器自身 Outcome 常为 NotEvaluated；成败由子步骤 IsBranchSuccessful 决定（有意分层，勿改成看容器 Outcome）
                bool currentNodeSuccess =this.StepConfig.StepType?.ToUpper() =="GROUP" ? true:(this.Outcome == StepOutcome.Passed || this.Outcome == StepOutcome.Skipped);
                // 2. 如果当前节点已经失败，整个分支都失败，无需检查子节点（短路优化）
                if (!currentNodeSuccess)
                {
                    return false;
                }
                // 3. 如果当前节点成功，则递归地检查所有子节点的分支是否也都成功
                //    List.All() 在集合为空时会返回 true，这正是我们需要的行为（没有子步骤的节点仅取决于自身状态）
                return this.SubStepResults.All(subResult => subResult.IsBranchSuccessful);
            }
        }
    }
}
