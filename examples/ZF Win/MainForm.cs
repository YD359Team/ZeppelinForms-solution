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

        UniformGrid grid = new() { Padding = 2f };
        UIElement[] controls = [
            new Label() { Text = "Label" },
            new Button() { Text = "Button" },
            new NumericUpDown(),
            new ProgressBar() { Maximum = 1f, Value = 0.5f },
            new CheckBox() { Text = "CheckBox" },
            new RadioButton() { Text = "RadioButton" },
            new TextBox() { Text = "TextBox" },
            new DateTimePicker(),
            new ListBox(),
            new Spoiler(),
        ];
        grid.Children.AddRange(controls);

        this.Content = grid;
    }
}
