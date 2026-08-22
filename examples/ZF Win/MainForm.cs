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
        Grid grid = new Grid();
        grid.ColumnDefinitions.Add(GridLength.Star(0.33f));
        grid.ColumnDefinitions.Add(GridLength.Star(0.33f));
        grid.ColumnDefinitions.Add(GridLength.Star(0.33f));
        grid.RowDefinitions.Add(GridLength.Star(1f));
        PictureBox pb1 = new PictureBox { Size = new(500, 500), Column = 0 };
        PictureBox pb2 = new PictureBox { Size = new(500, 500), Column = 1 };
        PictureBox pb3 = new PictureBox { Size = new(500, 500), Column = 2 };
        pb1.Load(@"C:\Users\ydav1\OneDrive\Изображения\Для видео\vecteezy_3d-yellow-angry-emoji_70808870.png");
        pb2.Load(@"C:\Users\ydav1\OneDrive\Изображения\Для видео\vecteezy_3d-yellow-crying-emoji_70808884.png");
        pb3.Load(@"C:\Users\ydav1\OneDrive\Изображения\Для видео\vecteezy_3d-yellow-laughing-emoji_70808865 (1).png");
        grid.Children.Add(pb1);
        grid.Children.Add(pb2);
        grid.Children.Add(pb3);
        this.Content = grid;
    }
}
