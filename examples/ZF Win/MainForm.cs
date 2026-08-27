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

        Grid grid = new Grid 
        { 
            Columns = "*,*,*,*,*,*",
            Rows = "*,*,*,*,*,*,*",
        };
        DateTimePicker dtPicker = new();
        grid.Children.Add(dtPicker);

        this.Content = grid;
    }
}
