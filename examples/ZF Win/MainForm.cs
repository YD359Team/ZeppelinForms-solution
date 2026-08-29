using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Forms.Layout;

namespace ZF_Win;

internal class MainForm : Form
{
    public MainForm()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        this.Title = "Form 1";
        this.Size = new Size(1024, 768);
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        this.Content = GetView1();
    }

    private UIElement GetView1()
    {
        DockPanel dockPanel = new DockPanel();
        Button btnNext = new Button() { Text = "Goto view 2", Docking = Dock.Top };
        btnNext.Click -= BtnNext_Click;
        btnNext.Click += BtnNext_Click;
        Label lbl = new Label();
        lbl.Text = "Presentation";
        dockPanel.Children.AddRange([lbl, btnNext]);
        return dockPanel;
    }

    private UIElement GetView2()
    {
        DockPanel dockPanel = new DockPanel();
        Button btnNext = new Button() { Text = "Goto view 1", Docking = Dock.Top };
        btnNext.Click -= BtnBack_Click;
        btnNext.Click += BtnBack_Click;
        UniformGrid grid = new() { Padding = 6f };
        PictureBox pBox = new();
        pBox.LoadAsset("Assets\\Laughing.png");
        ListBox lBox = new();
        lBox.Items.AddRange([new Button() { Text = "Item1"}, new Button() { Text = "Item2" }]);
        UIElement[] controls = [
            new Label() { Text = "Label" },
            new Button() { Text = "Button" },
            new Button() { Text = "Button with shadow", BoxShadow = BoxShadow.Large },
            new NumericUpDown(),
            new ProgressBar() { Maximum = 1f, Value = 0.5f },
            new CheckBox() { Text = "CheckBox" },
            new RadioButton() { Text = "RadioButton" },
            new TextBox() { Text = "TextBox" },
            new TrackBar(),
            new DateTimePicker(),
            new Calendar(),
            new TimePicker(),
            pBox,
            lBox,
            new Spoiler() { Child = new Label() { Text = "Hidden label" } },
            new CircularProgressBar() { Maximum = 1f, Value = 0.5f },
            new SvgIcon() { PathData = "M 45 45 L 345 45 L 345 345 L 45 345 Z M 195 45 L 195 345 M 45 195 L 345 195" },
        ];
        grid.Children.AddRange(controls);
        dockPanel.Children.AddRange([grid, btnNext]);
        return dockPanel;
    }

    private void BtnBack_Click(object? sender, ZeppelinForms.Input.Mouse.MouseClickEventArgs e)
    {
        this.Content = GetView1();
    }

    private void BtnNext_Click(object? sender, ZeppelinForms.Input.Mouse.MouseClickEventArgs e)
    {
        this.Content = GetView2();
    }
}
