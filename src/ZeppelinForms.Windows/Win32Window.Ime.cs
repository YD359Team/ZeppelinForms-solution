using System.Text;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Windows;

/// <summary>
/// Input methods on Windows (IMM32): the text being composed is drawn by the field
/// itself, underlined at its caret, and the candidate list opens under the caret.
/// </summary>
/// <remarks>
/// <para>
/// Before 0.15 the IME messages went to DefWindowProc: the system drew the composition
/// in a window of its own, somewhere near the top-left corner — <c>SetImePosition</c>
/// existed but nothing called it — and the result arrived as WM_CHAR.
/// </para>
/// <para>
/// Now WM_IME_COMPOSITION is read here: the composition string goes to the form as an
/// update, the result string as a commit, and the message is not passed on — its default
/// handling would send the result a second time as WM_IME_CHAR and WM_CHAR. The
/// system's composition window is turned off in WM_IME_SETCONTEXT; the candidate list
/// stays the IME's own.
/// </para>
/// </remarks>
internal sealed partial class Win32Window : ITextInputMethodWindow
{
    /// <summary>The system's composition window off; everything else of the IME's UI on.</summary>
    private static nint OnImeSetContext(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (wParam != 0)
            lParam = (nint)(lParam.ToInt64() & ~NativeConstants.ISC_SHOWUICOMPOSITIONWINDOW);

        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private nint OnImeComposition(nint hWnd, nint lParam)
    {
        long flags = lParam.ToInt64();
        nint context = NativeMethods.ImmGetContext(hWnd);

        if (context == 0) return 0;

        try
        {
            // a Korean IME commits a syllable and starts the next one in one message:
            // the result first, then the new composition
            if ((flags & NativeConstants.GCS_RESULTSTR) != 0)
                _form.OnCompositionCommit(ReadComposition(context, NativeConstants.GCS_RESULTSTR));

            if ((flags & NativeConstants.GCS_COMPSTR) != 0)
            {
                string text = ReadComposition(context, NativeConstants.GCS_COMPSTR);

                int cursor = (flags & NativeConstants.GCS_CURSORPOS) != 0
                    ? NativeMethods.ImmGetCompositionStringW(context, NativeConstants.GCS_CURSORPOS, null, 0) & 0xFFFF
                    : text.Length;

                _form.OnCompositionUpdate(text, cursor);
            }
        }
        finally
        {
            NativeMethods.ImmReleaseContext(hWnd, context);
        }

        return 0;
    }

    /// <summary>A string of the composition, UTF-16; its length comes in bytes.</summary>
    private static string ReadComposition(nint context, uint index)
    {
        int bytes = NativeMethods.ImmGetCompositionStringW(context, index, null, 0);

        if (bytes <= 0) return string.Empty;

        var buffer = new byte[bytes];
        NativeMethods.ImmGetCompositionStringW(context, index, buffer, (uint)bytes);

        return Encoding.Unicode.GetString(buffer);
    }

    /// <summary>The focused field's caret: the candidate list opens under it and keeps
    /// off the caret's line, and an IME that draws its own composition window anyway
    /// puts it there.</summary>
    public void SetTextInputArea(Rectangle? caret)
    {
        if (caret is not { } area || _handle == 0) return;

        nint context = NativeMethods.ImmGetContext(_handle);
        if (context == 0) return;

        try
        {
            int left = (int)(area.X * _scale);
            int top = (int)(area.Y * _scale);
            int right = (int)Math.Ceiling((area.X + area.Width) * _scale);
            int bottom = (int)Math.Ceiling((area.Y + area.Height) * _scale);

            var composition = new NativeMethods.COMPOSITIONFORM
            {
                dwStyle = NativeConstants.CFS_POINT,
                ptCurrentPos = new NativeMethods.POINT { X = left, Y = top },
            };

            NativeMethods.ImmSetCompositionWindow(context, ref composition);

            var candidates = new NativeMethods.CANDIDATEFORM
            {
                dwIndex = 0,
                dwStyle = NativeConstants.CFS_EXCLUDE,
                ptCurrentPos = new NativeMethods.POINT { X = left, Y = bottom },
                rcArea = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom },
            };

            NativeMethods.ImmSetCandidateWindow(context, ref candidates);
        }
        finally
        {
            NativeMethods.ImmReleaseContext(_handle, context);
        }
    }
}
