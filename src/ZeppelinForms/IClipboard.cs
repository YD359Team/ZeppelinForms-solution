using System;
using System.Collections.Generic;
using System.Text;

namespace ZeppelinForms;

public interface IClipboard
{
    string? GetText();
    void SetText(string text);
}

public static class Clipboard
{
    public static IClipboard Current { get; set; } = new NotRegisteredClipboard();

    private sealed class NotRegisteredClipboard : IClipboard
    {
        public string? GetText() => null;
        public void SetText(string text) { }
    }
}
