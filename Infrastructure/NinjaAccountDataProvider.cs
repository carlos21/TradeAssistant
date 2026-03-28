using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace TradeAssistant.Infrastructure
{
    /// <summary>
    /// Reads account data from the NinjaTrader account API.
    /// </summary>
    public sealed class NinjaAccountDataProvider : IAccountDataProvider
    {
        private readonly NinjaScriptBase _script;

        public NinjaAccountDataProvider(NinjaScriptBase script)
        {
            _script = script;
        }

        public double AccountBalance
        {
            get
            {
                // Account.Get returns the cash value for the default account currency
                return _script.Account != null
                    ? _script.Account.Get(AccountItem.CashValue, Currency.UsDollar)
                    : 0;
            }
        }

        public string AccountName => _script.Account?.Name ?? "Unknown";
    }
}
