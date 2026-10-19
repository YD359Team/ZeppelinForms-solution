using System.Collections.ObjectModel;
using System.ComponentModel;
using Xunit;
using ZeppelinForms.Data;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.17.0: a list control shows a data source and follows it; Items mirrors it.</summary>
[Collection("Platform")]
public class DataSourceTests
{
    private sealed record Person(string Name, int Age);

    private static Person[] People(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Person($"P{i}", 20 + i))];

    private static DataGridView Grid() => new()
    {
        Columns = { new DataGridViewColumn<Person, string>(person => person.Name) { Header = "Name" } },
    };

    private static Form Show(Forms.Controls.Base.UIElement content)
    {
        var form = new Form { Size = new Size(400, 300), Content = content };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();
        return form;
    }

    // ===== the grid =====

    [Fact]
    public void APlainListIsReadWhenSetAndOnRefresh()
    {
        List<Person> people = [.. People(3)];
        DataGridView grid = Grid();

        grid.DataSource = people;

        Assert.Equal(people, grid.Items);

        people.Add(new Person("late", 50));
        Assert.Equal(3, grid.Items.Count);

        grid.RefreshDataSource();
        Assert.Equal(4, grid.Items.Count);
    }

    [Fact]
    public void AnObservableCollectionIsFollowedAndTheSelectionKeepsItsRecord()
    {
        var people = new ObservableCollection<Person>(People(5));
        DataGridView grid = Grid();
        grid.DataSource = people;
        Show(grid);

        grid.SelectedIndex = 2;
        Person selected = people[2];

        people.Insert(0, new Person("first", 1));
        people.RemoveAt(5);

        Assert.Equal(people, grid.Items);
        Assert.Same(selected, grid.SelectedItem);
        Assert.Equal(3, grid.SelectedIndex);
    }

    [Fact]
    public void ABindingListIsFollowed()
    {
        var people = new BindingList<Person>([.. People(3)]);
        DataGridView grid = Grid();
        grid.DataSource = people;
        Show(grid);

        people.Add(new Person("added", 9));
        people.RemoveAt(0);

        Assert.Equal(people, grid.Items);
    }

    [Fact]
    public void BoundItemsCannotBeChangedByHand()
    {
        DataGridView grid = Grid();
        grid.DataSource = People(2);

        Assert.Throws<InvalidOperationException>(() => grid.Items.Add(new Person("x", 1)));
        Assert.Throws<InvalidOperationException>(() => grid.Items.Clear());

        // unbound, the items are the grid's own again, as the source left them
        grid.DataSource = null;
        grid.Items.Add(new Person("x", 1));

        Assert.Equal(3, grid.Items.Count);
    }

    [Fact]
    public void ADetachedGridDoesNotListen()
    {
        var people = new ObservableCollection<Person>(People(2));
        DataGridView grid = Grid();
        grid.DataSource = people;
        Form form = Show(grid);

        form.Content = null;
        people.Add(new Person("while away", 3));

        Assert.Equal(2, grid.Items.Count);

        // back in a form, it catches up
        form.Content = grid;

        Assert.Equal(3, grid.Items.Count);
    }

    [Fact]
    public void AChangeFromAnotherThreadReachesTheRowsOnTheUIThread()
    {
        var people = new ObservableCollection<Person>(People(2));
        DataGridView grid = Grid();
        grid.DataSource = people;

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(400, 300), Content = grid };
        platform.CreateWindow(form);

        Task.Run(() => people.Add(new Person("from a worker", 7))).Wait();

        // not applied on the worker: queued to the form's dispatcher
        Assert.Equal(2, grid.Items.Count);

        platform.PumpAll();

        Assert.Equal(3, grid.Items.Count);
        Assert.Same(people[2], grid.Items[2]);
    }

    // ===== DataSource<T> =====

    [Fact]
    public void AFilteredViewShowsWhatPassesAndFollowsTheFilter()
    {
        var people = new ObservableCollection<Person>(People(6));
        var view = new DataSource<Person>(people) { Filter = person => person.Age % 2 == 0 };
        DataGridView grid = Grid();
        grid.DataSource = view;
        Show(grid);

        Assert.Equal(3, grid.Items.Count);

        people.Add(new Person("even", 40));
        Assert.Equal(4, grid.Items.Count);

        view.Filter = null;
        Assert.Equal(7, grid.Items.Count);
    }

    [Fact]
    public void ANarrowedFilterKeepsTheListSelection()
    {
        var view = new DataSource<Person>(People(10));
        var list = new ListBox { DataSource = view };
        Show(list);

        list.SelectedIndex = 4;
        object selected = list.SelectedItem!;

        // the rows before it go; it stays selected, on its new row
        view.Filter = person => person.Age >= 22;

        Assert.Same(selected, list.SelectedItem);
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void AnOrderedViewOrders()
    {
        var view = new DataSource<Person>(People(4))
        {
            Comparer = Comparer<Person>.Create((left, right) => right.Age.CompareTo(left.Age)),
        };

        Assert.Equal(23, view[0].Age);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.IList)view).Add(view[0]));
    }

    // ===== the list and the combo box =====

    [Fact]
    public void AListBoxFollowsItsSource()
    {
        var names = new ObservableCollection<string>(["a", "b"]);
        var list = new ListBox { DataSource = names };
        Show(list);

        names.Add("c");

        Assert.Equal(["a", "b", "c"], list.Items);
        Assert.Throws<InvalidOperationException>(() => list.Items.Add("d"));
    }

    [Fact]
    public void AComboBoxKeepsItsPickWhileTheItemIsThere()
    {
        var names = new ObservableCollection<string>(["a", "b", "c"]);
        var combo = new ComboBox { DataSource = names };
        Show(combo);

        combo.SelectedIndex = 2;

        names.Insert(0, "first");
        Assert.Equal("c", combo.SelectedItem);
        Assert.Equal(3, combo.SelectedIndex);

        names.Remove("c");
        Assert.Null(combo.SelectedItem);
    }
}
