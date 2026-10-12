using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
/// <para>
/// Size and zoom are two things. The size is what the preview is laid out at — its
/// own, a device's, or the window's. The zoom only changes how big the picture is: the
/// host draws the same layout with more or fewer pixels (the settings' scale is the
/// window's DPI times the zoom), so a smaller picture stays sharp and is never
/// stretched. "Fit" picks the zoom that shows the whole preview, never above 100%.
/// It used to be the other way round: the preview was laid out at the window's size,
/// and a form of 1024×768 squeezed into a pane 350 pixels wide looked nothing like
/// the form in the application.
/// </para>
/// </remarks>
internal sealed class PreviewControl : UserControl
{
    private static readonly string[] Themes =
        ["", "Light", "Dark", "FluentLight", "FluentDark", "HighContrastBlack", "HighContrastWhite"];

    private static readonly string[] Cultures = ["", "en-US", "ru-RU", "de-DE", "ja-JP", "ar-SA", "he-IL"];

    /// <summary>Width −1: the window's size; 0: the preview's own.</summary>
    private static readonly (string Name, double Width, double Height)[] Sizes =
    [
        ("The preview's own size", 0, 0),
        ("Fill the window", -1, -1),
        ("Phone 360 × 640", 360, 640),
        ("Tablet 768 × 1024", 768, 1024),
        ("Desktop 1280 × 800", 1280, 800),
    ];

    /// <summary>0 — fit the window.</summary>
    private static readonly double[] Zooms = [0, 0.25, 0.5, 0.75, 1, 1.5, 2];

    private static readonly float[] TextScales = [1f, 1.25f, 1.5f, 2f];

    /// <summary>The empty space around the picture, room for its shadow.</summary>
    private const double SurfaceMargin = 24;

    private readonly ComboBox _list = new() { MinWidth = 220, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _theme = new() { MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _size = new() { MinWidth = 150, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _zoom = new() { MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
    private readonly ComboBox _culture = new() { MinWidth = 80, Margin = new Thickness(0, 0, 6, 0), IsEditable = true };
    private readonly ComboBox _textScale = new() { MinWidth = 64, Margin = new Thickness(0, 0, 6, 0) };
    private readonly CheckBox _rtl = new() { Content = "RTL", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Button _reload = new() { Content = "Reload", Padding = new Thickness(8, 1, 8, 1) };

    /// <summary>The zoom actually shown, next to the list. Not in the list's own text:
    /// a combo box doesn't redraw its selection box when the selected item's content
    /// changes, and "Fit (64%)" would have stayed "Fit" until another item was chosen.</summary>
    private readonly TextBlock _zoomInfo = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 36 };

    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Focusable = false,
    };

    /// <summary>The picture and its frame, centered in the window.</summary>
    private readonly Grid _stage = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(SurfaceMargin),
    };

    /// <summary>A shadow under the picture, so that the preview's own background
    /// doesn't melt into the window's. A sibling rather than an effect on the picture:
    /// an effect renders its element again through a bitmap.</summary>
    private readonly System.Windows.Shapes.Rectangle _shadow = new()
    {
        Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.35 },
        Visibility = Visibility.Collapsed,
    };

    private readonly Border _surface = new()
    {
        Focusable = true,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
        FocusVisualStyle = null,
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

    /// <summary>The zoom asked of the host with the last settings.</summary>
    private double _requestedZoom = 1;

    /// <summary>The zoom of the picture on screen: pointer positions are divided by it.</summary>
    private double _shownZoom = 1;

    /// <summary>The size the preview is laid out at, in its own pixels, from the last
    /// frame; null — no frame of this preview yet.</summary>
    private Size? _logicalSize;

    /// <summary>The size the last fitting zoom was computed for: a frame of nearly the
    /// same size doesn't ask again. A frame's size is rounded up to whole device pixels,
    /// so the size read back from it differs a little with every zoom, and two zooms
    /// on either side of a step could ask for each other forever.</summary>
    private Size? _zoomBasis;

    public PreviewControl()
    {
        foreach (string theme in Themes) _theme.Items.Add(theme.Length == 0 ? "Preview's theme" : theme);
        foreach (string culture in Cultures) _culture.Items.Add(culture.Length == 0 ? "Culture" : culture);
        foreach (var size in Sizes) _size.Items.Add(size.Name);
        foreach (float scale in TextScales) _textScale.Items.Add($"{scale:P0}");

        _zoom.Items.Add("Fit");
        foreach (double zoom in Zooms.Skip(1)) _zoom.Items.Add($"{zoom:P0}");

        _theme.SelectedIndex = 0;
        _culture.SelectedIndex = 0;
        _size.SelectedIndex = 0;
        _zoom.SelectedIndex = 0;
        _textScale.SelectedIndex = 0;

        _theme.ToolTip = "Theme";
        _size.ToolTip = "The size the preview is laid out at";
        _zoom.ToolTip = "Zoom: the picture only, the layout stays";
        _culture.ToolTip = "Culture";
        _textScale.ToolTip = "Text scale";
        _rtl.ToolTip = "Right-to-left layout";

        var toolbar = new WrapPanel { Margin = new Thickness(6) };

        foreach (UIElement item in new UIElement[] { _list, _size, _zoom, _zoomInfo, _theme, _culture, _textScale, _rtl, _reload })
            toolbar.Children.Add(item);

        _surface.Child = _image;
        _stage.Children.Add(_shadow);
        _stage.Children.Add(_surface);
        _scroll.Content = _stage;

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

        // whole pixels: a picture centered on half a pixel is resampled, and blurred
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);

        // the colors of the Visual Studio theme, light or dark
        SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _zoomInfo.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        _error.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        _error.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        // under a preview with a translucent background the window shows, not black
        _shadow.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

        // and its controls: WPF's own looked like a dialog of another program,
        // light on the dark theme
        foreach (ComboBox box in new[] { _list, _theme, _size, _zoom, _culture, _textScale })
            box.SetResourceReference(StyleProperty, VsResourceKeys.ComboBoxStyleKey);

        _rtl.SetResourceReference(StyleProperty, VsResourceKeys.CheckBoxStyleKey);
        _reload.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);

        _list.SelectionChanged += (_, _) => OnPreviewChosen();
        _theme.SelectionChanged += (_, _) => SendSettings();
        _size.SelectionChanged += (_, _) => OnSizeChosen();
        _zoom.SelectionChanged += (_, _) => SendSettings();
        _textScale.SelectionChanged += (_, _) => SendSettings();
        _culture.SelectionChanged += (_, _) => SendSettings();
        _culture.LostKeyboardFocus += (_, _) => SendSettings();
        _rtl.Click += (_, _) => SendSettings();
        _reload.Click += (_, _) => _designer?.Refresh(force: true);

        _scroll.SizeChanged += (_, _) =>
        {
            // the window decides the layout when it is filled, the zoom when it is fitted
            if (IsFillMode || IsFitZoom)
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

        SetStatus(catalog.Warning is { } warning
            ? FirstLine(warning)
            : $"{_previews.Count} previews in {Path.GetFileName(_designer?.AssemblyPath ?? string.Empty)}.");

        if (_previews.Count == 0)
        {
            // the warning says why when it knows: types that could not be loaded
            ShowError("No previews in this project.",
                catalog.Warning ??
                "Mark a static method returning a UIElement with [Preview], or add a form with a constructor without parameters.");
            return;
        }

        // what was shown before the rebuild, or what the active document declares
        string? id = _previews.Any(p => p.Id == _selectedId) ? _selectedId : null;

        if (id is null && _designer?.ActiveDocumentPath is { } document)
            id = PreviewsOf(document).FirstOrDefault()?.Id;

        Select(id ?? _previews[0].Id);
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text.Substring(0, end);
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

        // another preview, another size: the zoom that fitted the last one is a guess
        // until the first frame says the new size
        if (id != _selectedId) _logicalSize = null;

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

    private bool IsFillMode => Sizes[Math.Max(0, _size.SelectedIndex)].Width < 0;

    private bool IsFitZoom => !IsFillMode && _zoom.SelectedIndex <= 0;

    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>The room the picture has, in the window's pixels.</summary>
    private Size Room => new(
        Math.Max(1, _scroll.ActualWidth - 2 * SurfaceMargin),
        Math.Max(1, _scroll.ActualHeight - 2 * SurfaceMargin));

    private void OnSizeChosen()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // a filled window is shown as it is: zooming it would only leave empty room
        _zoom.IsEnabled = !IsFillMode;

        // the fitting zoom of the previous size says nothing about this one
        _logicalSize = null;

        SendSettings();
    }

    private PreviewSettings CurrentSettings()
    {
        var settings = new PreviewSettings
        {
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
            Size room = Room;
            settings.Width = (float)room.Width;
            settings.Height = (float)room.Height;
        }
        else
        {
            // 0 — the preview's own: the host keeps the size the form or the
            // attribute asks for
            settings.Width = (float)width;
            settings.Height = (float)height;

            // a device's size is known before the first frame; the preview's own
            // only from a frame of it
            if (width > 0) _logicalSize = new Size(width, height);
        }

        _zoomBasis = _logicalSize;
        _requestedZoom = ZoomFor(_logicalSize);

        // device pixels per pixel of the preview: the host draws the zoom, not WPF
        settings.Scale = (float)(Dpi * _requestedZoom);

        UpdateZoomInfo();

        return settings;
    }

    /// <summary>The zoom for a preview of this size: the chosen one, or the one that
    /// shows it whole — never larger than life. Unknown size: the last zoom stays.</summary>
    private double ZoomFor(Size? logical)
    {
        if (IsFillMode) return 1;

        if (!IsFitZoom) return Zooms[_zoom.SelectedIndex];

        if (logical is not { Width: > 0, Height: > 0 } size) return _requestedZoom;

        // a window not laid out yet has no room to speak of: fitting into it would
        // pick the smallest zoom, and the first picture would be a stamp
        if (_scroll.ActualWidth <= 2 * SurfaceMargin || _scroll.ActualHeight <= 2 * SurfaceMargin)
            return _requestedZoom;

        Size room = Room;
        double fit = Math.Min(1, Math.Min(room.Width / size.Width, room.Height / size.Height));

        // an odd zoom like 0.6373 draws fractional pixels at every edge; a step of
        // 1/64 is invisible and keeps the picture's size stable while resizing
        return Math.Max(0.1, Math.Floor(fit * 64) / 64);
    }

    /// <summary>Only a fitted zoom is worth spelling out: a chosen one is in the list.</summary>
    private void UpdateZoomInfo() =>
        _zoomInfo.Text = IsFitZoom ? $"{_requestedZoom:P0}" : string.Empty;

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

        double dpi = Dpi;
        double zoom = frame.Scale / dpi;

        // the preview's own size, whatever zoom it came with
        _logicalSize = new Size(frame.Width / frame.Scale, frame.Height / frame.Scale);

        // the first frame of a preview tells its size: if the fitting zoom is another,
        // this frame is not shown — the next one, of the right size, follows at once
        if (IsFitZoom && !NearlySame(_zoomBasis, _logicalSize) && Math.Abs(ZoomFor(_logicalSize) - _requestedZoom) > 0.001)
        {
            SendSettings();
            return;
        }

        // the bitmap is in the window's DPI, not the frame's: the zoom is in its pixels
        double bitmapDpi = 96.0 * dpi;

        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height ||
            Math.Abs(_bitmap.DpiX - bitmapDpi) > 0.01)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, bitmapDpi, bitmapDpi, PixelFormats.Pbgra32, null);
            _image.Source = _bitmap;
        }

        // the host sends RGBA, WPF wants BGRA: swap red and blue in place
        byte[] pixels = frame.Pixels;

        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);

        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), pixels, frame.Width * 4, 0);

        _shownZoom = zoom;
        _shadow.Visibility = Visibility.Visible;
        _error.Visibility = Visibility.Collapsed;
    }

    private static bool NearlySame(Size? a, Size? b) =>
        a is { } x && b is { } y && Math.Abs(x.Width - y.Width) < 2 && Math.Abs(x.Height - y.Height) < 2;

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
            Point at = InPreview(e);
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

    /// <summary>The mouse in the preview's own pixels: the picture on screen is zoomed.</summary>
    private Point InPreview(MouseEventArgs e)
    {
        Point at = e.GetPosition(_image);
        double zoom = _shownZoom > 0 ? _shownZoom : 1;

        return new Point(at.X / zoom, at.Y / zoom);
    }

    private void Pointer(PointerAction action, MouseEventArgs e, MouseButton button = MouseButton.Left)
    {
        if (_designer is null || _selectedId is null) return;

        Point at = InPreview(e);

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