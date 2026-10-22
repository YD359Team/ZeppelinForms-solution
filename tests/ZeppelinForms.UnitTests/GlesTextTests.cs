using System.Buffers.Binary;
using System.Text;
using Xunit;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Gles.Text;
using ZeppelinForms.Gles.Text.Fonts;
using ZeppelinForms.Gles.Text.Layout;
using ZeppelinForms.Gles.Text.OpenType;

namespace ZeppelinForms.UnitTests;

/// <summary>The GLES backend's text without a GPU: a TrueType font made here, read, laid
/// out and rasterized; the system's fonts where the machine has some.</summary>
public class GlesTextTests
{
    #region A tiny TrueType font

    /// <summary>A font of four glyphs at 1000 units per em: none, "A" a square of 300 by
    /// 600 from x 100, "B" that square moved by (200, 100) as a composite, a space. Ascender
    /// 800, descender -200; advances 500, 600 and 250.</summary>
    private static readonly Lazy<string> TinyFontPath = new(() =>
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"zf-tiny-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, BuildTinyFont());
        return path;
    });

    private static Font Tiny(float size, FontWeight weight = FontWeight.Normal) =>
        new Font("ZfTiny", size, weight).WithFile(TinyFontPath.Value);

    private static byte[] BuildTinyFont()
    {
        static byte[] Be(params int[] shorts)
        {
            var bytes = new byte[shorts.Length * 2];
            for (int i = 0; i < shorts.Length; i++) BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(i * 2), (short)shorts[i]);
            return bytes;
        }

        byte[] square =
        [
            .. Be(1, 100, 0, 400, 600),     // one contour, its box
            .. Be(3, 0),                    // the last point's index, no instructions
            1, 1, 1, 1,                     // four points on the curve, coordinates as words
            .. Be(100, 300, 0, -300),       // x deltas
            .. Be(0, 0, 600, 0),            // y deltas
        ];

        byte[] composite =
        [
            .. Be(-1, 300, 100, 600, 700),
            .. Be(0x0003, 1, 200, 100),     // words, x/y offsets; glyph 1 at (200, 100)
        ];

        int[] loca = [0, 0, square.Length, square.Length + composite.Length, square.Length + composite.Length];
        byte[] glyf = [.. square, .. composite];

        byte[] head = new byte[54];
        BinaryPrimitives.WriteInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);

        byte[] hhea = new byte[36];
        BinaryPrimitives.WriteInt32BigEndian(hhea, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), 800);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), -200);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), 4);

        byte[] maxp = [0, 0, 0x50, 0, .. Be(4)];
        byte[] hmtx = Be(500, 0, 500, 100, 600, 300, 250, 0);

        // format 4: ' ' → 3, 'A'..'B' → 1..2, the closing segment
        byte[] format4 =
        [
            .. Be(4, 16 + 3 * 8, 0, 6, 4, 1, 2),
            .. Be(0x20, 0x42, 0xFFFF), .. Be(0),
            .. Be(0x20, 0x41, 0xFFFF),
            .. Be(3 - 0x20, 1 - 0x41, 1),
            .. Be(0, 0, 0),
        ];

        byte[] cmap = [.. Be(0, 1, 3, 1), 0, 0, 0, 12, .. format4];

        byte[] family = Encoding.BigEndianUnicode.GetBytes("ZfTiny");
        byte[] name = [.. Be(0, 1, 18, 3, 1, 0x409, 1, family.Length, 0), .. family];

        byte[] locaBytes = new byte[loca.Length * 4];
        for (int i = 0; i < loca.Length; i++) BinaryPrimitives.WriteInt32BigEndian(locaBytes.AsSpan(i * 4), loca[i]);

        (string Tag, byte[] Data)[] tables =
        [
            ("cmap", cmap), ("glyf", glyf), ("head", head), ("hhea", hhea),
            ("hmtx", hmtx), ("loca", locaBytes), ("maxp", maxp), ("name", name),
        ];

        using var file = new MemoryStream();
        file.Write([0, 1, 0, 0, .. Be(tables.Length, 0, 0, 0)]);

        int offset = 12 + tables.Length * 16;

        foreach ((string tag, byte[] data) in tables)
        {
            file.Write(Encoding.ASCII.GetBytes(tag));
            file.Write(new byte[4]);

            var record = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(record, offset);
            BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(4), data.Length);
            file.Write(record);

            offset += (data.Length + 3) & ~3;
        }

        foreach ((_, byte[] data) in tables)
        {
            file.Write(data);
            file.Write(new byte[((data.Length + 3) & ~3) - data.Length]);
        }

        return file.ToArray();
    }

    private static FontFace TinyFace() => FontFile.Load(TinyFontPath.Value).Face(0)!;

    #endregion

    [Fact]
    public void AFaceGivesItsNamesAndMetrics()
    {
        FontFace face = TinyFace();

        Assert.Equal("ZfTiny", face.Family);
        Assert.Equal(1000, face.UnitsPerEm);
        Assert.Equal(800, face.Ascender);
        Assert.Equal(-200, face.Descender);
        Assert.Equal(4, face.NumGlyphs);
    }

    [Fact]
    public void CharactersMapToTheirGlyphs()
    {
        FontFace face = TinyFace();

        Assert.Equal((ushort)1, face.GlyphFor('A'));
        Assert.Equal((ushort)2, face.GlyphFor('B'));
        Assert.Equal((ushort)3, face.GlyphFor(' '));
        Assert.Equal((ushort)0, face.GlyphFor('C'));
        Assert.Equal((ushort)0, face.GlyphFor(0x1F600));

        Assert.Equal(500, face.Advance(1));
        Assert.Equal(600, face.Advance(2));
        Assert.Equal(250, face.Advance(3));
    }

    [Fact]
    public void OutlinesAndCompositesHaveTheirBoxes()
    {
        FontFace face = TinyFace();

        Assert.Equal(new Rectangle(100, 0, 300, 600), face.Bounds(1));

        // the composite is the square moved by its component's offset
        Assert.Equal(new Rectangle(300, 100, 300, 600), face.Bounds(2));

        Assert.Null(face.Outline(3));
    }

    [Fact]
    public void ALineIsAsWideAsItsAdvances()
    {
        var measurer = new GlesTextMeasurer();
        Font font = Tiny(10);

        // A 5, B 6, space 2.5, A 5
        Assert.Equal(18.5f, measurer.MeasureText("AB A", font).Width, 0.001f);
        Assert.Equal(11f, measurer.MeasureTextWidth("AB A", 2, font), 0.001f);
        Assert.Equal(0f, measurer.MeasureTextWidth("AB A", 0, font), 0.001f);
    }

    [Fact]
    public void ALineIsAsHighAsItsInk()
    {
        TextLine line = LineBuilder.Get("A", Tiny(10));

        // the square from x 1 to 4, from the baseline 6 up
        Assert.Equal(new Rectangle(1, -6, 3, 6), line.InkBounds);
        Assert.Equal(6f, new GlesTextMeasurer().MeasureText("A", Tiny(10)).Height, 0.001f);
    }

    [Fact]
    public void TheFontsLineIsItsAscentAndDescent()
    {
        FontMetrics metrics = new GlesTextMeasurer().MeasureFont(Tiny(10));

        Assert.Equal(8f, metrics.Ascent, 0.001f);
        Assert.Equal(2f, metrics.Descent, 0.001f);
    }

    [Fact]
    public void TheCharactersBoxesFollowOneAnother()
    {
        TextLineLayout layout = new GlesTextMeasurer().LayOutLine("AB", Tiny(10), Core.Text.FlowDirection.LeftToRight);

        Assert.Equal((0f, 5f), layout.GetCharacterBounds(0));
        Assert.Equal((5f, 11f), layout.GetCharacterBounds(1));
    }

    [Fact]
    public void ATabIsLaidOutAsASpace()
    {
        var measurer = new GlesTextMeasurer();

        Assert.Equal(measurer.MeasureText("A A", Tiny(10)).Width, measurer.MeasureText("A\tA", Tiny(10)).Width, 0.001f);
    }

    [Fact]
    public void ABoldOfAFileIsSynthesized()
    {
        Typeface regular = FontResolver.Resolve(Tiny(10))!;
        Typeface bold = FontResolver.Resolve(Tiny(10, FontWeight.Bold))!;

        Assert.False(regular.Embolden);
        Assert.True(bold.Embolden);
        Assert.Same(regular.Face, bold.Face);
    }

    [Fact]
    public void AGlyphRasterizesToItsPixels()
    {
        GlyphBitmap bitmap = new GlyphRasterizer().Rasterize(FontResolver.Resolve(Tiny(10))!, 1, 10, 0);

        Assert.False(bitmap.IsEmpty);

        // inside the square: covered; the box reaches a pixel past it all round
        int insideX = 2 - bitmap.Left, insideY = -3 - bitmap.Top;
        Assert.Equal((byte)255, bitmap.Coverage![insideY * bitmap.Width + insideX]);
        Assert.Equal((byte)0, bitmap.Coverage[0]);

        Assert.True(bitmap.Left <= 1 && bitmap.Left + bitmap.Width >= 4);
        Assert.True(bitmap.Top <= -6 && bitmap.Top + bitmap.Height >= 0);
    }

    [Fact]
    public void ASynthesizedBoldIsWider()
    {
        var rasterizer = new GlyphRasterizer();

        GlyphBitmap regular = rasterizer.Rasterize(FontResolver.Resolve(Tiny(48))!, 1, 48, 0);
        GlyphBitmap bold = rasterizer.Rasterize(FontResolver.Resolve(Tiny(48, FontWeight.Bold))!, 1, 48, 0);

        int Ink(GlyphBitmap b) => b.Coverage!.Sum(c => c);

        Assert.True(Ink(bold) > Ink(regular), "the widened outline covers more");
    }

    [Fact]
    public void TheSystemsDefaultFontDrawsLatinText()
    {
        Typeface? typeface = FontResolver.Resolve(Font.Default);
        Assert.SkipUnless(typeface is not null, "The machine has no fonts.");

        var measurer = new GlesTextMeasurer();

        float hello = measurer.MeasureText("Hello", Font.Default).Width;
        float h = measurer.MeasureTextWidth("Hello", 1, Font.Default);

        Assert.True(hello > 0);
        Assert.True(h > 0 && h < hello);

        FontMetrics metrics = measurer.MeasureFont(Font.Default);
        Assert.True(metrics.Ascent > 0 && metrics.Descent > 0);
    }

    [Fact]
    public void AMissingCharacterComesFromAFallback()
    {
        Typeface? primary = FontResolver.Resolve(Font.Default);
        Assert.SkipUnless(primary is not null, "The machine has no fonts.");

        // a CJK character the primary latin font hasn't got, where some font has it
        Assert.SkipUnless(primary!.Face.GlyphFor(0x4E2D) == 0, "The default font covers CJK itself.");

        Typeface? fallback = FontResolver.Fallback(Font.Default, 0x4E2D);
        Assert.SkipUnless(fallback is not null, "No font of the machine has CJK.");

        TextLine line = LineBuilder.Get("a中", Font.Default);

        Assert.Equal(2, line.Runs.Length);
        Assert.Same(fallback, line.Runs[1].Typeface);
    }
}