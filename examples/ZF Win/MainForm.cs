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

        this.Content = GetView1();
    }

    private UIElement GetView1()
    {
        DockPanel dockPanel = new DockPanel();
        Button btnNext = new Button() { Text = "Goto view 2", Docking = Dock.Top };
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
        btnNext.Click += BtnBack_Click;
        UniformGrid grid = new() { Padding = 6f };
        UIElement[] controls = [
            new Label() { Text = "Label" },
            new Button() { Text = "Button" },
            new NumericUpDown(),
            new ProgressBar() { Maximum = 1f, Value = 0.5f },
            new CheckBox() { Text = "CheckBox" },
            new RadioButton() { Text = "RadioButton" },
            new TextBox() { Text = "TextBox" },
            new TrackBar(),
            new DateTimePicker(),
            new Calendar(),
            new PictureBox(),
            new ListBox(),
            new Spoiler() { Child = new Label() { Text = "Hidden label" } },
            new CircularProgressBar() { Maximum = 1f, Value = 0.5f },
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
