using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Headless;

/// <summary>Возвращает одноцветную заглушку вместо разбора файла.</summary>
public sealed class HeadlessImageDecoder : ImageDecoder
{
    public int StubWidth { get; set; } = 64;
    public int StubHeight { get; set; } = 64;

    public static void Register() => Current = new HeadlessImageDecoder();

    public override Image Decode(Stream stream) =>
        new(StubWidth, StubHeight, new byte[StubWidth * StubHeight * 4]);
}
