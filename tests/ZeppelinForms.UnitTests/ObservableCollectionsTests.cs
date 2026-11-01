using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
using ZeppelinForms.Analyzers;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Charts;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.20: the collections of the controls tell of their changes
/// (<see cref="ZfCollection{T}"/>) — a series added to a chart, a column to a grid, a tab
/// taken out — and the controls keep what they kept by an index with the item it meant.</summary>
[Collection("Platform")]
public class ObservableCollectionsTests
{
    private static (Form Form, HeadlessWindow Window) Show(UIElement content)
    {
        var form = new Form { Size = new Size(600, 400), Content = content };
        var window = (HeadlessWindow)new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, window);
    }

    // ===== the collection =====

    [Fact]
    public void BatchOperationsAreOneChangeEach()
    {
        var collection = new ZfCollection<int> { 3, 1, 2 };

        List<NotifyCollectionChangedEventArgs> changes = [];
        int counts = 0;
        collection.CollectionChanged += (_, e) => changes.Add(e);
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(collection.Count)) counts++;
        };

        collection.AddRange([4, 5, 6]);

        NotifyCollectionChangedEventArgs added = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, added.Action);
        Assert.Equal(3, added.NewStartingIndex);
        Assert.Equal(3, added.NewItems!.Count);

        collection.Sort();
        Assert.Equal([1, 2, 3, 4, 5, 6], collection);

        Assert.Equal(3, collection.RemoveAll(value => value % 2 == 0));
        Assert.Equal([1, 3, 5], collection);

        collection.ReplaceAll([7, 8]);
        Assert.Equal([7, 8], collection);

        Assert.Equal(4, changes.Count);
        Assert.Equal(NotifyCollectionChangedAction.Reset, changes[^1].Action);
        Assert.Equal(4, counts);
    }

    [Fact]
    public void SortKeepsTheOrderOfEqualItems()
    {
        var collection = new ZfCollection<(int Key, string Name)> { (2, "b1"), (1, "a"), (2, "b2"), (2, "b3") };

        collection.Sort((x, y) => x.Key.CompareTo(y.Key));

        Assert.Equal(["a", "b1", "b2", "b3"], collection.Select(item => item.Name));
    }

    [Fact]
    public void ReplaceAllMayTakeTheCollectionItself()
    {
        var collection = new ZfCollection<int> { 1, 2, 3 };

        collection.ReplaceAll(collection.Where(value => value > 1));

        Assert.Equal([2, 3], collection);
    }

    // ===== charts =====

    [Fact]
    public void AValueAddedToASeriesIsDrawn()
    {
        var series = new ChartSeries { Values = [1, 2, 3] };
        var chart = new LineChart { Categories = ["a", "b", "c"], Series = { series } };
        var (_, window) = Show(chart);

        int before = window.InvalidateCount;
        series.Values.Add(4);
        Assert.True(window.InvalidateCount > before, "a value added to a series was not drawn");

        before = window.InvalidateCount;
        chart.Categories.Add("d");
        Assert.True(window.InvalidateCount > before, "a category added was not drawn");

        // a series taken out of the chart is no longer heard
        chart.Series.Remove(series);
        before = window.InvalidateCount;
        series.Values.Add(5);
        Assert.Equal(before, window.InvalidateCount);
    }

    [Fact]
    public void ACollectionGivenInTheInitializerIsHeardToo()
    {
        var chart = new PieChart { Slices = [new PieSlice { Label = "One", Value = 1 }] };
        var (_, window) = Show(chart);

        float before = chart.DesiredSize.Width;
        int invalidated = window.InvalidateCount;

        chart.Slices.Add(new PieSlice { Label = "A much longer label than the first one", Value = 2 });
        Assert.True(window.InvalidateCount > invalidated);

        // the legend measures by its labels: the chart is measured again
        chart.FindOwner()!.UpdateLayout();
        Assert.True(chart.DesiredSize.Width > before);
    }

    [Fact]
    public void DataTakenAwayUnderThePointerIsForgotten()
    {
        var chart = new LineChart { Categories = ["a", "b", "c"], Series = { new ChartSeries { Values = [1, 2, 3] } } };
        var (form, _) = Show(chart);

        Point at = chart.GetAbsolutePosition();
        form.OnPointerMove(new Point(at.X + chart.ActualSize.Width * 0.8f, at.Y + chart.ActualSize.Height / 2f));
        Assert.True(chart.HoveredCategory >= 0);

        chart.Categories.Clear();
        chart.Series.Clear();

        Assert.Equal(-1, chart.HoveredCategory);
        ElementTreeRenderer.Draw(chart, new HeadlessGraphics());
    }

    // ===== layout =====

    [Fact]
    public void ARowAddedToAGridLaysItOut()
    {
        var second = new Label { Text = "second", Row = 1, VerticalAlignment = Forms.Enums.VerticalAlignment.Top };
        var grid = new Grid { Rows = "50,*", Children = { new Label { Text = "first" }, second } };
        var (form, _) = Show(grid);

        float Top() => second.GetAbsolutePosition().Y - grid.GetAbsolutePosition().Y;

        Assert.Equal(50f, Top(), 0.5f);

        // row 1 is the 50 px one now, under a new one of 30
        grid.RowDefinitions.Insert(0, GridLength.Fixed(30));
        form.UpdateLayout();

        Assert.Equal(30f, Top(), 0.5f);
    }

    [Fact]
    public void AMenuAddedToTheBarWidensIt()
    {
        var bar = new MenuBar { Items = { new MenuItem { Text = "File" } } };
        var (form, _) = Show(bar);

        float before = bar.DesiredSize.Width;

        bar.Items.Add(new MenuItem { Text = "Edit" });
        form.UpdateLayout();

        Assert.True(bar.DesiredSize.Width > before);
    }

    [Fact]
    public void ARowAddedToATableGrowsIt()
    {
        var table = new Table { Columns = { new TableColumn("Name", GridLength.Auto) } };
        table.Rows.Add(["one"]);
        var (form, _) = Show(table);

        float before = table.DesiredSize.Height;

        table.Rows.Add(["two"]);
        form.UpdateLayout();

        Assert.True(table.DesiredSize.Height > before);
    }

    // ===== what was kept by an index =====

    [Fact]
    public void TheSelectedTabStaysSelectedWhereverItMoves()
    {
        var a = new TabItem { Header = "A", Content = new Label { Text = "a" } };
        var b = new TabItem { Header = "B", Content = new Label { Text = "b" } };
        var c = new TabItem { Header = "C", Content = new Label { Text = "c" } };
        var tabs = new TabControl { Tabs = [a, b, c], SelectedIndex = 1 };
        Show(tabs);

        int changes = 0;
        tabs.SelectionChanged += (_, _) => changes++;

        tabs.Tabs.Insert(0, new TabItem { Header = "New" });

        Assert.Same(b, tabs.SelectedTab);
        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Equal(0, changes);

        // the selected one taken out: the tab that came to its place is selected
        tabs.Tabs.Remove(b);

        Assert.Same(c, tabs.SelectedTab);
        Assert.Same(c.Content, Assert.Single(tabs.Children));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheSelectedItemOfAComboBoxFollowsIt()
    {
        var combo = new ComboBox { Items = { "red", "green", "blue" }, SelectedIndex = 1 };
        Show(combo);

        int changes = 0;
        combo.SelectionChanged += (_, _) => changes++;

        combo.Items.Insert(0, "white");
        Assert.Equal("green", combo.SelectedItem);
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal(0, changes);

        combo.Items.Remove("green");
        Assert.Null(combo.SelectedItem);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheCheckedItemsOfACheckedComboBoxFollowThem()
    {
        var combo = new CheckedComboBox { Items = { "red", "green", "blue" } };
        Show(combo);

        combo.SetChecked(1, true);
        combo.SetChecked(2, true);

        combo.Items.RemoveAt(0);
        Assert.Equal(["green", "blue"], combo.CheckedItems);

        int changes = 0;
        combo.SelectionChanged += (_, _) => changes++;

        combo.Items.Remove("blue");
        Assert.Equal(["green"], combo.CheckedItems);
        Assert.Equal(1, changes);
    }

    private sealed record Person(string Name, int Age);

    [Fact]
    public void TheSortKeepsToItsColumn()
    {
        var name = new DataGridViewColumn { Header = "Name", Value = item => ((Person)item).Name };
        var age = new DataGridViewColumn { Header = "Age", Value = item => ((Person)item).Age };
        var grid = new DataGridView { Columns = { name, age } };
        grid.Items.Add(new Person("Bob", 30));
        grid.Items.Add(new Person("Alice", 25));
        Show(grid);

        grid.SortBy(1);
        Assert.Equal(1, grid.SortColumnIndex);

        grid.Columns.Insert(0, new DataGridViewColumn { Header = "First", Value = item => ((Person)item).Name[..1] });
        Assert.Equal(2, grid.SortColumnIndex);

        int sorts = 0;
        grid.SortChanged += (_, _) => sorts++;

        grid.Columns.Remove(age);
        Assert.Equal(-1, grid.SortColumnIndex);
        Assert.Equal(1, sorts);
    }

    // ===== ZF0001 =====

    [Fact]
    public void AZfCollectionMayBeGivenInAnInitializer()
    {
        const string code = """
            using System.Collections.ObjectModel;
            using ZeppelinForms.Forms.Controls.Charts;

            public class Model
            {
                public ObservableCollection<int> Plain { get; set; } = new();
            }

            public static class Use
            {
                public static object Chart() => new LineChart { Categories = ["a", "b"] };

                public static void Plain(Model model) => model.Plain = new ObservableCollection<int>();
            }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(UIElement).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Zf0001",
            [CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<Diagnostic> diagnostics = compilation
            .WithAnalyzers([new ObservableCollectionAssignmentAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();

        // the plain ObservableCollection replaced is still told of; the chart's isn't
        Diagnostic only = Assert.Single(diagnostics);
        Assert.Equal(ObservableCollectionAssignmentAnalyzer.DiagnosticId, only.Id);
        Assert.Contains("model.Plain", only.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }
}
