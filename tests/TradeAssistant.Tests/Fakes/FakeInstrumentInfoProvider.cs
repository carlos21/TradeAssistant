using TradeAssistant.Application.Ports;

namespace TradeAssistant.Tests.Fakes
{
    /// <summary>NQ-like instrument: tick 0.25, $20/pt → $5/tick.</summary>
    public sealed class FakeInstrumentInfoProvider : IInstrumentInfoProvider
    {
        public double TickSize { get; set; } = 0.25;
        public double TickValue { get; set; } = 5.0;
        public double PointValue { get; set; } = 20.0;
        public string InstrumentName { get; set; } = "NQ";
    }
}
