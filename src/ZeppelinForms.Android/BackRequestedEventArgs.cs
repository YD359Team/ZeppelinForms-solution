namespace ZeppelinForms.Android;

public sealed class BackRequestedEventArgs : EventArgs
{
    /// <summary>Приложение обработало возврат само — закрывать активность
    /// не нужно.</summary>
    public bool Handled { get; set; }
}