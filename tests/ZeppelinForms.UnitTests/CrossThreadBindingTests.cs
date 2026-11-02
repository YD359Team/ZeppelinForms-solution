using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Data;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;
using ZeppelinForms.Input.Commands;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: a view model told of a change on a worker thread. The element is
/// written on the form's thread, once for the changes told before its turn; a
/// command's CanExecuteChanged enables its button there too.</summary>
[Collection("Platform")]
public sealed class CrossThreadBindingTests
{
    private abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    private sealed class Customer : Observable
    {
        public string Name { get => field; set => Set(ref field, value); } = string.Empty;
    }

    private sealed class OrderViewModel : Observable
    {
        public string Status { get => field; set => Set(ref field, value); } = "New";

        public Customer? Customer { get => field; set => Set(ref field, value); }
    }

    private static BindingPath CustomerName() =>
        new("Customer.Name", typeof(OrderViewModel), typeof(string),
            new BindingStep("Customer", source => ((OrderViewModel)source).Customer),
            new BindingStep("Name", source => ((Customer)source).Name));

    private static HeadlessPlatform Show(UIElement content)
    {
        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(300, 200), Content = content };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return platform;
    }

    [Fact]
    public async Task AChangeToldOnAWorkerIsWrittenOnTheUIThread()
    {
        var vm = new OrderViewModel { Status = "Before" };
        var label = new Label();
        HeadlessPlatform platform = Show(label);

        label.Bind(Label.TextProperty, vm, m => m.Status);
        Assert.Equal("Before", label.Text);

        await Task.Run(() =>
        {
            vm.Status = "One";
            vm.Status = "Two";
        }, TestContext.Current.CancellationToken);

        // not written on the worker: queued to the form's dispatcher
        Assert.Equal("Before", label.Text);

        platform.PumpAll();

        Assert.Equal("Two", label.Text);
    }

    [Fact]
    public async Task AStepChangedOnAWorkerIsFollowedFromThere()
    {
        var vm = new OrderViewModel { Customer = new Customer { Name = "Ann" } };
        var label = new Label();
        HeadlessPlatform platform = Show(label);

        label.Bind(Label.TextProperty, vm, CustomerName(), BindingMode.OneWay);
        Assert.Equal("Ann", label.Text);

        var bob = new Customer { Name = "Bob" };
        await Task.Run(() => vm.Customer = bob, TestContext.Current.CancellationToken);

        platform.PumpAll();
        Assert.Equal("Bob", label.Text);

        // the new customer is the one followed now. The test may go on on another
        // thread than the window's after the await: the queue is pumped either way
        bob.Name = "Robert";
        platform.PumpAll();

        Assert.Equal("Robert", label.Text);
    }

    [Fact]
    public async Task AnElementOutOfEveryFormIsWrittenWhereItIsTold()
    {
        var vm = new OrderViewModel { Status = "Before" };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, m => m.Status);

        await Task.Run(() => vm.Status = "After", TestContext.Current.CancellationToken);

        Assert.Equal("After", label.Text);
    }

    [Fact]
    public async Task ACommandToldOnAWorkerDisablesItsButtonOnTheUIThread()
    {
        bool can = true;
        var command = new Command(() => { }, () => can);
        var button = new Button { Command = command };
        HeadlessPlatform platform = Show(button);

        Assert.True(button.IsEnabled);

        can = false;
        await Task.Run(command.NotifyCanExecuteChanged, TestContext.Current.CancellationToken);

        Assert.True(button.IsEnabled);

        platform.PumpAll();

        Assert.False(button.IsEnabled);
    }
}
