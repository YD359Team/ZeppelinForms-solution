using ZeppelinForms.Animation;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.DragDrop;

namespace ZF_SharedLib.Showcase;

/// <summary>Dragging: cards between lists of a board, and files from the system's
/// file manager.</summary>
internal static class DragDropPage
{
    public static UIElement Create() =>
        Demo.Page("Drag and drop", "Moving items between lists, and taking files from outside the application.",
            Demo.Section("Board", Board(), "Done only receives; In progress takes at most three; Notes is a group of its own"),
            Demo.Section("Files from the system", FileDrop(), "Drag files from the file manager onto the frame"));

    private static UIElement Board()
    {
        Label log = Demo.Note("Move a card to a neighboring list, or within its own");

        DragList backlog = Column("tasks");
        backlog.Items.AddRange([Card("Migrate Calendar"), Card("Write the README"), Card("WrapPanel"), Card("Table")]);

        DragList progress = Column("tasks");
        progress.Items.Add(Card("Drag and drop"));

        // a limit on work in progress: the very case
        // DragList's ReceivePredicate exists for
        progress.ReceivePredicate = (_, _) => progress.Items.Count < 3;

        // what is done can't be taken back: CanSendItem stops the drag on press
        DragList done = Column("tasks");
        done.CanSendItem = false;
        done.Items.Add(Card("StyledProperty"));

        // another group: cards of "tasks" can't be dropped here, though it's next to them
        DragList notes = Column("notes");
        notes.Items.AddRange([Card("Check XDND"), Card("Ask about brushes")]);

        foreach (DragList list in (DragList[])[backlog, progress, done, notes])
        {
            list.ItemSent += (_, args) =>
                log.Text = ReferenceEquals(args.Source, args.Target)
                    ? $"Moved within the list: {args.SourceIndex} → {args.TargetIndex}"
                    : "The card left its list";

            list.ItemReceived += (_, args) => log.Text = $"Received at position {args.TargetIndex}";
        }

        var board = new UniformGrid { Columns = 4 };
        board.Children.AddRange([
            Lane("Backlog", backlog),
            Lane("In progress (max 3)", progress),
            Lane("Done (receives only)", done),
            Lane("Notes (another group)", notes),
        ]);

        // four lanes side by side on a wide screen, two by two on a narrow one
        var adaptive = new LayoutBuilder
        {
            RebuildKey = size => size.Width < 560,
            Builder = size =>
            {
                bool narrow = size.Width < 560;
                board.Columns = narrow ? 2 : 4;
                board.Size = new(float.NaN, narrow ? 520 : 300);
                return board;
            },
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        root.Children.AddRange([adaptive, log]);
        return root;

        // the card is the row's container itself: ItemsControl wraps
        // in a Label only the data that is not a UIElement
        static UIElement Card(string text) => new Border
        {
            CornerRadius = new CornerRadius(4f),
            BorderWidth = 1f,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new Label { Text = text },
        };

        static DragList Column(string group) => new()
        {
            Group = group,
            Padding = new Thickness(6),
            OverflowY = Overflow.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // the cards around a drop make room smoothly
            ChildrenLayoutTransition = LayoutTransition.Ease(120, Easing.EaseInOut),
        };

        static UIElement Lane(string title, DragList list)
        {
            var header = new Label { Text = title, TextStyle = TextStyle.BodyStrong, Margin = new Thickness(6, 0, 6, 4) };
            header.Docking = Dock.Top;
            list.Docking = Dock.Fill;

            var lane = new DockPanel { Margin = new Thickness(4) };
            lane.Children.AddRange([header, list]);
            return lane;
        }
    }

    private static UIElement FileDrop()
    {
        var hint = new Label
        {
            Text = "Drop files here",
            HorizontalContentAlign = HorizontalContentAlignment.Center,
            VerticalContentAlign = VerticalContentAlignment.Center,
        };

        var dropped = new ListBox
        {
            OverflowY = Overflow.Auto,
            SelectionMode = SelectionMode.Extended,
            Size = new(float.NaN, 120),
        };

        var zone = new Border
        {
            AllowDrop = true,
            BorderWidth = 2f,
            CornerRadius = new CornerRadius(8f),
            Padding = new Thickness(24),
            Size = new Size(float.NaN, 120),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = hint,
        };

        // the frame's color at rest is taken later, in the first DragEnter:
        // the theme sets it when the zone is added to the form
        Color idleBorder = Colors.Transparent;

        zone.DragEnter += (_, args) =>
        {
            if (idleBorder.A == 0) idleBorder = zone.BorderColor;

            // the effect is required: without it the source shows "not allowed"
            // and the drop never comes
            args.Effect = args.Data.HasFiles ? DragDropEffect.Copy : DragDropEffect.None;

            if (args.Effect == DragDropEffect.None) return;

            zone.BorderColor = new Color(255, 0, 120, 215);
            hint.Text = "Release";
        };

        zone.DragLeave += (_, _) =>
        {
            zone.BorderColor = idleBorder;
            hint.Text = "Drop files here";
        };

        zone.Drop += (_, args) =>
        {
            zone.BorderColor = idleBorder;
            dropped.Items.Clear();

            foreach (string path in args.Data.Files)
                dropped.Items.Add(path);

            hint.Text = $"Dropped: {args.Data.Files.Count}";
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            CrossAxisAlignment = CrossAxisAlignment.Stretch,
        };
        root.Children.AddRange([zone, dropped]);
        return root;
    }
}
