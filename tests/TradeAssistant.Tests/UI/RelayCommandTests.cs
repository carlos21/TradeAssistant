using System;
using TradeAssistant.UI;
using Xunit;

namespace TradeAssistant.Tests.UI
{
    public class RelayCommandTests
    {
        [Fact]
        public void Object_ctor_executes_with_parameter()
        {
            object seen = null;
            var cmd = new RelayCommand(p => seen = p);

            Assert.True(cmd.CanExecute("x")); // no predicate → always true
            cmd.Execute("hello");
            Assert.Equal("hello", seen);
        }

        [Fact]
        public void Object_ctor_honours_can_execute_predicate()
        {
            var cmd = new RelayCommand(_ => { }, p => p is int i && i > 0);

            Assert.True(cmd.CanExecute(5));
            Assert.False(cmd.CanExecute(-5));
            Assert.False(cmd.CanExecute("nope"));
        }

        [Fact]
        public void Parameterless_ctor_executes_action()
        {
            int calls = 0;
            var cmd = new RelayCommand(() => calls++);

            Assert.True(cmd.CanExecute(null));
            cmd.Execute(null);
            cmd.Execute(null);
            Assert.Equal(2, calls);
        }

        [Fact]
        public void Parameterless_ctor_honours_can_execute()
        {
            bool allowed = false;
            var cmd = new RelayCommand(() => { }, () => allowed);

            Assert.False(cmd.CanExecute(null));
            allowed = true;
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void Null_execute_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RelayCommand((Action<object>)null));

            // The parameterless ctor wraps the action in a lambda, so the null
            // surfaces only when the command is executed.
            var cmd = new RelayCommand((Action)null);
            Assert.Throws<NullReferenceException>(() => cmd.Execute(null));
        }

        [Fact]
        public void RaiseCanExecuteChanged_fires_event()
        {
            var cmd = new RelayCommand(() => { });
            int fired = 0;
            cmd.CanExecuteChanged += (_, _) => fired++;

            cmd.RaiseCanExecuteChanged();
            Assert.Equal(1, fired);
        }

        [Fact]
        public void RaiseCanExecuteChanged_without_subscribers_does_not_throw()
        {
            new RelayCommand(() => { }).RaiseCanExecuteChanged();
        }
    }
}
