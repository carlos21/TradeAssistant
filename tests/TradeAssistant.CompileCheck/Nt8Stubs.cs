// Compile-check stub. NinjaTrader generates the real `Indicator` partial class
// into NinjaTrader.Custom.dll (@Indicator.cs:
//   namespace NinjaTrader.NinjaScript.Indicators
//   { public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase {} })
// NinjaTrader.Custom.dll also contains previously compiled versions of this
// project's own types, so it cannot be referenced without CS0433 conflicts.
// This stub mirrors the real declaration. It is compiled ONLY into the
// TradeAssistant.CompileCheck project — NT8 never sees it (it is not under src/).
namespace NinjaTrader.NinjaScript.Indicators
{
    public abstract class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
    }
}
