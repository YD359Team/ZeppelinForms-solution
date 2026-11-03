using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Data;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.21: what a binding does with a value on the way — a converter, a format, the
/// values written where there is none — and the streams a path reads: a task's result, an
/// observable's latest value.</summary>
[Collection("Platform")]
public sealed class BindingOptionsTests
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
        public string? Name { get => field; set => Set(ref field, value); }
    }

    private sealed class ViewModel : Observable
    {
        public bool IsBusy { get => field; set => Set(ref field, value); }

        public string? Note { get => field; set => Set(ref field, value); }

        public double Total { get => field; set => Set(ref field, value); }

        public int Count { get => field; set => Set(ref field, value); }

        public Customer? Customer { get => field; set => Set(ref field, value); }

        public Task<string>? Title { get => field; set => Set(ref field, value); }

        public Task<Customer>? Profile { get => field; set => Set(ref field, value); }

        public IObservable<string>? Ticker { get => field; set => Set(ref field, value); }

        public object? Anything { get => field; set => Set(ref field, value); }
    }

    /// <summary>An observable of the values pushed into it, counting its observers.</summary>
    private sealed class Subject<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];

        public int Observers => _observers.Count;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            return new Unsubscriber(() => _observers.Remove(observer));
        }

        public void Push(T value)
        {
            foreach (IObserver<T> observer in _observers.ToArray())
                observer.OnNext(value);
        }

        private sealed class Unsubscriber(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    /// <summary>An observable that gives its current value as it is subscribed to, as a
    /// BehaviorSubject does.</summary>
    private sealed class Current<T>(T value) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
        {
            observer.OnNext(value);
            return new Subject<T>().Subscribe(observer);
        }
    }

    private static HeadlessPlatform Show(UIElement content)
    {
        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(300, 200), Content = content };

        platform.CreateWindow(form);
        form.UpdateLayout();

        return platform;
    }

    private static BindingPath PathOf<T>(string name, Func<ViewModel, T> get, Action<ViewModel, T>? set = null) =>
        new(name, typeof(ViewModel), typeof(T), new BindingStep(name, source => get((ViewModel)source)))
        {
            Set = set is null ? null : (source, value) => set((ViewModel)source, (T)value!),
        };

    // ===== options =====

    [Fact]
    public void AConverterTurnsTheValue()
    {
        var vm = new ViewModel { IsBusy = true };
        var label = new Label();

        label.Bind(UIElement.IsVisibleProperty, vm, PathOf(nameof(ViewModel.IsBusy), m => m.IsBusy), BindingMode.OneWay,
            new BindingOptions { Converter = BoolConverters.Not });

        Assert.False(label.IsVisible);

        vm.IsBusy = false;
        Assert.True(label.IsVisible);
    }

    [Fact]
    public void AConverterGetsItsParameterAndTheTargetType()
    {
        (object? Value, Type Type, object? Parameter)? seen = null;
        var converter = new Recording(call => seen = call);

        var vm = new ViewModel { Count = 3 };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, PathOf(nameof(ViewModel.Count), m => m.Count), BindingMode.OneWay,
            new BindingOptions { Converter = converter, ConverterParameter = "items" });

        Assert.Equal((3, typeof(string), "items"), seen);
        Assert.Equal("3 items", label.Text);
    }

    private sealed class Recording(Action<(object? Value, Type Type, object? Parameter)> seen) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            seen((value, targetType, parameter));
            return $"{value} {parameter}";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
    }

    [Fact]
    public void ANullIsWrittenAsTheTargetNullValue()
    {
        var vm = new ViewModel { Note = null };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, PathOf(nameof(ViewModel.Note), m => m.Note), BindingMode.OneWay,
            new BindingOptions { TargetNullValue = "(none)" });

        Assert.Equal("(none)", label.Text);

        vm.Note = "Fragile";
        Assert.Equal("Fragile", label.Text);
    }

    [Fact]
    public void APathWithNoValueWritesTheFallback()
    {
        var vm = new ViewModel();
        var label = new Label();

        var path = new BindingPath("Customer.Name", typeof(ViewModel), typeof(string),
            new BindingStep("Customer", source => ((ViewModel)source).Customer),
            new BindingStep("Name", source => ((Customer)source).Name));

        label.Bind(Label.TextProperty, vm, path, BindingMode.OneWay, new BindingOptions { FallbackValue = "—" });

        Assert.Equal("—", label.Text);

        vm.Customer = new Customer { Name = "Ann" };
        Assert.Equal("Ann", label.Text);

        vm.Customer = null;
        Assert.Equal("—", label.Text);
    }

    [Fact]
    public void AValueThatDoesntFitWritesTheFallback()
    {
        var vm = new ViewModel { Note = "not a number" };
        var label = new Label();

        // the fallback is text too: made the property's type as any bound value is
        label.Bind(UIElement.ColumnProperty, vm, PathOf(nameof(ViewModel.Note), m => m.Note), BindingMode.OneWay,
            new BindingOptions { FallbackValue = "2" });

        Assert.Equal(2, label.Column);

        vm.Note = "4";
        Assert.Equal(4, label.Column);
    }

    [Theory]
    [InlineData("{0:N2} kg", "1,234.50 kg")]
    [InlineData("N1", "1,234.5")]
    [InlineData("Total: {0}", "Total: 1234.5")]
    public void AFormatMakesTheValueText(string format, string expected)
    {
        var vm = new ViewModel { Total = 1234.5 };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, PathOf(nameof(ViewModel.Total), m => m.Total), BindingMode.OneWay,
            new BindingOptions { StringFormat = format, Culture = CultureInfo.InvariantCulture });

        Assert.Equal(expected, label.Text);
    }

    [Fact]
    public void UnsetValueFromAConverterIsNoValueAndDoNothingKeepsTheElement()
    {
        var vm = new ViewModel { Count = 1 };
        var label = new Label();

        var converter = new FuncValueConverter<int, object?>(count => count switch
        {
            0 => Binding.UnsetValue,
            < 0 => Binding.DoNothing,
            _ => count.ToString(CultureInfo.InvariantCulture),
        });

        label.Bind(Label.TextProperty, vm, PathOf(nameof(ViewModel.Count), m => m.Count), BindingMode.OneWay,
            new BindingOptions { Converter = converter, FallbackValue = "empty" });

        Assert.Equal("1", label.Text);

        vm.Count = 0;
        Assert.Equal("empty", label.Text);

        vm.Count = 5;
        vm.Count = -1;
        Assert.Equal("5", label.Text);
    }

    [Fact]
    public void BothWaysTheConverterConvertsBack()
    {
        var vm = new ViewModel { Count = 2 };
        var box = new TextBox();

        var dozens = new FuncValueConverter<int, string>(
            count => (count * 12).ToString(CultureInfo.InvariantCulture),
            text => int.Parse(text!, CultureInfo.InvariantCulture) / 12);

        box.Bind(TextBox.TextProperty, vm, PathOf(nameof(ViewModel.Count), m => m.Count, (m, v) => m.Count = v),
            BindingMode.TwoWay, new BindingOptions { Converter = dozens });

        Assert.Equal("24", box.Text);

        box.Text = "36";
        Assert.Equal(3, vm.Count);
    }

    [Fact]
    public void BothWaysTheTargetNullValueIsNullAtTheSource()
    {
        var vm = new ViewModel { Note = "x" };
        var box = new TextBox();

        box.Bind(TextBox.TextProperty, vm, PathOf(nameof(ViewModel.Note), m => m.Note, (m, v) => m.Note = v),
            BindingMode.TwoWay, new BindingOptions { TargetNullValue = "" });

        box.Text = "";
        Assert.Null(vm.Note);
    }

    [Fact]
    public void TheBuiltInConverters()
    {
        CultureInfo culture = CultureInfo.InvariantCulture;

        Assert.Equal(true, ObjectConverters.IsNull.Convert(null, typeof(bool), null, culture));
        Assert.Equal(false, ObjectConverters.IsNotNull.Convert(null, typeof(bool), null, culture));
        Assert.Equal(true, StringConverters.IsNullOrEmpty.Convert("", typeof(bool), null, culture));
        Assert.Equal(true, StringConverters.IsNotNullOrEmpty.Convert("a", typeof(bool), null, culture));
        Assert.Equal(false, BoolConverters.Not.ConvertBack(true, typeof(bool), null, culture));

        // one way: nothing goes back
        Assert.Same(Binding.DoNothing, ObjectConverters.IsNull.ConvertBack(true, typeof(object), null, culture));
    }

    // ===== streams =====

    private static BindingPath TitlePath() =>
        new("Title^", typeof(ViewModel), typeof(string),
            new BindingStep("Title", source => ((ViewModel)source).Title) { Stream = StreamOperator.FromTask<string>() });

    [Fact]
    public void ATasksResultIsWrittenWhenItIsThere()
    {
        var pending = new TaskCompletionSource<string>();
        var vm = new ViewModel { Title = pending.Task };
        var label = new Label();
        Show(label);

        label.Bind(Label.TextProperty, vm, TitlePath(), BindingMode.OneWay, new BindingOptions { FallbackValue = "Loading…" });

        Assert.Equal("Loading…", label.Text);

        pending.SetResult("Orders");
        Assert.Equal("Orders", label.Text);
    }

    [Fact]
    public void AFinishedTaskIsReadAtOnce()
    {
        var vm = new ViewModel { Title = Task.FromResult("Ready") };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, TitlePath(), BindingMode.OneWay);

        Assert.Equal("Ready", label.Text);
    }

    [Fact]
    public async Task AResultOnAWorkerIsWrittenOnTheUIThread()
    {
        var pending = new TaskCompletionSource<string>();
        var vm = new ViewModel { Title = pending.Task };
        var label = new Label();
        HeadlessPlatform platform = Show(label);

        label.Bind(Label.TextProperty, vm, TitlePath(), BindingMode.OneWay);

        await Task.Run(() => pending.SetResult("From a worker"), TestContext.Current.CancellationToken);

        platform.PumpAll();

        Assert.Equal("From a worker", label.Text);
    }

    [Fact]
    public void ATaskReplacedBeforeItEndsIsForgotten()
    {
        var first = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();
        var vm = new ViewModel { Title = first.Task };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, TitlePath(), BindingMode.OneWay);

        vm.Title = second.Task;
        second.SetResult("Second");
        first.SetResult("First");

        Assert.Equal("Second", label.Text);
    }

    [Fact]
    public void AFailedTaskGivesNoValue()
    {
        var pending = new TaskCompletionSource<string>();
        var vm = new ViewModel { Title = pending.Task };
        var label = new Label();

        label.Bind(Label.TextProperty, vm, TitlePath(), BindingMode.OneWay, new BindingOptions { FallbackValue = "?" });

        pending.SetException(new InvalidOperationException("offline"));

        Assert.Equal("?", label.Text);
    }

    [Fact]
    public void AStreamOnTheWayIsFollowedToItsValuesProperty()
    {
        var pending = new TaskCompletionSource<Customer>();
        var vm = new ViewModel { Profile = pending.Task };
        var label = new Label();

        var path = new BindingPath("Profile^.Name", typeof(ViewModel), typeof(string),
            new BindingStep("Profile", source => ((ViewModel)source).Profile) { Stream = StreamOperator.FromTask<Customer>() },
            new BindingStep("Name", source => ((Customer)source).Name));

        label.Bind(Label.TextProperty, vm, path, BindingMode.OneWay);
        Assert.Null(label.Text);

        var ann = new Customer { Name = "Ann" };
        pending.SetResult(ann);
        Assert.Equal("Ann", label.Text);

        // the result is followed as any object on the way
        ann.Name = "Anna";
        Assert.Equal("Anna", label.Text);
    }

    [Fact]
    public void AnObservablesLatestValueIsWritten()
    {
        var ticker = new Subject<string>();
        var vm = new ViewModel { Ticker = ticker };
        var label = new Label();

        label.Bind(Label.TextProperty, vm,
            new BindingPath("Ticker^", typeof(ViewModel), typeof(string),
                new BindingStep("Ticker", source => ((ViewModel)source).Ticker) { Stream = StreamOperator.FromObservable<string>() }),
            BindingMode.OneWay);

        Assert.Equal(1, ticker.Observers);

        ticker.Push("101.5");
        Assert.Equal("101.5", label.Text);

        ticker.Push("102.0");
        Assert.Equal("102.0", label.Text);

        // another observable on the path: the first is let go
        vm.Ticker = new Current<string>("99.9");

        Assert.Equal(0, ticker.Observers);
        Assert.Equal("99.9", label.Text);
    }

    [Fact]
    public void AnUnboundObservableIsLetGo()
    {
        var ticker = new Subject<string>();
        var vm = new ViewModel { Ticker = ticker };
        var label = new Label();

        label.Bind(Label.TextProperty, vm,
            new BindingPath("Ticker^", typeof(ViewModel), typeof(string),
                new BindingStep("Ticker", source => ((ViewModel)source).Ticker) { Stream = StreamOperator.FromObservable<string>() }),
            BindingMode.OneWay);

        label.Unbind(Label.TextProperty);

        Assert.Equal(0, ticker.Observers);
    }

    [Fact]
    public void AValueThatIsNoStreamIsTakenAsItIs()
    {
        var vm = new ViewModel { Anything = "plain" };
        var label = new Label();

        label.Bind(Label.TextProperty, vm,
            new BindingPath("Anything^", typeof(ViewModel), typeof(object),
                new BindingStep("Anything", source => ((ViewModel)source).Anything) { Stream = StreamOperator.FromTask<string>() }),
            BindingMode.OneWay);

        Assert.Equal("plain", label.Text);

        vm.Anything = Task.FromResult("from a task");
        Assert.Equal("from a task", label.Text);
    }
}
