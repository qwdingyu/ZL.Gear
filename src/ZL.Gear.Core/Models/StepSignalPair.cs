using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Models
{
    public class StepSignalPair
    {
        public string Key { get; }
        public TaskCompletionSource<bool> StartSignal { get; }
        public CancellationTokenSource EndSignalCts { get; }

        public StepSignalPair(TaskCompletionSource<bool> startSignal, CancellationTokenSource endSignalCts)
            : this(null, startSignal, endSignalCts)
        {
        }

        public StepSignalPair(string key, TaskCompletionSource<bool> startSignal, CancellationTokenSource endSignalCts)
        {
            Key = key;
            StartSignal = startSignal;
            EndSignalCts = endSignalCts;
        }

        public void Deconstruct(out TaskCompletionSource<bool> start, out CancellationTokenSource end)
        {
            start = StartSignal;
            end = EndSignalCts;
        }

        public void Deconstruct(out string key, out TaskCompletionSource<bool> start, out CancellationTokenSource end)
        {
            key = Key;
            start = StartSignal;
            end = EndSignalCts;
        }
    }
}
