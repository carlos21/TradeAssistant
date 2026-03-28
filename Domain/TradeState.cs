namespace TradeAssistant.Domain
{
    public enum TradeState
    {
        Idle,
        Planning,
        Armed,
        Submitted,
        Active,
        BreakEvenTriggered,
        Closed,
        Cancelled
    }
}
