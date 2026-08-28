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
        this.Font = "Segoe UI";

        Grid grid = new() 
        { 
            Columns = "*,*",
            Rows = "*,*",
        };
        Label lbl = new() { Text = "Label with box shadow\nline2\nline3", Column = 0, Row = 0, BoxShadow = BoxShadow.Large };
        Button btn = new() { Text = "Button with opacity", Column = 1, Row = 0, Opacity = 0.5f };
        CheckBox cb = new() { Text = "Check me", Column = 1, Row = 1 };
        grid.Children.AddRange([lbl, btn, cb]);

        this.Content = grid;
    }
}
