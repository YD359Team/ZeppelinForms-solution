namespace ZeppelinForms.Input.Gestures;

public enum GestureState
{
    /// <summary>Ещё не решил: смотрит на контакт и ждёт.</summary>
    Possible,

    /// <summary>Выиграл контакт. Остальные участники арены выбыли,
    /// совместимые события мыши по этому контакту отменены.</summary>
    Accepted,

    /// <summary>Выбыл — сам отказался или проиграл другому.</summary>
    Rejected,
}