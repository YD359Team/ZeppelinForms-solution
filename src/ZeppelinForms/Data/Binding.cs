using System.ComponentModel;
using System.Reflection;
using ZeppelinForms.Diagnostics;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms.Data;

public enum BindingMode
{
    /// <summary>Источник → элемент.</summary>
    OneWay,

    /// <summary>В обе стороны.</summary>
    TwoWay,
}

/// <summary>
/// Связь стилизуемого свойства элемента со свойством источника.
/// </summary>
/// <remarks>
/// Цель хранится слабой ссылкой: подписка на PropertyChanged — это
/// ссылка от источника к обработчику, и сильная ссылка на элемент
/// означала бы, что живая модель держит всё закрытое окно.
/// Обнаружив мёртвую цель, биндинг отписывается сам.
/// </remarks>
public sealed class Binding
{
    private readonly WeakReference<UIElement> _target;
    private readonly StyledProperty _targetProperty;
    private readonly PropertyInfo _sourceProperty;
    private readonly string _sourcePropertyName;

    /// <summary>Источник. Ссылка сильная: пока элемент жив, модель ему нужна.</summary>
    public object Source { get; }

    public BindingMode Mode { get; }

    /// <summary>Идёт запись из биндинга — чтобы отличить её от присваивания
    /// пользователя и не уйти в петлю в TwoWay.</summary>
    private bool _updating;

    private bool _detached;

    internal Binding(
        UIElement target,
        StyledProperty targetProperty,
        object source,
        PropertyInfo sourceProperty,
        BindingMode mode)
    {
        _target = new WeakReference<UIElement>(target);
        _targetProperty = targetProperty;
        Source = source;
        _sourceProperty = sourceProperty;
        _sourcePropertyName = sourceProperty.Name;
        Mode = mode;

        if (source is INotifyPropertyChanged notify)
            notify.PropertyChanged += OnSourceChanged;
    }

    /// <summary>Забрать значение из источника и записать в элемент.</summary>
    internal void PushToTarget()
    {
        if (_detached) return;

        if (!_target.TryGetTarget(out UIElement? element))
        {
            Detach();
            return;
        }

        object? raw = _sourceProperty.GetValue(Source);

        object? value;

        try
        {
            value = Convert(raw, _targetProperty.PropertyType);
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            // тип источника не лезет в тип свойства — записывать нечего,
            // но и падать посреди отрисовки неправильно
            ZfContract.Fail(
                $"Значение '{raw}' из '{_sourcePropertyName}' не приводится " +
                $"к {_targetProperty.PropertyType.Name}.");

            return;
        }

        _updating = true;

        try
        {
            element.SetBoundValue(_targetProperty, value);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Записать значение элемента в источник. Только TwoWay.</summary>
    internal void PushToSource(object? value)
    {
        if (_detached || Mode != BindingMode.TwoWay || _updating) return;

        if (!_sourceProperty.CanWrite) return;

        _updating = true;

        try
        {
            _sourceProperty.SetValue(Source, Convert(value, _sourceProperty.PropertyType));
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            // Значение не лезет в тип источника — в TwoWay это обычное дело:
            // пользователь печатает в поле, и промежуточные состояния
            // ("-", "1,") числом не являются. Роняем запись, не приложение.
            ZfContract.Fail(
                $"Значение '{value}' не приводится к {_sourceProperty.PropertyType.Name} " +
                $"для '{_sourcePropertyName}'.");
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        // пустое имя по соглашению означает «поменялось всё»
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != _sourcePropertyName)
            return;

        if (_updating) return;

        PushToTarget();
    }

    /// <summary>Разорвать связь и отписаться от источника.</summary>
    internal void Detach()
    {
        if (_detached) return;
        _detached = true;

        if (Source is INotifyPropertyChanged notify)
            notify.PropertyChanged -= OnSourceChanged;
    }

    private static object? Convert(object? value, Type targetType)
    {
        if (value is null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return underlying.IsEnum
            ? Enum.Parse(underlying, value.ToString()!)
            : System.Convert.ChangeType(value, underlying);
    }
}