using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Data;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.19: the data context — a view model set on an element or the form and
/// inherited below — and bindings to it: typed, compiled, along paths of several steps.</summary>
[Collection("Platform")]
public class DataContextBindingTests
{
    private abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Subscribers => PropertyChanged?.GetInvocationList().Length ?? 0;

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

        public int Quantity { get => field; set => Set(ref field, value); } = 1;

        public Customer? Customer { get => field; set => Set(ref field, value); }
    }

    private static BindingPath CustomerName() =>
        new("Customer.Name", typeof(OrderViewModel), typeof(string),
            new BindingStep("Customer", source => ((OrderViewModel)source).Customer),
            new BindingStep("Name", source => ((Customer)source).Name))
        {
            Set = (owner, value) => ((Customer)owner).Name = (string)value!,
        };

    [Fact]
    public void AnElementInheritsTheDataContextOfItsAncestorsAndTheForm()
    {
        var vm = new OrderViewModel();
        var label = new Label();
        var panel = new StackPanel { Children = { label } };
        var form = new Form { Content = panel };

        Assert.Null(label.DataContext);

        form.DataContext = vm;
        Assert.Same(vm, label.DataContext);

        var own = new OrderViewModel();
        panel.DataContext = own;
        Assert.Same(own, label.DataContext);
        Assert.Same(vm, form.DataContext);

        // null is "the parent's"
        panel.DataContext = null;
        Assert.Same(vm, label.DataContext);
    }

    [Fact]
    public void ATypedBindingFollowsTheDataContextAndItsChanges()
    {
        var vm = new OrderViewModel { Status = "Paid" };
        var label = new Label();
        var form = new Form { Content = new StackPanel { Children = { label } } };

        label.Bind(Label.TextProperty, (OrderViewModel m) => m.Status);
        Assert.Equal(Label.TextProperty.DefaultValue, label.Text);

        form.DataContext = vm;
        Assert.Equal("Paid", label.Text);

        vm.Status = "Shipped";
        Assert.Equal("Shipped", label.Text);

        var next = new OrderViewModel { Status = "Next" };
        form.DataContext = next;
        Assert.Equal("Next", label.Text);

        // the old view model is let go of
        Assert.Equal(0, vm.Subscribers);
    }

    [Fact]
    public void ATypedBindingBothWaysWritesTheViewModel()
    {
        var vm = new OrderViewModel { Status = "New" };
        var box = new TextBox { DataContext = vm };

        box.Bind(TextBox.TextProperty, (OrderViewModel m) => m.Status, (m, v) => m.Status = v!);
        Assert.Equal("New", box.Text);

        box.Text = "Typed";
        Assert.Equal("Typed", vm.Status);

        vm.Status = "Back";
        Assert.Equal("Back", box.Text);
    }

    [Fact]
    public void AnElementMovedUnderAnotherTakesItsDataContext()
    {
        var a = new OrderViewModel { Status = "A" };
        var b = new OrderViewModel { Status = "B" };
        var label = new Label();
        var left = new StackPanel { DataContext = a, Children = { label } };
        var right = new StackPanel { DataContext = b };

        label.Bind(Label.TextProperty, (OrderViewModel m) => m.Status);
        Assert.Equal("A", label.Text);

        left.Children.Remove(label);
        right.Children.Add(label);
        Assert.Equal("B", label.Text);
    }

    [Fact]
    public void DataContextChangedIsRaisedWhereTheEffectiveOneChanged()
    {
        var label = new Label();
        var inner = new StackPanel { Children = { label } };
        var outer = new StackPanel { Children = { inner } };

        int raised = 0;
        label.DataContextChanged += (_, _) => raised++;

        outer.DataContext = new OrderViewModel();
        Assert.Equal(1, raised);

        // below an element with its own, an ancestor's change changes nothing
        inner.DataContext = new OrderViewModel();
        Assert.Equal(2, raised);

        outer.DataContext = new OrderViewModel();
        Assert.Equal(2, raised);
    }

    private static BindingPath StatusText() =>
    new("Status", typeof(OrderViewModel), typeof(string),
        new BindingStep("Status", source => ((OrderViewModel)source).Status));

    [Fact]
    public void AValueThatDoesNotFitIsReportedNotThrown()
    {
        var vm = new OrderViewModel { Status = "3" };
        var label = new Label { DataContext = vm };

        var failures = new List<string>();
        void OnFailed(object? sender, string message) => failures.Add(message);

        Binding.ConversionFailed += OnFailed;

        try
        {
            label.Bind(ZeppelinForms.Forms.Controls.Base.UIElement.ColumnProperty, StatusText(), BindingMode.OneWay);
            Assert.Equal(3, label.Column);

            // 0.19.1: this was ZfContract.Fail — an exception in a debug build of the
            // framework, out of layout, and in the previewer on every frame
            vm.Status = "Paid";

            Assert.Equal(3, label.Column);   // the element keeps its value
            Assert.Contains(failures, message => message.Contains("'Paid'", StringComparison.Ordinal));

            vm.Status = "5";
            Assert.Equal(5, label.Column);
        }
        finally
        {
            Binding.ConversionFailed -= OnFailed;
        }
    }

    [Fact]
    public void APathFollowsEveryObjectOnTheWay()
    {
        var vm = new OrderViewModel();
        var box = new TextBox { DataContext = vm };

        box.Bind(TextBox.TextProperty, CustomerName(), BindingMode.TwoWay);

        // a null on the way: the property's default
        Assert.Equal(TextBox.TextProperty.DefaultValue, box.Text);

        var first = new Customer { Name = "Ann" };
        vm.Customer = first;
        Assert.Equal("Ann", box.Text);

        first.Name = "Anna";
        Assert.Equal("Anna", box.Text);

        box.Text = "Anne";
        Assert.Equal("Anne", first.Name);

        var second = new Customer { Name = "Bob" };
        vm.Customer = second;
        Assert.Equal("Bob", box.Text);

        // the old customer is no longer followed
        Assert.Equal(0, first.Subscribers);
        first.Name = "Ignored";
        Assert.Equal("Bob", box.Text);
    }

    [Fact]
    public void ADataContextOfAnotherTypeIsNoValue()
    {
        var label = new Label { DataContext = "not a view model" };

        label.Bind(Label.TextProperty, (OrderViewModel m) => m.Status);

        Assert.Equal(Label.TextProperty.DefaultValue, label.Text);
    }

    [Fact]
    public void AValueIsConvertedToTheElementsType()
    {
        var vm = new OrderViewModel { Quantity = 3 };
        var box = new TextBox { DataContext = vm };

        box.Bind(TextBox.TextProperty,
            new BindingPath("Quantity", typeof(OrderViewModel), typeof(int),
                new BindingStep("Quantity", source => ((OrderViewModel)source).Quantity))
            {
                Set = (owner, value) => ((OrderViewModel)owner).Quantity = (int)value!,
            },
            BindingMode.TwoWay);

        Assert.Equal("3", box.Text);

        box.Text = "12";
        Assert.Equal(12, vm.Quantity);

        // not a number yet: the view model keeps what it had
        box.Text = "-";
        Assert.Equal(12, vm.Quantity);
    }

    [Fact]
    public void AnExplicitSourceNeedsNoDataContext()
    {
        var vm = new OrderViewModel { Status = "Own" };
        var label = new Label { DataContext = new OrderViewModel { Status = "Context" } };

        label.Bind(Label.TextProperty, vm, m => m.Status);

        Assert.Equal("Own", label.Text);
    }

    [Fact]
    public void APropertyThatIsntStyledIsKeptSetByBindValue()
    {
        var vm = new CommandViewModel();
        var button = new Button();
        var form = new Form { Content = button };

        button.BindValue(nameof(Button.Command),
            new BindingPath("Save", typeof(CommandViewModel), typeof(System.Windows.Input.ICommand),
                new BindingStep("Save", source => ((CommandViewModel)source).Save)),
            static (element, value) => ((Button)element).Command = (System.Windows.Input.ICommand?)value);

        Assert.Null(button.Command);

        form.DataContext = vm;
        Assert.Same(vm.Save, button.Command);

        form.DataContext = null;
        Assert.Null(button.Command);
    }

    private sealed class CommandViewModel
    {
        public ZeppelinForms.Input.Commands.Command Save { get; } = new(() => { });
    }

    [Fact]
    public void ABindingDoesntKeepItsElementAlive()
    {
        var vm = new OrderViewModel();

        WeakReference Make()
        {
            var label = new Label { DataContext = vm };
            label.Bind(Label.TextProperty, (OrderViewModel m) => m.Status);
            return new WeakReference(label);
        }

        WeakReference element = Make();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(element.IsAlive);

        // and the next change lets the view model go of the binding
        vm.Status = "After";
        Assert.Equal(0, vm.Subscribers);
    }

    [Fact]
    public void AFormShownWithAViewModelShowsItsValues()
    {
        var vm = new OrderViewModel { Status = "Ready" };
        var label = new Label();
        var form = new Form { Size = new Size(200, 100), Content = new StackPanel { Children = { label } }, DataContext = vm };

        label.Bind(Label.TextProperty, (OrderViewModel m) => m.Status);
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        Assert.Equal("Ready", label.Text);
    }
}
