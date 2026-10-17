namespace ZeppelinForms.Core;

/// <summary>The application isn't set up for what it asked: a backend wasn't
/// registered, the thread is of the wrong kind. The message says what to call
/// or change.</summary>
public class ZfSetupException : ZfException
{
    public ZfSetupException()
    {
    }

    public ZfSetupException(string? message) : base(message)
    {
    }

    public ZfSetupException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}