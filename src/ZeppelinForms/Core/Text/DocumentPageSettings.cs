using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Core.Text;

/// <summary>Page size and orientation for <c>DocumentEdit</c>.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>A named paper size in device-independent units (1/96 inch, like WPF DIPs).</summary>
public readonly struct PageSize : IEquatable<PageSize>
{
    public PageSize(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public float Width { get; }
    public float Height { get; }

    public static PageSize A4 { get; } = new(794, 1123);      // 210×297 mm at 96 dpi
    public static PageSize Letter { get; } = new(816, 1056);  // 8.5×11 in
    public static PageSize Legal { get; } = new(816, 1344);   // 8.5×14 in
    public static PageSize A5 { get; } = new(559, 794);

    public PageSize For(PageOrientation orientation) =>
        orientation == PageOrientation.Landscape && Width < Height
            ? new PageSize(Height, Width)
            : orientation == PageOrientation.Portrait && Width > Height
                ? new PageSize(Height, Width)
                : this;

    public bool Equals(PageSize other) => Width.Equals(other.Width) && Height.Equals(other.Height);
    public override bool Equals(object? obj) => obj is PageSize other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    public static bool operator ==(PageSize left, PageSize right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PageSize left, PageSize right)
    {
        return !(left == right);
    }
}

/// <summary>Margins, size, orientation and header/footer slots for a document.</summary>
public sealed class DocumentPageSettings
{
    public PageSize Paper { get; set; } = PageSize.A4;
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    /// <summary>Page margins in DIPs.</summary>
    public Thickness Margins { get; set; } = new(96, 96, 96, 96); // 1 inch

    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }

    /// <summary>When true, footer shows <c>Page N</c> after <see cref="FooterText"/>.</summary>
    public bool ShowPageNumbers { get; set; } = true;

    public PageSize EffectiveSize => Paper.For(Orientation);

    public float ContentWidth =>
        Math.Max(0, EffectiveSize.Width - Margins.Left - Margins.Right);

    public float ContentHeight =>
        Math.Max(0, EffectiveSize.Height - Margins.Top - Margins.Bottom);
}

/// <summary>One laid-out page ready to draw or print.</summary>
public sealed class DocumentPage
{
    public DocumentPage(int index, int textStart, int textLength)
    {
        Index = index;
        TextStart = textStart;
        TextLength = textLength;
    }

    public int Index { get; }
    public int TextStart { get; }
    public int TextLength { get; }
    public int TextEnd => TextStart + TextLength;
}

/// <summary>Request sent when the document is printed.</summary>
public sealed class DocumentPrintRequest
{
    public required DocumentPageSettings Settings { get; init; }
    public required IReadOnlyList<DocumentPage> Pages { get; init; }
    public required string PlainText { get; init; }
    public required RichTextDocument Document { get; init; }
}
