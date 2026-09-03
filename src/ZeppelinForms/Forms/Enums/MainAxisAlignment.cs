namespace ZeppelinForms.Forms.Enums;

/// <summary>Как распределить детей вдоль главной оси панели.</summary>
public enum MainAxisAlignment
{
    Start,
    Center,
    End,
    /// <summary>Промежутки равны, крайние прижаты к краям.</summary>
    SpaceBetween,
    /// <summary>Промежутки равны, у краёв — половинные.</summary>
    SpaceAround,
    /// <summary>Все промежутки, включая крайние, равны.</summary>
    SpaceEvenly,
}
