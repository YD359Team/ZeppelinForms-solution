namespace ZeppelinForms;

public interface IAppLifecycle
{
    /// <summary>Приложение уходит в фон. Здесь останавливают анимации
    /// и освобождают то, что дорого держать.</summary>
    event EventHandler? Paused;

    event EventHandler? Resumed;

    /// <summary>Последняя возможность сохранить состояние.
    /// На Android приложение после этого может быть убито без предупреждения.</summary>
    event EventHandler? Saving;
}