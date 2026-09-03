namespace ZeppelinForms.Core;

public class ZfException : Exception
{
    public ZfException()
    {
    }

    public ZfException(string? message) : base(message)
    {
    }
}
