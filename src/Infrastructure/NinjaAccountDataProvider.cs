using NinjaTrader.Cbi;
using TradeAssistant.Application.Ports;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Reads account data from a NinjaTrader Account instance.
    /// </summary>
    public sealed class NinjaAccountDataProvider : IAccountDataProvider
    {
        private readonly Account _account;

        public NinjaAccountDataProvider(Account account)
        {
            _account = account;
        }

        public double AccountBalance =>
            _account != null
                ? _account.Get(AccountItem.CashValue, Currency.UsDollar)
                : 0;

        public string AccountName => _account?.Name ?? "Unknown";
    }
}
