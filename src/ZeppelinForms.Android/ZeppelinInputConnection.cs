using Android.Views;
using Android.Views.InputMethods;
using Java.Lang;
using ZeppelinForms.Forms;
using ZeppelinForms.Input.Keyboard;

namespace ZeppelinForms.Android;

/// <summary>Мост между IME и формой.</summary>
/// <remarks>
/// fullEditor: false означает, что мы не держим редактируемый буфер
/// на стороне Android — текст живёт в TextDocument формы. Простые нажатия
/// IME тогда присылает как события клавиш, а подсказки, автозамену
/// и эмодзи — через CommitText.
/// </remarks>
internal sealed class ZeppelinInputConnection(View view, Form form)
    : BaseInputConnection(view, fullEditor: false)
{
    public override bool CommitText(ICharSequence? text, int newCursorPosition)
    {
        if (text is null) return true;

        string value = text.ToString();

        foreach (char c in value)
            form.OnTextInput(c);

        return true;
    }

    /// <summary>Автозамена и подсказки приходят как «сотри столько-то
    /// символов и вставь это»: стирание надо выполнить, иначе слово
    /// удвоится.</summary>
    public override bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        for (int i = 0; i < beforeLength; i++)
        {
            form.OnKeyDown(Key.Backspace, KeyModifiers.None, false);
            form.OnKeyUp(Key.Backspace, KeyModifiers.None);
        }

        for (int i = 0; i < afterLength; i++)
        {
            form.OnKeyDown(Key.Delete, KeyModifiers.None, false);
            form.OnKeyUp(Key.Delete, KeyModifiers.None);
        }

        return true;
    }

    /// <summary>Промежуточный текст набора показываем как обычный:
    /// подчёркнутого состояния «ещё набирается» у TextDocument нет,
    /// и подделывать его здесь неправильно — это его забота, не наша.</summary>
    public override bool SetComposingText(ICharSequence? text, int newCursorPosition) =>
        CommitText(text, newCursorPosition);
}