using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
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

        UniformGrid grid = new();
        grid.Columns = 25;
        grid.Rows = 5;
        Label lbl = new() { Text = "Label with box shadow" };
        Button btn = new() { Text = "Button with opacity", Opacity = 0.5f };
        CheckBox cb = new() { Text = "Check me" };
        grid.Children.AddRange([lbl, btn, cb]);

        this.Content = grid;
    }
}
