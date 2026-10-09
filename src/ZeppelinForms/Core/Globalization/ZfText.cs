using ZeppelinForms.Core.Globalization;

namespace ZeppelinForms.Core.Globalization;

/// <summary>The framework's own texts. An application overrides any of them
/// by registering a table of its own with the same keys.</summary>
public static class ZfText
{
    // ===== buttons =====

    public static readonly TextKey Ok = new("zf.button.ok", "OK");
    public static readonly TextKey Cancel = new("zf.button.cancel", "Cancel");
    public static readonly TextKey Yes = new("zf.button.yes", "Yes");
    public static readonly TextKey No = new("zf.button.no", "No");
    public static readonly TextKey Save = new("zf.button.save", "Save");
    public static readonly TextKey Select = new("zf.button.select", "Select");
    public static readonly TextKey Up = new("zf.button.up", "Up");

    // ===== dialog titles =====

    public static readonly TextKey MessageTitle = new("zf.title.message", "Message");
    public static readonly TextKey ConfirmTitle = new("zf.title.confirm", "Confirm");
    public static readonly TextKey ErrorTitle = new("zf.title.error", "Error");
    public static readonly TextKey InputTitle = new("zf.title.input", "Input");
    public static readonly TextKey NumberInputTitle = new("zf.title.number-input", "Enter a number");
    public static readonly TextKey OpenFileTitle = new("zf.title.open-file", "Open file");
    public static readonly TextKey SaveFileTitle = new("zf.title.save-file", "Save file");
    public static readonly TextKey SelectFolderTitle = new("zf.title.select-folder", "Select folder");

    // ===== the file dialog =====

    public static readonly TextKey AllFiles = new("zf.file.all-files", "All files");
    public static readonly TextKey FileExists = new("zf.file.exists", "\"{0}\" already exists. Replace it?");

    // ===== validation =====

    public static readonly TextKey Required = new("zf.validation.required", "This field is required");
    public static readonly TextKey InvalidEmail = new("zf.validation.email", "Invalid email address");
    public static readonly TextKey DigitsOnly = new("zf.validation.digits", "Digits only");

    public static readonly PluralKey TooShort = new(
        "zf.validation.too-short", one: "At least {0} character", other: "At least {0} characters");

    public static readonly PluralKey TooLong = new(
        "zf.validation.too-long", one: "At most {0} character", other: "At most {0} characters");

    // ===== AttachButton =====

    public static readonly TextKey AttachBrowse = new("zf.attach.browse", "Choose file…");
    public static readonly TextKey AttachEmpty = new("zf.attach.empty", "No file chosen");
    public static readonly TextKey AttachClear = new("zf.attach.clear", "Clear selection");

    public static readonly PluralKey FilesSelected = new(
        "zf.attach.files", one: "{0} file", other: "{0} files");

    // ===== CheckedComboBox =====

    public static readonly TextKey NothingSelected = new("zf.selection.none", "None selected");
    public static readonly TextKey SelectedCount = new("zf.selection.count", "Selected: {0}");

    // ===== RangeSlider =====

    public static readonly TextKey RangeLower = new("zf.range.lower", "Lower value");
    public static readonly TextKey RangeUpper = new("zf.range.upper", "Upper value");

    // ===== TabStrip =====

    public static readonly TextKey CloseTab = new("zf.tabs.close", "Close tab");
    public static readonly TextKey NewTab = new("zf.tabs.new", "New tab");


    // ===== SplitView =====

    /// <summary>The name of a split view's pane without a title of its own.</summary>
    public static readonly TextKey Pane = new("zf.splitview.pane", "Pane");

    // ===== RefreshContainer =====

    public static readonly TextKey Refreshing = new("zf.refresh.refreshing", "Refreshing");
    public static readonly TextKey Refreshed = new("zf.refresh.refreshed", "Updated");


    // ===== ImageGallery and ImageViewer =====

    /// <summary>The name of a picture with neither a title nor a description.</summary>
    public static readonly TextKey ImageNumber = new("zf.gallery.number", "Image {0}");

    /// <summary>The place of the picture shown: "3 / 20".</summary>
    public static readonly TextKey ImageCounter = new("zf.gallery.counter", "{0} / {1}");

    public static readonly TextKey PreviousImage = new("zf.gallery.previous", "Previous image");
    public static readonly TextKey NextImage = new("zf.gallery.next", "Next image");
    public static readonly TextKey CloseViewer = new("zf.gallery.close", "Close");
}