namespace ZeppelinForms.Core;

/// <summary>The platform or a native library refused: no X server to connect to,
/// no OpenGL context, no surface, an OLE call that failed.</summary>
public class ZfPlatformException : ZfException
{
    public ZfPlatformException()
    {
    }

    public ZfPlatformException(string? message) : base(message)
    {
    }

    public ZfPlatformException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <param name="nativeErrorCode">What the native call returned: an HRESULT,
    /// a Win32 error, errno.</param>
    public ZfPlatformException(string? message, int nativeErrorCode) : base(message)
    {
        NativeErrorCode = nativeErrorCode;
    }

    /// <summary>The native call's own error code, for a crash report. 0 — the call
    /// reported none, or failed without one.</summary>
    public int NativeErrorCode { get; }
}