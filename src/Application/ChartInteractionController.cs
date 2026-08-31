using System;
using TradeAssistant.Application.Ports;

namespace TradeAssistant.Application
{
    /// <summary>
    /// Pure, testable state machine for dragging the SL line on the chart.
    /// The indicator only translates raw mouse pixels; all hit-testing and
    /// drag-lifecycle decisions live here.
    ///
    /// Reliability rules (fix for "can't drag anymore"):
    ///  - Hit test requires BOTH X inside the rectangle bounds AND Y near the SL line.
    ///  - Begin is idempotent while already dragging.
    ///  - EndDrag / CancelDrag unconditionally reset the state — capture loss,
    ///    mouse-up off-chart, or Escape can never leave it stuck.
    /// </summary>
    public sealed class ChartInteractionController
    {
        private readonly IPriceAxisConverter _axis;
        private readonly double              _hitTolerancePx;

        private double _entryPrice;
        private double _stopPrice;
        private double _rectLeftX;
        private double _rectRightX;

        public ChartInteractionController(IPriceAxisConverter axis, double hitTolerancePx = 12.0)
        {
            _axis = axis ?? throw new ArgumentNullException(nameof(axis));
            if (hitTolerancePx <= 0)
                throw new ArgumentException("Hit tolerance must be positive.", nameof(hitTolerancePx));
            _hitTolerancePx = hitTolerancePx;
        }

        public bool IsDragging { get; private set; }

        /// <summary>
        /// Latest geometry/prices. Refreshed by the indicator on every render
        /// and tick so hit tests always use current values.
        /// </summary>
        public void UpdateSnapshot(double entryPrice, double stopPrice, double rectLeftX, double rectRightX)
        {
            _entryPrice = entryPrice;
            _stopPrice  = stopPrice;
            _rectLeftX  = Math.Min(rectLeftX, rectRightX);
            _rectRightX = Math.Max(rectLeftX, rectRightX);
        }

        /// <summary>True when (x, y) is over the SL line inside the rectangle bounds.</summary>
        public bool IsOverStopLine(double x, double y)
        {
            if (_stopPrice <= 0 || _entryPrice <= 0) return false;
            if (x < _rectLeftX - _hitTolerancePx || x > _rectRightX + _hitTolerancePx) return false;
            double slY = _axis.YFromPrice(_stopPrice);
            return Math.Abs(slY - y) <= _hitTolerancePx;
        }

        /// <summary>
        /// Starts a drag when the pointer is on the SL line. Idempotent:
        /// calling again while already dragging keeps the drag alive.
        /// Returns true when a drag is active after the call.
        /// </summary>
        public bool TryBeginDrag(double x, double y)
        {
            if (IsDragging) return true;
            if (!IsOverStopLine(x, y)) return false;
            IsDragging = true;
            return true;
        }

        /// <summary>
        /// While dragging, converts a Y pixel to a raw stop price.
        /// Returns false (and 0) when not dragging.
        /// </summary>
        public bool TryDragTo(double y, out double rawStopPrice)
        {
            rawStopPrice = 0;
            if (!IsDragging) return false;
            rawStopPrice = _axis.PriceFromY(y);
            return true;
        }

        /// <summary>Ends the drag (mouse up). Safe to call when not dragging.</summary>
        public void EndDrag()    { IsDragging = false; }

        /// <summary>Aborts the drag (capture lost / Escape). Safe to call anytime.</summary>
        public void CancelDrag() { IsDragging = false; }
    }
}
