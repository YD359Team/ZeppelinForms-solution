using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.DataGrid;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: a data grid follows its records without the mouse passing over it,
/// and a split view's pane button stays in place while the pane moves.</summary>
[Collection("Platform")]
public class GridRefreshAndPaneButtonTests
{
    /// <summary>A record that tells about its changes.</summary>
    private sealed class Process(string name, long memory) : INotifyPropertyChanged
    {
        public string Name { get; } = name;

        public long Memory
        {
            get => memory;
            set
            {
                memory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Memory)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>A plain record, updated without a word, as a timer does it.</summary>
    private sealed class PlainProcess(string name, long memory)
    {
        public string Name { get; } = name;
        public long Memory { get; set; } = memory;
    }

    private static (HeadlessPlatform Platform, DataGridView Grid) CreateGrid(params object[] records)
    {
        var grid = new DataGridView
        {
            Columns =
            {
                new DataGridViewColumn { Header = "Name", Value = item => item is Process p ? p.Name : ((PlainProcess)item).Name },
                new DataGridViewColumn { Header = "Memory", Value = item => item is Process p ? p.Memory : ((PlainProcess)item).Memory },
            },
        };

        foreach (object record in records) grid.Items.Add(record);

        var platform = new HeadlessPlatform();
        var form = new Form { Size = new Size(400, 300), Content = grid };
        platform.CreateWindow(form);
        form.UpdateLayout();

        // by memory, the largest first, as a task manager lists them
        grid.SortBy(1, descending: true);

        return (platform, grid);
    }

    [Fact]
    public void ARecordThatTellsIsSortedAgain()
    {
        var a = new Process("a", 100);
        var b = new Process("b", 200);
        var (platform, grid) = CreateGrid(a, b);

        Assert.Same(b, grid.RowItem(0));

        a.Memory = 300;
        platform.PumpAll();

        Assert.Same(a, grid.RowItem(0));
    }

    [Fact]
    public void ARecordThatLeftIsNotListenedTo()
    {
        var a = new Process("a", 100);
        var (platform, grid) = CreateGrid(a);

        grid.Items.Remove(a);
        a.Memory = 500;

        // nothing queued: the grid let go of the record with the row
        Assert.False(platform.Pump());
    }

    [Fact]
    public async Task PlainRecordsShowAfterRefreshFromAnotherThread()
    {
        var a = new PlainProcess("a", 100);
        var b = new PlainProcess("b", 200);
        var (platform, grid) = CreateGrid(a, b);

        // a timer's thread updates the records and asks once for the redraw
        await Task.Run(() =>
        {
            a.Memory = 300;
            grid.Refresh();
            grid.Refresh();
        }, TestContext.Current.CancellationToken);

        platform.PumpAll();

        Assert.Same(a, grid.RowItem(0));
    }

    // ===== the pane button =====

    private static (Form Form, SplitView View) CreateView(SplitViewDisplayMode mode)
    {
        var view = new SplitView { DisplayMode = mode };
        view.Items.Add(new SplitViewItem("Processes", content: () => new Label { Text = "processes" }));
        view.Items.Add(new SplitViewItem("Startup", content: () => new Label { Text = "startup" }));

        var form = new Form { Size = new Size(600, 400), Content = view };
        new HeadlessPlatform().CreateWindow(form);
        form.UpdateLayout();

        return (form, view);
    }

    [Theory]
    [InlineData(SplitViewDisplayMode.Inline)]
    [InlineData(SplitViewDisplayMode.Overlay)]
    public void ThePaneButtonStaysWhileThePaneMoves(SplitViewDisplayMode mode)
    {
        var (form, view) = CreateView(mode);
        SplitViewPaneButton button = view.Children.OfType<SplitViewPaneButton>().Single();

        Assert.True(button.IsVisible);
        Point closed = button.Position;

        // the pane starts to open: narrower than its own button yet
        view.IsPaneOpen = true;
        form.Clock.Advance(TimeSpan.FromMilliseconds(10));
        form.UpdateLayout();

        Assert.InRange(view.OpenProgress, 0.001f, 0.999f);
        Assert.True(button.IsVisible);
        Assert.Equal(closed, button.Position);

        // open: the pane's own button covers the place, and only one is drawn
        form.Clock.Advance(TimeSpan.FromSeconds(1));
        form.UpdateLayout();

        Assert.False(button.IsVisible);

        // closing: the button is back at once, in the same place, not after the pane is gone
        view.IsPaneOpen = false;
        form.Clock.Advance(TimeSpan.FromMilliseconds(10));
        form.UpdateLayout();

        Assert.True(button.IsVisible);
        Assert.Equal(closed, button.Position);
    }
}