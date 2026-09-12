using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Protocol;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 可复用的 IMeasurable 实现，封装 MeasurementKit 标准采样流程。
    /// </summary>
    public class GenericSampler<T> : IMeasurable
    {
        private readonly IProtocolHandler _protocolHandler;
        private readonly string _command;
        private readonly SamplingConfig<T> _config;
        private readonly Func<string, IEnumerable<T>> _parser;
        private readonly StepContext _context;
        private readonly Action<string> _log;

        public GenericSampler(
            IProtocolHandler protocolHandler,
            string command,
            SamplingConfig<T> config,
            Func<string, IEnumerable<T>> parser,
            StepContext context,
            Action<string> log)
        {
            _protocolHandler = protocolHandler ?? throw new ArgumentNullException(nameof(protocolHandler));
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _log = log;
        }

        public async Task<ExecutionResultBase> MeasureAsync(CancellationToken token)
        {
            var measureKit = new MeasurementKit(_protocolHandler, _log);
            return await measureKit.SamplingAndAdaptAsync(_context, _command, _config, _parser, token);
        }
    }
}
