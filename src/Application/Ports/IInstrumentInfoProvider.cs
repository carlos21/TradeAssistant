namespace TradeAssistant.Application.Ports
{
    /// <summary>
    /// Provides instrument-specific tick data.
    /// Values must always come from Instrument.MasterInstrument — never hardcoded.
    /// </summary>
    public interface IInstrumentInfoProvider
    {
        double TickSize       { get; }
        double TickValue      { get; }
        double PointValue     { get; }
        string InstrumentName { get; }
    }
}
