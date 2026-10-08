using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using ZeppelinForms.Design.Protocol;
using Key = System.Windows.Input.Key;

namespace ZeppelinForms.VisualStudio;

/// <summary>
/// The preview window's content: a list of the project's previews, the settings to
/// show them with, and the live picture, which takes the mouse and the keyboard.
/// </summary>
/// <remarks>
/// <para>
/// Built in code rather than XAML: there are few controls, and nothing here is
/// worth a designer of its own.
/// </para>
/// <para>
/// Frames arrive on the pipe's thread, often. Only the latest is kept, and the UI
/// thread is asked to show it once; frames that came in between are dropped, which is
/// what a screen does with frames it had no time for.
/// </para>
/// </remarks>
internal sealed class PreviewControl : UserControl
{
    private static readonly string[] Themes =
        ["", "Light", "Dark", "FluentLight", "FluentDark", "HighContrastBlack", "HighContrastWhite"];

    private static readonly string[] Cultures = ["", "en-US", "ru-RU", "de-DE", "ja-JP", "ar-SA", "he-IL"];

    private static readonly (string Name, double Width, double Height)[] Sizes =
    [
        ("Fit the window", -1, -1),
        ("The preview's own", 0, 0),
        ("Phone 360 × 640", 360, 640),
        ("Tablet 768 × 1024", 768, 1024),
        ("Desktop 1280 × 800", 1280, 800),
    ];

    private static readonly float[] TextScales = [1f, 1.25f, 1.5f, 2f];

    private readonly ComboBox _list = new() { MinWidth = 220, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _theme = new() { MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _size = new() { MinWidth = 140, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _culture = new() { MinWidth = 80, Margin = new Thickness(0, 0, 6, 0), IsEditable = true };
    private readonly ComboBox _textScale = new() { MinWidth = 64, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ToggleButton _rtl = new() { Content = "RTL", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _reload = new() { Content = "Reload", Padding = new Thickness(6, 0, 6, 0) };

    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private readonly Border _surface = new()
    {
        Focusable = true,
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12),
    };

    private readonly Image _image = new() { Stretch = Stretch.None, SnapsToDevicePixels = true };

    private readonly TextBox _error = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"),
        Visibility = Visibility.Collapsed,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12),
    };

    private readonly TextBlock _status = new() { Margin = new Thickness(6, 3, 6, 3), TextTrimming = TextTrimming.CharacterEllipsis };

    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _release = new() { Interval = TimeSpan.FromMinutes(2) };

    private DesignerService? _designer;
    private bool _holdsHost;

    private IList<PreviewInfo> _previews = [];
    private string? _selectedId;
    private bool _updatingList;

    private WriteableBitmap? _bitmap;
    private FrameMessage? _pendingFrame;
    private int _frameScheduled;

    public PreviewControl()
    {
        foreach (string theme in Themes) _theme.Items.Add(theme.Length == 0 ? "Preview's theme" : theme);
        foreach (string culture in Cultures) _culture.Items.Add(culture.Length == 0 ? "Culture" : culture);
        foreach (var size in Sizes) _size.Items.Add(size.Name);
        foreach (float scale in TextScales) _textScale.Items.Add($"{scale:P0}");

        _theme.SelectedIndex = 0;
        _culture.SelectedIndex = 0;
        _size.SelectedIndex = 0;
        _textScale.SelectedIndex = 0;

        var toolbar = new WrapPanel { Margin = new Thickness(6) };

        foreach (UIElement item in new UIElement[] { _list, _theme, _size, _culture, _textScale, _rtl, _reload })
            toolbar.Children.Add(item);

        _surface.Child = _image;
        _scroll.Content = _surface;

        var center = new Grid();
        center.Children.Add(_scroll);
        center.Children.Add(_error);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_status);
        root.Children.Add(center);

        Content = root;

        // the colors of the Visual Studio theme, light or dark
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _error.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        _error.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        _list.SelectionChanged += (_, _) => OnPreviewChosen();
        _theme.SelectionChanged += (_, _) => SendSettings();
        _size.SelectionChanged += (_, _) => SendSettings();
        _textScale.SelectionChanged += (_, _) => SendSettings();
        _culture.SelectionChanged += (_, _) => SendSettings();
        _culture.LostKeyboardFocus += (_, _) => SendSettings();
        _rtl.Click += (_, _) => SendSettings();
        _reload.Click += (_, _) => _designer?.Refresh(force: true);

        _scroll.SizeChanged += (_, _) =>
        {
            if (IsFitMode)
            {
                _resize.Stop();
                _resize.Start();
            }
        };

        _resize.Tick += (_, _) =>
        {
            _resize.Stop();
            SendSettings();
        };

        _release.Tick += (_, _) =>
        {
            _release.Stop();
            ReleaseHost();
        };

        HookInput();

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) OnShown();
            else _release.Start();
        };

        SetStatus("Open a document of a ZeppelinForms project.");
    }

    // ===== the host =====

    private void OnShown()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _release.Stop();

        if (_designer is null && ZeppelinFormsPackage.Instance is { } package)
        {
            _designer = package.Designer;
            _designer.Received += OnReceived;
            _designer.HostStarted += OnHostStarted;
            _designer.HostFailed += OnHostFailed;
            _designer.DocumentActivated += OnDocumentActivated;
        }

        if (_designer is not null && !_holdsHost)
        {
            _holdsHost = true;
            _designer.Acquire();
        }
    }

    /// <summary>Hidden for a while: let the host go. It starts again when the window is shown.</summary>
    private void ReleaseHost()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!_holdsHost || _designer is null) return;

        _holdsHost = false;
        _designer.Release();
    }

    /// <summary>The window is destroyed.</summary>
    public void Detach()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ReleaseHost();

        if (_designer is null) return;

        _designer.Received -= OnReceived;
        _designer.HostStarted -= OnHostStarted;
        _designer.HostFailed -= OnHostFailed;
        _designer.DocumentActivated -= OnDocumentActivated;
        _designer = null;
    }

    private void OnHostStarted()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SetStatus(_designer?.AssemblyPath is { } path ? $"Loading {Path.GetFileName(path)}…" : "No project.");
    }

    private void OnHostFailed(string reason) => OnUi(() => SetStatus(reason));

    private void OnReceived(DesignerMessage message)
    {
        switch (message)
        {
            case FrameMessage frame:
                // the latest frame wins; the UI thread is asked once
                Interlocked.Exchange(ref _pendingFrame, frame);

                if (Interlocked.Exchange(ref _frameScheduled, 1) == 0)
                    OnUi(ShowPendingFrame);

                break;

            case CatalogMessage catalog:
                OnUi(() => ShowCatalog(catalog));
                break;

            case PreviewErrorMessage error:
                OnUi(() => ShowError(error.Message, error.Details));
                break;

            case HelloMessage hello when hello.ProtocolVersion != DesignerProtocol.Version:
                OnUi(() => SetStatus(
                    $"The previewer speaks protocol {hello.ProtocolVersion}, the extension {DesignerProtocol.Version}: reinstall the extension."));
                break;
        }
    }

    private void OnUi(Action action)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            action();
        }).FileAndForget("zeppelinforms/preview/ui");
    }

    // ===== the list =====

    private void ShowCatalog(CatalogMessage catalog)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _previews = catalog.Previews;

        _updatingList = true;

        try
        {
            _list.Items.Clear();

            foreach (PreviewInfo preview in _previews)
                _list.Items.Add(new ComboBoxItem
                {
                    Content = $"{preview.Group} › {preview.Name}",
                    Tag = preview.Id,
                    ToolTip = preview.MemberName is null ? preview.TypeName : $"{preview.TypeName}.{preview.MemberName}",
                });
        }
        finally
        {
            _updatingList = false;
        }

        if (catalog.Error is { } error)
        {
            ShowError(error, string.Empty);
            SetStatus("The project could not be loaded.");
            return;
        }

        SetStatus(catalog.Warning ?? $"{_previews.Count} previews in {Path.GetFileName(_designer?.AssemblyPath ?? string.Empty)}.");

        if (_previews.Count == 0)
        {
            ShowError("No previews in this project.",
                "Mark a static method returning a UIElement with [Preview], or add a form with a constructor without parameters.");
            return;
        }

        // what was shown before the rebuild, or what the active document declares
        string? id = _previews.Any(p => p.Id == _selectedId) ? _selectedId : null;

        if (id is null && _designer?.ActiveDocumentPath is { } document)
            id = PreviewsOf(document).FirstOrDefault()?.Id;

        Select(id ?? _previews[0].Id);
    }

    private void Select(string id)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (ComboBoxItem item in _list.Items.OfType<ComboBoxItem>())
        {
            if (!Equals(item.Tag, id)) continue;

            if (ReferenceEquals(_list.SelectedItem, item)) OnPreviewChosen();
            else _list.SelectedItem = item;

            return;
        }
    }

    private void OnPreviewChosen()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_updatingList || _list.SelectedItem is not ComboBoxItem { Tag: string id }) return;

        _selectedId = id;
        _error.Visibility = Visibility.Collapsed;

        _designer?.Send(new OpenMessage(id, CurrentSettings()));
    }

    /// <summary>A document of the project became active: if it declares previews and
    /// the one shown is not among them, show its first.</summary>
    private void OnDocumentActivated(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        List<PreviewInfo> own = PreviewsOf(path);

        if (own.Count > 0 && !own.Any(p => p.Id == _selectedId))
            Select(own[0].Id);
    }

    /// <summary>The previews whose types the source file declares, by name.</summary>
    private List<PreviewInfo> PreviewsOf(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return [];

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return [];
        }

        var declared = new HashSet<string>(
            Regex.Matches(text, @"\b(?:class|record|struct)\s+([A-Za-z_]\w*)").Cast<Match>().Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        return [.. _previews.Where(p => declared.Contains(ShortName(p.TypeName)))];
    }

    private static string ShortName(string typeName)
    {
        int cut = Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('+'));
        return cut < 0 ? typeName : typeName.Substring(cut + 1);
    }

    // ===== settings =====

    private bool IsFitMode => _size.SelectedIndex == 0;

    private PreviewSettings CurrentSettings()
    {
        var settings = new PreviewSettings
        {
            Scale = (float)VisualTreeHelper.GetDpi(this).DpiScaleX,
            Theme = _theme.SelectedIndex > 0 ? Themes[_theme.SelectedIndex] : null,
            RightToLeft = _rtl.IsChecked == true,
            TextScale = _textScale.SelectedIndex > 0 ? TextScales[_textScale.SelectedIndex] : 0f,
        };

        string culture = _culture.Text?.Trim() ?? string.Empty;
        if (culture.Length > 0 && culture != "Culture") settings.Culture = culture;

        (string _, double width, double height) = Sizes[Math.Max(0, _size.SelectedIndex)];

        if (width < 0)
        {
            // the visible area, less the margin around the picture
            settings.Width = (float)Math.Max(1, _scroll.ActualWidth - 24);
            settings.Height = (float)Math.Max(1, _scroll.ActualHeight - 24);
        }
        else
        {
            settings.Width = (float)width;
            settings.Height = (float)height;
        }

        return settings;
    }

    private void SendSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_selectedId is null || _designer is null) return;

        _designer.Send(new SettingsMessage(CurrentSettings()));
    }

    // ===== the picture =====

    private void ShowPendingFrame()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Interlocked.Exchange(ref _frameScheduled, 0);

        if (Interlocked.Exchange(ref _pendingFrame, null) is not { } frame) return;

        double dpi = 96.0 * frame.Scale;

        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height ||
            Math.Abs(_bitmap.DpiX - dpi) > 0.01)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, dpi, dpi, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;
        }

        // the host sends RGBA, WPF wants BGRA: swap red and blue in place
        byte[] pixels = frame.Pixels;

        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);

        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), pixels, frame.Width * 4, 0);

        _error.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message, string details)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _error.Text = details.Length == 0 ? message : message + Environment.NewLine + Environment.NewLine + details;
        _error.Visibility = Visibility.Visible;
    }

    private void SetStatus(string text) => _status.Text = text;

    // ===== input =====

    private void HookInput()
    {
        _image.MouseMove += (_, e) => Pointer(PointerAction.Move, e);
        _image.MouseLeave += (_, e) => Pointer(PointerAction.Leave, e);

        _image.MouseDown += (_, e) =>
        {
            _surface.Focus();
            Keyboard.Focus(_surface);
            _image.CaptureMouse();

            Pointer(PointerAction.Down, e, e.ChangedButton);
            e.Handled = true;
        };

        _image.MouseUp += (_, e) =>
        {
            Pointer(PointerAction.Up, e, e.ChangedButton);
            _image.ReleaseMouseCapture();
            e.Handled = true;
        };

        _image.MouseWheel += (_, e) =>
        {
            Point at = e.GetPosition(_image);
            _designer?.Send(new PointerMessage(PointerAction.Wheel, (float)at.X, (float)at.Y)
            {
                WheelDelta = e.Delta,
                Modifiers = Modifiers(),
            });

            e.Handled = true;
        };

        _surface.PreviewKeyDown += (_, e) => KeyChanged(e, isDown: true);
        _surface.PreviewKeyUp += (_, e) => KeyChanged(e, isDown: false);

        _surface.PreviewTextInput += (_, e) =>
        {
            if (e.Text.Length > 0)
                _designer?.Send(new TextMessage(e.Text));

            e.Handled = true;
        };
    }

    private void Pointer(PointerAction action, MouseEventArgs e, MouseButton button = MouseButton.Left)
    {
        if (_designer is null || _selectedId is null) return;

        Point at = e.GetPosition(_image);

        _designer.Send(new PointerMessage(action, (float)at.X, (float)at.Y)
        {
            Button = button switch
            {
                MouseButton.Right => DesignerMouseButton.Right,
                MouseButton.Middle => DesignerMouseButton.Middle,
                _ => DesignerMouseButton.Left,
            },
            Modifiers = Modifiers(),
        });
    }

    private void KeyChanged(KeyEventArgs e, bool isDown)
    {
        if (_designer is null || _selectedId is null) return;

        // Alt combinations come as Key.System with the real key aside
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // the virtual-key codes are ZeppelinForms' own Key values
        int code = KeyInterop.VirtualKeyFromKey(key);

        if (code == 0) return;

        _designer.Send(new KeyMessage(isDown, key.ToString())
        {
            Code = code,
            Modifiers = Modifiers(),
            IsRepeat = e.IsRepeat,
        });

        e.Handled = true;
    }

    private static DesignerModifiers Modifiers()
    {
        DesignerModifiers modifiers = DesignerModifiers.None;
        ModifierKeys keys = Keyboard.Modifiers;

        if ((keys & ModifierKeys.Shift) != 0) modifiers |= DesignerModifiers.Shift;
        if ((keys & ModifierKeys.Control) != 0) modifiers |= DesignerModifiers.Control;
        if ((keys & ModifierKeys.Alt) != 0) modifiers |= DesignerModifiers.Alt;

        return modifiers;
    }
}