namespace ZeppelinForms.Animation;

public interface IAnimation
{
    object Target { get; }
    string Key { get; }

    /// <summary>Продвинуть на прошедшее время. false — анимация закончилась.</summary>
    bool Advance(TimeSpan elapsed);
}
