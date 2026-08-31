using NinjaTrader.NinjaScript;
using TradeAssistant.Application.Ports;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Reads instrument data from NinjaTrader's MasterInstrument.
    /// Tick values come exclusively from the API — never hardcoded.
    /// </summary>
    public sealed class NinjaInstrumentInfoProvider : IInstrumentInfoProvider
    {
        private readonly NinjaScriptBase _script;

        public NinjaInstrumentInfoProvider(NinjaScriptBase script)
        {
            _script = script;
        }

        public double TickSize   => _script?.Instrument?.MasterInstrument?.TickSize ?? 0.25;
        public double TickValue  => _script?.Instrument?.MasterInstrument != null
            ? _script.Instrument.MasterInstrument.PointValue * _script.Instrument.MasterInstrument.TickSize
            : 0;
        public double PointValue => _script?.Instrument?.MasterInstrument?.PointValue ?? 0;
        public string InstrumentName => _script?.Instrument?.MasterInstrument?.Name ?? "Unknown";
    }
}
