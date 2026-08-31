using System;
using System.Collections.Generic;
using TradeAssistant.Application.Ports;
using TradeAssistant.Domain;

namespace TradeAssistant.Tests.Fakes
{
    public sealed class FakeOrderGateway : IOrderGateway
    {
        public sealed record EntryCall(TradeDirection Direction, int Quantity);
        public sealed record BracketCall(double StopPrice, double TpPrice, int Quantity);

        public List<EntryCall> Entries { get; } = new();
        public List<BracketCall> Brackets { get; } = new();
        public List<double> StopMoves { get; } = new();
        public int CancelAllCount { get; private set; }

        public bool ThrowOnSubmitMarketEntry { get; set; }
        public bool ThrowOnSubmitBracket { get; set; }
        public bool ThrowOnMoveStopTo { get; set; }

        public void SubmitMarketEntry(TradeDirection direction, int quantity)
        {
            if (ThrowOnSubmitMarketEntry) throw new InvalidOperationException("entry boom");
            Entries.Add(new EntryCall(direction, quantity));
        }

        public void SubmitBracket(double stopPrice, double tpPrice, int quantity)
        {
            if (ThrowOnSubmitBracket) throw new InvalidOperationException("bracket boom");
            Brackets.Add(new BracketCall(stopPrice, tpPrice, quantity));
        }

        public void MoveStopTo(double newStopPrice)
        {
            if (ThrowOnMoveStopTo) throw new InvalidOperationException("move boom");
            StopMoves.Add(newStopPrice);
        }

        public void CancelAll() => CancelAllCount++;

        public event Action<double> EntryFilled;
        public event Action<string> OrderRejected;
        public event Action<string> OrderCancelled;
        public event Action PositionClosed;

        public void FireEntryFilled(double fillPrice) => EntryFilled?.Invoke(fillPrice);
        public void FireOrderRejected(string reason) => OrderRejected?.Invoke(reason);
        public void FireOrderCancelled(string reason) => OrderCancelled?.Invoke(reason);
        public void FirePositionClosed() => PositionClosed?.Invoke();
    }
}
