using NinjaTrader.NinjaScript;

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

        public double TickSize   => _script.Instrument.MasterInstrument.TickSize;
        public double TickValue  => _script.Instrument.MasterInstrument.PointValue * _script.Instrument.MasterInstrument.TickSize;
        public double PointValue => _script.Instrument.MasterInstrument.PointValue;
        public string InstrumentName => _script.Instrument.MasterInstrument.Name;
    }
}
