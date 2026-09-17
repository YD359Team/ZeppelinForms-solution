using ZeppelinForms.Animation;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms;

/// <summary>Кадры, анимации и отложенные дела формы. Всё это живёт
/// в FrameClock — здесь только вход для остального кода.</summary>
public partial class Form
{
    private FrameClock? _clock;

    /// <summary>Часы формы. Создаются по первому обращению: форме без
    /// анимаций и отложенных дел они не нужны.</summary>
    internal FrameClock Clock => _clock ??= new FrameClock(this);

    public int FrameIntervalMs { get; set; } = 16;   // ~60 кадров в секунду

    internal void AddAnimation(IAnimation animation) => Clock.Add(animation);

    internal void RemoveAnimation(object target, string key) => Clock.Remove(target, key);

    /// <summary>Поддерево уходит из формы — его анимации сняты.</summary>
    internal void CancelAnimationsIn(UIElement root) => Clock.CancelIn(root);

    /// <summary>Кадр от платформы: таймер окна, requestAnimationFrame,
    /// Choreographer.</summary>
    internal void Tick()
    {
        // анимации читают геометрию: PageControl сдвигает страницы
        // от их слота, волна темы — от размера клиентской области
        EnsureLayout();

        Clock.Tick();
    }

    /// <summary>Пересмотреть, нужны ли кадры. Видимость — свойство
    /// раскладки, поэтому достаточно позвать это из Invalidate: анимация
    /// на показавшейся заново странице сама вернёт себе кадры.</summary>
    internal void ReviewFrames() => _clock?.Review();

    /// <summary>Приложение ушло в фон.</summary>
    internal void SuspendFrames() => _clock?.Suspend();

    /// <summary>Приложение вернулось из фона.</summary>
    internal void ResumeFrames() => _clock?.Resume();

    /// <summary>Выполнить действие через задержку в потоке UI.</summary>
    /// <returns>Отмена: освободите результат, чтобы вызова не было.</returns>
    internal IDisposable Schedule(int delayMs, Action action) =>
        Clock.Schedule(TimeSpan.FromMilliseconds(delayMs), action);
}