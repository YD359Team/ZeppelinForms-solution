using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;

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
        StackPanel panel = new StackPanel();
        panel.Children.Add(new Label { Text = "Label" });
        panel.Children.Add(Buttons.Primary("Primary"));
        panel.Children.Add(Buttons.Secondary("Secondary"));
        this.Content = panel;
    }
}
