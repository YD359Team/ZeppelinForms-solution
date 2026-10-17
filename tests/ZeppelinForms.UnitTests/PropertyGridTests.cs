using System.ComponentModel;
using PropertyDescriptor = ZeppelinForms.Forms.Controls.Tools.PropertyDescriptor;
using System.Numerics;
using Xunit;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Controls.Tools;
using ZeppelinForms.Headless;

namespace ZeppelinForms.UnitTests;

/// <summary>0.16: the property grid of a model editor — categories, editors by type and
/// by descriptor, ranges, several objects at once, one undo step per edit.</summary>
[Collection("Platform")]
public class PropertyGridTests
{
    [Flags]
    public enum Layers
    {
        None = 0,
        Geometry = 1,
        Bones = 2,
        Lights = 4,
        All = Geometry | Bones | Lights,
    }

    private sealed class Bone : INotifyPropertyChanged
    {
        public string Name { get; set; } = "Bone";
        public bool Visible { get; set; } = true;
        public float Weight { get; set; } = 0.5f;
        public int Parent { get; set; } = -1;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Layers Layers { get; set; } = Layers.Geometry;
        public Color Tint { get; set; } = Colors.Red;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static IReadOnlyList<PropertyDescriptor> Describe(object target) =>
    [
        new("Name", typeof(string), o => ((Bone)o).Name, (o, v) => ((Bone)o).Name = (string)v!) { Category = "Bone" },
        new("Visible", typeof(bool), o => ((Bone)o).Visible, (o, v) => ((Bone)o).Visible = (bool)v!) { Category = "Bone" },
        new("Weight", typeof(float), o => ((Bone)o).Weight, (o, v) => ((Bone)o).Weight = (float)v!)
        {
            Category = "Bone", Minimum = 0, Maximum = 1, DecimalPlaces = 3, Increment = 0.1,
            Description = "How much the bone moves its vertices",
        },
        new("Parent", typeof(int), o => ((Bone)o).Parent) { Category = "Bone" },
        new("Position", typeof(Vector3), o => ((Bone)o).Position, (o, v) => ((Bone)o).Position = (Vector3)v!) { Category = "Transform" },
        new("Rotation", typeof(Quaternion), o => ((Bone)o).Rotation, (o, v) => ((Bone)o).Rotation = (Quaternion)v!) { Category = "Transform" },
        new("Layers", typeof(Layers), o => ((Bone)o).Layers, (o, v) => ((Bone)o).Layers = (Layers)v!) { Category = "Display" },
        new("Tint", typeof(Color), o => ((Bone)o).Tint, (o, v) => ((Bone)o).Tint = (Color)v!) { Category = "Display" },
    ];

    private static PropertyGrid Grid(params object[] targets)
    {
        var grid = new PropertyGrid { PropertiesProvider = Describe };
        grid.SelectedObjects = targets;
        return grid;
    }

    private static IEnumerable<UIElement> Descendants(UIElement element)
    {
        yield return element;

        IEnumerable<UIElement> children = element switch
        {
            PanelControl panel => panel.Children,
            WrapControl { Child: { } child } => [child],
            _ => [],
        };

        foreach (UIElement child in children)
            foreach (UIElement descendant in Descendants(child))
                yield return descendant;
    }

    private static PropertyGridRow Row(PropertyGrid grid, string name) =>
        Descendants(grid).OfType<PropertyGridRow>().Single(row => row.Property.Name == name);

    private static T Editor<T>(PropertyGrid grid, string name) where T : UIElement =>
        Assert.IsType<T>(Row(grid, name).Editor);

    private static List<string> Shown(PropertyGrid grid) =>
        [.. Descendants(grid).OfType<PropertyGridRow>().Select(row => row.Property.Name)];

    // ===== categories and the filter =====

    [Fact]
    public void RowsComeUnderTheirCategories()
    {
        PropertyGrid grid = Grid(new Bone());

        List<string> categories = [.. Descendants(grid).OfType<PropertyGridCategory>().Select(c => c.CategoryName)];

        Assert.Equal(["Bone", "Transform", "Display"], categories);
        Assert.Equal(8, Shown(grid).Count);
    }

    [Fact]
    public void AFoldedCategoryHidesItsRowsAndStaysFolded()
    {
        PropertyGrid grid = Grid(new Bone());

        grid.ToggleCategory("Transform");
        Assert.DoesNotContain("Position", Shown(grid));

        // another object: the category is still folded, as the user left it
        grid.SelectedObject = new Bone();
        Assert.DoesNotContain("Position", Shown(grid));
        Assert.Contains("Name", Shown(grid));
    }

    [Fact]
    public void TheFilterFindsRowsEvenInAFoldedCategory()
    {
        PropertyGrid grid = Grid(new Bone());
        grid.ToggleCategory("Transform");

        grid.Filter = "rot";

        Assert.Equal(["Rotation"], Shown(grid));
    }

    [Fact]
    public void WithoutCategoriesTheRowsGoByName()
    {
        PropertyGrid grid = Grid(new Bone());
        grid.GroupByCategory = false;

        Assert.Empty(Descendants(grid).OfType<PropertyGridCategory>());
        Assert.Equal(["Layers", "Name", "Parent", "Position", "Rotation", "Tint", "Visible", "Weight"], Shown(grid));
    }

    // ===== editors =====

    [Fact]
    public void EachTypeGetsItsEditor()
    {
        PropertyGrid grid = Grid(new Bone());

        Editor<TextBox>(grid, "Name");
        Editor<CheckBox>(grid, "Visible");
        Editor<NumericUpDown>(grid, "Weight");
        Editor<Label>(grid, "Parent");
        Editor<PropertyGridComponents>(grid, "Position");
        Editor<CheckedComboBox>(grid, "Layers");
        Editor<ColorPicker>(grid, "Tint");
    }

    [Fact]
    public void ANumberTakesTheDescriptorsRangeAndPrecision()
    {
        NumericUpDown weight = Editor<NumericUpDown>(Grid(new Bone()), "Weight");

        Assert.Equal(0m, weight.Minimum);
        Assert.Equal(1m, weight.Maximum);
        Assert.Equal(3, weight.DecimalPlaces);
        Assert.Equal(0.1m, weight.Step);
        Assert.Equal(0.5m, weight.Value);
    }

    [Fact]
    public void AFlagsEnumListsItsSingleFlags()
    {
        CheckedComboBox layers = Editor<CheckedComboBox>(Grid(new Bone { Layers = Layers.Geometry | Layers.Lights }), "Layers");

        Assert.Equal([Layers.Geometry, Layers.Bones, Layers.Lights], layers.Items.Cast<Layers>());
        Assert.Equal([Layers.Geometry, Layers.Lights], layers.CheckedItems.Cast<Layers>());
    }

    [Fact]
    public void CheckingAFlagWritesTheSum()
    {
        var bone = new Bone { Layers = Layers.Geometry };
        CheckedComboBox layers = Editor<CheckedComboBox>(Grid(bone), "Layers");

        layers.SetChecked(1, true);

        Assert.Equal(Layers.Geometry | Layers.Bones, bone.Layers);
    }

    [Fact]
    public void AVectorIsEditedByItsParts()
    {
        var bone = new Bone { Position = new Vector3(1, 2, 3) };
        PropertyGrid grid = Grid(bone);

        TextBox[] fields = [.. Descendants(Row(grid, "Position").Editor).OfType<TextBox>()];
        Assert.Equal(3, fields.Length);

        int changes = 0;
        grid.PropertyValueChanged += (_, _) => changes++;

        fields[1].Text = "7.5";
        fields[1].RaiseLostFocus();

        Assert.Equal(new Vector3(1, 7.5f, 3), bone.Position);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ADescriptorsOwnEditorComesFirst()
    {
        var mark = new Label { Text = "custom" };
        var bone = new Bone();

        var grid = new PropertyGrid
        {
            PropertiesProvider = _ =>
            [
                new("Name", typeof(string), o => ((Bone)o).Name, (o, v) => { }) { Editor = _ => mark },
            ],
            SelectedObject = bone,
        };

        Assert.Same(mark, Row(grid, "Name").Editor);
    }

    [Fact]
    public void TheGridsEditorsComeBeforeTheBuiltInOnes()
    {
        var grid = new PropertyGrid { PropertiesProvider = Describe };
        grid.Editors[typeof(Vector3)] = context => new Label { Text = $"vector {context.Value}" };

        grid.SelectedObject = new Bone();

        Assert.StartsWith("vector", Editor<Label>(grid, "Position").Text);
    }

    // ===== writing and undo =====

    [Fact]
    public void AnEditIsOneStepHoweverManyChangesItHas()
    {
        var bone = new Bone { Weight = 0.5f };
        PropertyGrid grid = Grid(bone);

        List<PropertyValueChangedEventArgs> steps = [];
        grid.PropertyValueChanged += (_, e) => steps.Add(e);

        NumericUpDown weight = Editor<NumericUpDown>(grid, "Weight");
        weight.Value = 0.6m;
        weight.Value = 0.7m;
        weight.Value = 0.8m;

        Assert.Equal(0.8f, bone.Weight);
        Assert.Empty(steps);

        weight.RaiseLostFocus();

        PropertyValueChangedEventArgs step = Assert.Single(steps);
        Assert.Equal(0.5f, step.OldValues[0]);
        Assert.Equal(0.8f, step.NewValue);

        step.Undo();
        Assert.Equal(0.5f, bone.Weight);

        step.Redo();
        Assert.Equal(0.8f, bone.Weight);
    }

    [Fact]
    public void ChangingAnotherPropertyEndsTheEdit()
    {
        PropertyGrid grid = Grid(new Bone());

        int steps = 0;
        grid.PropertyValueChanged += (_, _) => steps++;

        Editor<NumericUpDown>(grid, "Weight").Value = 0.9m;
        Editor<CheckBox>(grid, "Visible").IsChecked = false;

        Assert.Equal(2, steps);
    }

    [Fact]
    public void AValueCanBeRefused()
    {
        var bone = new Bone { Name = "Head" };
        PropertyGrid grid = Grid(bone);

        grid.PropertyValueChanging += (_, e) => e.Cancel = e.NewValue is "";

        TextBox name = Editor<TextBox>(grid, "Name");
        name.Text = "";

        Assert.Equal("Head", bone.Name);
    }

    [Fact]
    public void ANewSelectionCommitsTheEditInProgress()
    {
        PropertyGrid grid = Grid(new Bone());

        int steps = 0;
        grid.PropertyValueChanged += (_, _) => steps++;

        Editor<TextBox>(grid, "Name").Text = "Spine";
        grid.SelectedObject = new Bone();

        Assert.Equal(1, steps);
    }

    // ===== several objects =====

    [Fact]
    public void SeveralObjectsShareTheirCommonRows()
    {
        var bone = new Bone();
        var label = new Label();

        string labelName = "label";

        var grid = new PropertyGrid
        {
            PropertiesProvider = o => o is Bone
                ? Describe(o)
                : [new("Name", typeof(string), _ => labelName, (_, v) => labelName = (string)v!)],
        };

        grid.SelectedObjects = [bone, label];

        Assert.Equal(["Name"], Shown(grid));

        // each object written through its own descriptor
        Row(grid, "Name").Context.Set("Root");

        Assert.Equal("Root", bone.Name);
        Assert.Equal("Root", labelName);
    }

    [Fact]
    public void DifferentValuesShowNoneAndAWriteGoesToAll()
    {
        var a = new Bone { Weight = 0.2f };
        var b = new Bone { Weight = 0.4f };
        PropertyGrid grid = Grid(a, b);

        PropertyGridRow row = Row(grid, "Weight");
        Assert.True(row.Context.IsMixed);

        List<PropertyValueChangedEventArgs> steps = [];
        grid.PropertyValueChanged += (_, e) => steps.Add(e);

        row.Context.Set(0.9f);

        Assert.Equal(0.9f, a.Weight);
        Assert.Equal(0.9f, b.Weight);
        Assert.Equal([0.2f, 0.4f], Assert.Single(steps).OldValues.Cast<float>());
    }

    // ===== changes from outside =====

    [Fact]
    public void AChangeFromOutsideShowsInTheRow()
    {
        var bone = new Bone { Name = "Head" };
        PropertyGrid grid = Grid(bone);

        bone.Name = "Skull";
        bone.Raise(nameof(Bone.Name));

        Assert.Equal("Skull", Editor<TextBox>(grid, "Name").Text);
    }

    [Fact]
    public void TheGridsOwnWritesDontRebuildIt()
    {
        var bone = new Bone();
        PropertyGrid grid = Grid(bone);

        TextBox name = Editor<TextBox>(grid, "Name");
        name.Text = "Spine";
        bone.Raise(nameof(Bone.Name));

        Assert.Same(name, Editor<TextBox>(grid, "Name"));
    }

    // ===== the inspector's own rows =====

    [Fact]
    public void AnElementsRowsSayWhereTheValueCameFrom()
    {
        var label = new Label { Text = "x", Padding = new Thickness(3) };
        var grid = new PropertyGrid { SelectedObject = label };

        string? tip = Row(grid, "Padding").GetToolTip(Row(grid, "Padding").GetAbsolutePosition());

        Assert.Contains("Set in code", tip);
    }

    [Fact]
    public void TheColumnBorderStaysInsideTheGrid()
    {
        var grid = new PropertyGrid { NameColumnRatio = 2f };
        Assert.Equal(0.85f, grid.NameColumnRatio);

        grid.NameColumnRatio = 0f;
        Assert.Equal(0.15f, grid.NameColumnRatio);
    }

    [Fact]
    public void TheTitleSaysWhatIsShown()
    {
        PropertyGrid grid = Grid(new Bone(), new Bone());

        Label title = Descendants(grid).OfType<Label>().First();

        Assert.Equal("2 objects", title.Text);
    }
}
