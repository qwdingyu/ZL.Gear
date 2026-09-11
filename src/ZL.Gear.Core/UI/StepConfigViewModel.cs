using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.UI
{
    /// <summary>
    /// StepConfig 的视图模型包装器。
    /// 专为 .NET 4.8 WinForms/WPF 设计，提供 INotifyPropertyChanged 支持。
    /// 解决了 StepConfig 回归 DTO 后 UI 无法响应更新的问题。
    /// </summary>
    public class StepConfigViewModel : INotifyPropertyChanged
    {
        private readonly StepConfig _model;

        public StepConfig Model => _model;

        public StepConfigViewModel(StepConfig model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public string StepName
        {
            get => _model.StepName;
            set
            {
                if (_model.StepName != value)
                {
                    _model.StepName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Command
        {
            get => _model.Command;
            set
            {
                if (_model.Command != value)
                {
                    _model.Command = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool Enable
        {
            get => _model.Enable;
            set
            {
                if (_model.Enable != value)
                {
                    _model.Enable = value;
                    OnPropertyChanged();
                }
            }
        }

        // 可以根据需要暴露更多属性...

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 批量更新 UI
        /// </summary>
        public void RefreshAll()
        {
            OnPropertyChanged(string.Empty);
        }
    }
}
