namespace ZeppelinForms;

/// <summary>Вложенный цикл для синхронной модальности. Есть только там,
/// где платформа владеет циклом; в браузере невозможен в принципе.</summary>
public interface INestedLoopSupport
{
    void RunModal(IPlatformWindow dialog, IPlatformWindow? owner);
}