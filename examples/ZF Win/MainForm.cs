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
        PictureBox pb = new PictureBox { Size = new(500, 500) };
        pb.Load(@"C:\Users\ydav1\OneDrive\Изображения\photo_2024-10-26_08-12-21.jpg");
        StackPanel panel = new StackPanel();
        panel.Children.Add(new Label { Text = "Label" });
        panel.Children.Add(Buttons.Primary("Primary"));
        panel.Children.Add(Buttons.Secondary("Secondary"));
        panel.Children.Add(pb);
        this.Content = panel;
    }
}
