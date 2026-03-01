using System;
using System.Collections.Generic;
using System.Threading;

namespace ZL.Gear.Core.Utils
{
    public sealed class PeriodicSender : IDisposable
    {
        private Thread _thread;
        private volatile bool _run;
        private readonly Action<string> _sendAction;
        private readonly object _locker = new object();
        private string[] _frames = new string[0];
        private int _periodMs = 100;
        public PeriodicSender(Action<string> sendAction)
        {
            _sendAction = sendAction;
        }
        public void Start(IEnumerable<string> frames, int periodMs)
        {
            lock (_locker)
            {
                _frames = frames == null ? new string[0] : new List<string>(frames).ToArray();
                _periodMs = Math.Max(1, periodMs);
                if (_run) return;
                _run = true;
                _thread = new Thread(RunLoop) { IsBackground = true };
                _thread.Start();
            }
        }
        public void Stop()
        {
            lock (_locker)
            {
                if (!_run) return;
                _run = false;
            }
            try
            {
                _thread?.Join(500);
            }
            catch { }
            _thread = null;
        }
        private void RunLoop()
        {
            try
            {
                while (_run)
                {
                    var copy = (string[])_frames.Clone();
                    if (copy.Length == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    foreach (var f in copy)
                    {
                        if (!_run) break;
                        try
                        {
                            _sendAction(f);
                        }
                        catch { }
                        Thread.Sleep(_periodMs);
                    }
                }
            }
            catch { }
        }
        public void Dispose()
        {
            Stop();
        }
    }
}