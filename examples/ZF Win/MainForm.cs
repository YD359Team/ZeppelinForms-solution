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
        StackPanel panel = new() { Orientation = Orientation.Vertical };
        TextBox textBox = new();
        Button btn = new() { Text = "Show toast!" };
        panel.Children.AddRange([ textBox, btn ]);
        this.Content = panel;

        btn.Click += (_, _) => this.ShowToast(textBox.Text, 3000, ToastPosition.TopRight);
    }
}
