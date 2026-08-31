namespace TradeAssistant.Application.Ports
{
    /// <summary>
    /// Provides account financial data.
    /// </summary>
    public interface IAccountDataProvider
    {
        double AccountBalance { get; }
        string AccountName    { get; }
    }
}
