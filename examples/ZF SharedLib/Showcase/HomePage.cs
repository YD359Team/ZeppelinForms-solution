using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ZeppelinForms;
using ZeppelinForms.Core.Text;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Effects;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZF_SharedLib.Showcase;

/// <summary>The welcome: the logo, the framework's version, the way to the sources.</summary>
internal static class HomePage
{
    private const string Repository = "https://github.com/YD359Team/ZeppelinForms-solution";

    public static UIElement Create()
    {
        var logo = new PictureBox { Size = new(128, 128), Padding = new(8) };
        logo.SetImage(Assets.LogoImage());
        logo.Glitch();

        var welcome = new RichLabel();
        welcome.SetText(
            "Welcome to ",
            TextRun.Colored("ZeppelinForms", MediaColors.Dotnet),
            " — a cross-platform UI framework drawn with SkiaSharp");

        var version = new Label { Text = $"Version {FrameworkVersion}, running on {RuntimeInformation.OSDescription}", TextStyle = TextStyle.Caption };

        var github = new PrimaryButton { Text = "Open the project on GitHub", Icon = Icons.Send, Padding = new(16, 10) };
        github.Click += (_, _) => Open(Repository);

        var hint = new Label { Text = "Every page of the menu is one subject: the controls, their styles, effects and behavior." };

        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            MainAxisAlignment = MainAxisAlignment.Center,
            CrossAxisAlignment = CrossAxisAlignment.Center,
            Padding = new(24),
            Children = { logo, welcome, version, github, hint },
        };

        return new GradientBorder(column)
        {
            Stops = [new(MediaColors.DotnetModern, 0), new(MediaColors.Dotnet, 1)],
            Angle = 135,
        };
    }

    internal static string FrameworkVersion =>
        typeof(UIElement).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(UIElement).Assembly.GetName().Version?.ToString(3)
        ?? "?";

    private static void Open(string url)
    {
        // a browser has no processes: the link opens through the page
        if (OperatingSystem.IsBrowser()) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // no browser to open it with
        }
    }
}
