using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Core.Collections;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Enums;

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
        DockPanel dockPanel = new DockPanel();
        dockPanel.Docking = Dock.Fill;
        Label label1 = new Label { Text = "Left", Docking = Dock.Left };
        Label label2 = new Label { Text = "Right", Docking = Dock.Right };
        Label label3 = new Label { Text = "Up", Docking = Dock.Top };
        Label label4 = new Label { Text = "Dawn", Docking = Dock.Bottom };
        Label label5 = new Label { Text = "Fill", Docking = Dock.Fill };
        dockPanel.Children.AddRange([label1, label2, label3, label4, label5]);
        this.Content = dockPanel;
    }
}
