using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Interfaces;
using ZeppelinForms.Forms.Styling;
using ZeppelinForms.Input.Mouse;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Что и куда перенесли.</summary>
public sealed record class DragListDropEventArgs(
    object Item,
    DragList Source,
    int SourceIndex,
    DragList Target,
    int TargetIndex);

/// <summary>
/// Список с перестановкой строк мышью. Списки с одинаковым
/// <see cref="Group"/> обмениваются строками между собой.
/// </summary>
public partial class DragList : ItemsControl
{
    // перетаскивание одно на всё приложение: тянуть можно только одну строку,
    // а список-приёмник должен знать, что к нему летит
    private static DragList? _source;
    private static DragList? _target;
    private static object? _item;
    private static int _sourceIndex = -1;
    private static int _targetIndex = -1;
    private static UIElement? _preview;

    private static readonly Dictionary<string, List<DragList>> Groups = new(StringComparer.Ordinal);

    private string? _group;
    private bool _attached;
    private bool _dragging;
    private Point _pressOrigin;
    private int _pressIndex = -1;

    /// <summary>Имя группы. Списки одной группы могут передавать строки
    /// друг другу. Пусто — список замкнут на себя.</summary>
    public string? Group
    {
        get => _group;
        set
        {
            if (string.Equals(_group, value, StringComparison.Ordinal)) return;

            Unregister();
            _group = value;
            Register();
        }
    }

    /// <summary>Разрешено ли забирать строки отсюда.</summary>
    public bool CanSendItem { get; set; } = true;

    /// <summary>Разрешено ли класть строки сюда.</summary>
    public bool CanReceiveItem { get; set; } = true;

    /// <summary>Точечный фильтр поверх <see cref="CanReceiveItem"/>:
    /// решает по конкретной строке и списку-источнику.</summary>
    public Func<object, DragList, bool>? ReceivePredicate { get; set; }

    /// <summary>Сколько пикселей надо протащить, прежде чем это считается
    /// перетаскиванием, а не промахом при клике.</summary>
    public float DragThreshold { get; set; } = 4f;

    public float DropIndicatorHeight { get; set; } = 2f;

    [Styled(Category = "Drag")]
    public partial Color DropIndicatorColor { get; set; }
    private static Color DropIndicatorColorDefault => new(255, 0, 120, 215);

    [Styled(Category = "Drag")]
    public partial Color DragPreviewBackground { get; set; }
    private static Color DragPreviewBackgroundDefault => Colors.White;

    /// <summary>Строку унесли отсюда.</summary>
    public event EventHandler<DragListDropEventArgs>? ItemSent;

    /// <summary>Строку принесли сюда.</summary>
    public event EventHandler<DragListDropEventArgs>? ItemReceived;

    // ===== регистрация в группе =====

    protected override void OnAttached()
    {
        _attached = true;
        Register();
    }

    protected override void OnDetached()
    {
        Unregister();
        _attached = false;

        // список уносят из дерева прямо во время перетаскивания —
        // бросаем всё, иначе статика останется со ссылкой на мертвеца
        if (ReferenceEquals(_source, this) || ReferenceEquals(_target, this))
            CancelDrag();
    }

    private void Register()
    {
        if (!_attached || string.IsNullOrEmpty(_group)) return;

        if (!Groups.TryGetValue(_group, out List<DragList>? members))
            Groups[_group] = members = [];

        if (!members.Contains(this))
            members.Add(this);
    }

    private void Unregister()
    {
        if (string.IsNullOrEmpty(_group)) return;
        if (!Groups.TryGetValue(_group, out List<DragList>? members)) return;

        members.Remove(this);

        if (members.Count == 0)
            Groups.Remove(_group);
    }

    /// <summary>Куда вообще можно уронить: сам список плюс его группа.</summary>
    private IEnumerable<DragList> DropCandidates()
    {
        yield return this;

        if (string.IsNullOrEmpty(_group)) yield break;
        if (!Groups.TryGetValue(_group, out List<DragList>? members)) yield break;

        foreach (DragList list in members)
            if (!ReferenceEquals(list, this))
                yield return list;
    }

    // ===== мышь =====

    /// <summary>Нажатие ловим предпросмотром: попадание достаётся строке,
    /// а список должен узнать о нажатии раньше неё.</summary>
    protected override void OnPreviewMouseDown(Point location)
    {
        if (!CanSendItem || Children.Count == 0) return;

        int index = IndexAt(ToLocal(location));
        if (index < 0) return;

        _pressOrigin = location;
        _pressIndex = index;
        _dragging = false;

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseMoveEventArgs args)
    {
        if (_pressIndex < 0) return;

        if (!_dragging)
        {
            // порог: без него любой клик дрожащей рукой превращается в перенос
            if (Point.DistanceBetween(args.Location, _pressOrigin) < DragThreshold) return;

            BeginDrag();
        }

        UpdateDrag(args.Location);
    }

    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        if (args.Button != MouseButton.Left) return;

        if (_dragging) Drop();

        ReleaseMouseCapture();

        _pressIndex = -1;
        _dragging = false;
    }

    // ===== перетаскивание =====

    private void BeginDrag()
    {
        _dragging = true;
        _source = this;
        _sourceIndex = _pressIndex;
        _item = Items[_pressIndex];

        _preview = CreatePreview(Children[_pressIndex], _item);

        FindOwner()?.AddOverlay(_preview);
    }

    private void UpdateDrag(Point location)
    {
        if (_preview is not null)
            _preview.Position = new Point(
                location.X - _preview.DesiredSize.Width / 2f,
                location.Y - _preview.DesiredSize.Height / 2f);

        DragList? target = null;
        int index = -1;

        foreach (DragList list in DropCandidates())
        {
            if (!list.AcceptsDrop(_item!, this)) continue;

            var bounds = new Rectangle(list.GetAbsolutePosition(), list.ActualSize);
            if (!bounds.Contains(location)) continue;

            target = list;
            index = list.InsertionIndexAt(list.ToLocal(location));
            break;
        }

        if (!ReferenceEquals(target, _target))
        {
            // перерисовать надо оба: со старого убрать полоску, на новый добавить
            _target?.InvalidateVisual();
            target?.InvalidateVisual();
        }
        else if (index != _targetIndex)
        {
            target?.InvalidateVisual();
        }

        _target = target;
        _targetIndex = index;

        // именно Invalidate, а не InvalidateVisual: превью — оверлей,
        // его позицию подхватывает только проход раскладки
        FindOwner()?.Invalidate();
    }

    private bool AcceptsDrop(object item, DragList source)
    {
        if (!CanReceiveItem) return false;

        // в себя роняем всегда: это перестановка, а не передача между списками
        if (!ReferenceEquals(source, this) && !SameGroup(source)) return false;

        return ReceivePredicate?.Invoke(item, source) ?? true;
    }

    private bool SameGroup(DragList other) =>
        !string.IsNullOrEmpty(_group) &&
        string.Equals(_group, other._group, StringComparison.Ordinal);

    private void Drop()
    {
        DragList? target = _target;
        object? item = _item;
        int from = _sourceIndex;
        int to = _targetIndex;

        CancelDrag();

        if (target is null || item is null || to < 0) return;

        if (ReferenceEquals(target, this))
        {
            // строку сначала вынимают, поэтому при переносе вниз
            // все индексы после неё съезжают на один
            int corrected = to > from ? to - 1 : to;

            if (corrected == from) return;

            Items.Move(from, corrected);
        }
        else
        {
            Items.RemoveAt(from);
            target.Items.Insert(Math.Clamp(to, 0, target.Items.Count), item);
        }

        var args = new DragListDropEventArgs(item, this, from, target, to);

        ItemSent?.Invoke(this, args);

        if (!ReferenceEquals(target, this))
            target.ItemReceived?.Invoke(target, args);
    }

    private static void CancelDrag()
    {
        if (_preview is not null)
        {
            _source?.FindOwner()?.RemoveOverlay(_preview);
            _preview = null;
        }

        _target?.InvalidateVisual();

        if (_source is not null)
        {
            _source._dragging = false;
            _source._pressIndex = -1;
        }

        _source = null;
        _target = null;
        _item = null;
        _sourceIndex = -1;
        _targetIndex = -1;
    }

    // ===== геометрия =====

    /// <summary>Индекс строки под точкой, или -1.</summary>
    private int IndexAt(Point local)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];

            if (local.Y >= child.Position.Y &&
                local.Y < child.Position.Y + child.ActualSize.Height)
                return i;
        }

        return -1;
    }

    /// <summary>Куда вставить: по середине строки решаем, до неё или после.</summary>
    private int InsertionIndexAt(Point local)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            UIElement child = Children[i];

            if (local.Y < child.Position.Y + child.ActualSize.Height / 2f)
                return i;
        }

        return Children.Count;
    }

    private Point ToLocal(Point absolute)
    {
        Point origin = GetAbsolutePosition();

        return new Point(absolute.X - origin.X, absolute.Y - origin.Y);
    }

    // ===== отрисовка =====

    /// <summary>Полоска места вставки. DrawDecoration зовётся из DrawOverlay,
    /// то есть после потомков — линию не перекроют строки.</summary>
    protected override void DrawDecoration(Graphics g)
    {
        if (!ReferenceEquals(_target, this) || _targetIndex < 0) return;

        Rectangle content = ContentBounds;

        float y = _targetIndex < Children.Count
            ? Children[_targetIndex].Position.Y
            : content.Y + content.Height;

        y = Math.Clamp(y, content.Y, Math.Max(content.Y, content.Y + content.Height - DropIndicatorHeight));

        g.FillRectangle(
            new Rectangle(new Point(content.X, y), new Size(content.Width, DropIndicatorHeight)),
            DropIndicatorColor);
    }

    private UIElement CreatePreview(UIElement container, object item)
    {
        return new Border
        {
            Background = DragPreviewBackground,
            BorderColor = DropIndicatorColor,
            BorderWidth = 1f,
            CornerRadius = new CornerRadius(4f),
            Padding = new Thickness(2f),
            Opacity = 0.85f,

            // оверлеи хит-тестятся первыми: без этого превью
            // перехватит курсор у списков под ним
            IsHitTestVisible = false,

            Child = Snapshot(container) ?? Caption(item),
        };
    }

    /// <summary>Снимок строки. Если рендерер не зарегистрирован —
    /// обойдёмся подписью, ронять перетаскивание из-за превью незачем.</summary>
    private static UIElement? Snapshot(UIElement container)
    {
        try
        {
            Image image = container.RenderToImage();

            var picture = new PictureBox { Size = new Size(image.Width, image.Height) };
            picture.SetImage(image);

            return picture;
        }
        catch
        {
            return null;
        }
    }

    private static UIElement Caption(object item) =>
        new Label
        {
            Text = item.ToString() ?? string.Empty,
            Padding = new Thickness(8, 4),
        };
}