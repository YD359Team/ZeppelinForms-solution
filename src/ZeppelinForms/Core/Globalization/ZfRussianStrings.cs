using ZeppelinForms.Core.Globalization;

namespace ZeppelinForms.Core.Globalization;

/// <summary>The built-in Russian table. Registered by Localization itself;
/// an application's table for "ru" registered later overrides any line of it.</summary>
internal sealed class ZfRussianStrings : StringTable
{
    public ZfRussianStrings() : base("ru")
    {
        Add(ZfText.Ok, "ОК");
        Add(ZfText.Cancel, "Отмена");
        Add(ZfText.Yes, "Да");
        Add(ZfText.No, "Нет");
        Add(ZfText.Save, "Сохранить");
        Add(ZfText.Select, "Выбрать");
        Add(ZfText.Up, "Вверх");

        Add(ZfText.MessageTitle, "Сообщение");
        Add(ZfText.ConfirmTitle, "Подтверждение");
        Add(ZfText.ErrorTitle, "Ошибка");
        Add(ZfText.InputTitle, "Ввод");
        Add(ZfText.NumberInputTitle, "Ввод числа");
        Add(ZfText.OpenFileTitle, "Открыть файл");
        Add(ZfText.SaveFileTitle, "Сохранить файл");
        Add(ZfText.SelectFolderTitle, "Выбрать папку");

        Add(ZfText.AllFiles, "Все файлы");
        Add(ZfText.FileExists, "Файл «{0}» уже есть. Заменить?");

        Add(ZfText.Required, "Поле обязательно");
        Add(ZfText.InvalidEmail, "Некорректный адрес");
        Add(ZfText.DigitsOnly, "Только цифры");

        // "не короче 21 символа", but "не короче 2 символов" and "5 символов":
        // after a comparison the genitive plural covers both few and many
        Add(ZfText.TooShort, one: "Не короче {0} символа", few: "Не короче {0} символов", many: "Не короче {0} символов");
        Add(ZfText.TooLong, one: "Не длиннее {0} символа", few: "Не длиннее {0} символов", many: "Не длиннее {0} символов");

        Add(ZfText.AttachBrowse, "Выбрать файл…");
        Add(ZfText.AttachEmpty, "Файл не выбран");
        Add(ZfText.AttachClear, "Сбросить выбор");
        Add(ZfText.FilesSelected, one: "{0} файл", few: "{0} файла", many: "{0} файлов");

        Add(ZfText.NothingSelected, "Не выбрано");
        Add(ZfText.SelectedCount, "Выбрано: {0}");

        Add(ZfText.RangeLower, "Нижнее значение");
        Add(ZfText.RangeUpper, "Верхнее значение");

        Add(ZfText.CloseTab, "Закрыть вкладку");
        Add(ZfText.NewTab, "Новая вкладка");

        Add(ZfText.Pane, "Панель");

        Add(ZfText.Refreshing, "Обновление");
        Add(ZfText.Refreshed, "Обновлено");

        Add(ZfText.ImageNumber, "Изображение {0}");
        Add(ZfText.ImageCounter, "{0} из {1}");
        Add(ZfText.PreviousImage, "Предыдущее изображение");
        Add(ZfText.NextImage, "Следующее изображение");
        Add(ZfText.CloseViewer, "Закрыть");
    }
}