using System;
using TradeAssistant.Application;
using TradeAssistant.Tests.Fakes;
using Xunit;

namespace TradeAssistant.Tests.Application
{
    public class ChartInteractionControllerTests
    {
        // Axis: price 19000 → y=0, 2 px/pt. Stop 19980 → y=1960.
        private readonly FakePriceAxisConverter _axis = new();
        private readonly ChartInteractionController _sut;

        public ChartInteractionControllerTests()
        {
            _sut = new ChartInteractionController(_axis, hitTolerancePx: 10);
            _sut.UpdateSnapshot(entryPrice: 20000, stopPrice: 19980, rectLeftX: 100, rectRightX: 300);
        }

        [Fact]
        public void Ctor_guards()
        {
            Assert.Throws<ArgumentNullException>(() => new ChartInteractionController(null));
            Assert.Throws<ArgumentException>(() => new ChartInteractionController(_axis, 0));
            Assert.Throws<ArgumentException>(() => new ChartInteractionController(_axis, -1));
        }

        [Fact]
        public void Default_tolerance_is_12px()
        {
            var c = new ChartInteractionController(_axis);
            c.UpdateSnapshot(20000, 19980, 100, 300);
            Assert.True(c.IsOverStopLine(200, 1960 + 11));
            Assert.False(c.IsOverStopLine(200, 1960 + 13));
        }

        [Theory]
        [InlineData(1950, true)]   // exactly at tolerance edge
        [InlineData(1970, true)]
        [InlineData(1960, true)]   // dead on the line
        [InlineData(1949, false)]  // beyond tolerance
        [InlineData(1971, false)]
        public void Hit_test_checks_y_distance_to_stop_line(double y, bool expected)
        {
            Assert.Equal(expected, _sut.IsOverStopLine(200, y));
        }

        [Theory]
        [InlineData(89.9, false)]  // left of bounds − tolerance
        [InlineData(90.0, true)]   // at bounds − tolerance
        [InlineData(310.0, true)]  // at bounds + tolerance
        [InlineData(310.1, false)]
        public void Hit_test_checks_x_within_extended_bounds(double x, bool expected)
        {
            Assert.Equal(expected, _sut.IsOverStopLine(x, 1960));
        }

        [Fact]
        public void Hit_test_false_when_prices_not_set()
        {
            var c = new ChartInteractionController(_axis, 10);
            c.UpdateSnapshot(0, 19980, 100, 300);
            Assert.False(c.IsOverStopLine(200, 1960));

            c.UpdateSnapshot(20000, 0, 100, 300);
            Assert.False(c.IsOverStopLine(200, 1960));
        }

        [Fact]
        public void Snapshot_normalizes_inverted_rect_bounds()
        {
            _sut.UpdateSnapshot(20000, 19980, 300, 100); // left/right swapped
            Assert.True(_sut.IsOverStopLine(200, 1960));
            Assert.False(_sut.IsOverStopLine(310.1, 1960));
        }

        [Fact]
        public void TryBeginDrag_starts_drag_on_hit_and_is_idempotent()
        {
            Assert.False(_sut.IsDragging);

            Assert.False(_sut.TryBeginDrag(200, 1000)); // off the line
            Assert.False(_sut.IsDragging);

            Assert.True(_sut.TryBeginDrag(200, 1960));
            Assert.True(_sut.IsDragging);

            Assert.True(_sut.TryBeginDrag(500, 500)); // anywhere while dragging
            Assert.True(_sut.IsDragging);
        }

        [Fact]
        public void TryDragTo_converts_y_to_raw_price()
        {
            Assert.False(_sut.TryDragTo(1900, out double raw));
            Assert.Equal(0, raw);

            _sut.TryBeginDrag(200, 1960);
            Assert.True(_sut.TryDragTo(1900, out raw));
            Assert.Equal(19950, raw); // 1900 / 2 + 19000
        }

        [Fact]
        public void EndDrag_and_CancelDrag_reset_state()
        {
            _sut.TryBeginDrag(200, 1960);
            _sut.EndDrag();
            Assert.False(_sut.IsDragging);
            Assert.False(_sut.TryDragTo(1900, out _));

            // Safe when not dragging.
            _sut.EndDrag();
            _sut.CancelDrag();

            _sut.TryBeginDrag(200, 1960);
            _sut.CancelDrag();
            Assert.False(_sut.IsDragging);
        }
    }
}
