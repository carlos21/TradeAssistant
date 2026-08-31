using System.Collections.Generic;
using TradeAssistant.UI;
using Xunit;

namespace TradeAssistant.Tests.UI
{
    public class ViewModelBaseTests
    {
        private sealed class TestVm : ViewModelBase
        {
            private int _number;
            public int Number
            {
                get => _number;
                set => SetProperty(ref _number, value);
            }

            public void RaiseManual() => OnPropertyChanged();
            public void RaiseNamed(string name) => OnPropertyChanged(name);
        }

        [Fact]
        public void SetProperty_raises_only_on_change()
        {
            var vm = new TestVm();
            var raised = new List<string>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Number = 5;
            vm.Number = 5; // no change
            vm.Number = 7;

            Assert.Equal(new[] { "Number", "Number" }, raised);
            Assert.Equal(7, vm.Number);
        }

        [Fact]
        public void OnPropertyChanged_uses_caller_member_name_by_default()
        {
            var vm = new TestVm();
            string name = "unset";
            vm.PropertyChanged += (_, e) => name = e.PropertyName;

            vm.RaiseManual();
            Assert.Equal("RaiseManual", name);

            vm.RaiseNamed("Explicit");
            Assert.Equal("Explicit", name);
        }

        [Fact]
        public void No_subscribers_means_no_events_and_no_throw()
        {
            var vm = new TestVm();
            vm.Number = 1; // must not throw
            Assert.Equal(1, vm.Number);
        }
    }
}
