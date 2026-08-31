using System;
using TradeAssistant.Application.Ports;

namespace TradeAssistant.Tests.Fakes
{
    public sealed class FakeAccountDataProvider : IAccountDataProvider
    {
        public double AccountBalance { get; set; } = 100_000.0;
        public string AccountName { get; set; } = "Sim101";

        /// <summary>When true, AccountBalance throws — exercises controller error handling.</summary>
        public bool ThrowOnBalance { get; set; }

        double IAccountDataProvider.AccountBalance =>
            ThrowOnBalance ? throw new InvalidOperationException("account not ready") : AccountBalance;
    }
}
