namespace ZeppelinForms.Animation;

public interface IAnimation
{
    object Target { get; }
    string Key { get; }

    /// <summary>Продвинуть на прошедшее время. false — анимация закончилась.</summary>
    bool Advance(TimeSpan elapsed);

    /// <summary>Анимацию сняли до срока: вытеснила новая с тем же ключом
    /// либо убрали цель из дерева. Реализация обязана привести состояние
    /// к завершённому — тот, кто её запускал, о снятии не узнает.</summary>
    /// <param name="applyFinalValue">Довести значение до конечного.
    /// false — оставить как есть: цель уже уходит и дорисовывать нечего.</param>
    void Cancel(bool applyFinalValue);
}
