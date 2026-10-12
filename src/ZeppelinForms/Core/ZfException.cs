namespace ZeppelinForms.Core;

/// <summary>The base of the framework's own exceptions: the ones an application
/// can't avoid by fixing its call, and may want to tell apart in a crash handler.
/// Wrong arguments and wrong state throw the standard ArgumentException and
/// InvalidOperationException, as everywhere in .NET.</summary>
public class ZfException : Exception
{
    public ZfException()
    {
    }

    public ZfException(string? message) : base(message)
    {
    }

    public ZfException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}