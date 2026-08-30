using System;
using System.Collections.Generic;
using System.Text;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.UnitTests.Snapshots;

public static class SnapshotAssert
{
    private const int DefaultTolerance = 2;

    private static string SnapshotDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", "Expected");

    private static string FailureDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Snapshots", "Failed");

    /// <summary>Сравнить отрисовку элемента с эталоном. Эталон создаётся
    /// автоматически при первом запуске — его нужно проверить глазами и закоммитить.</summary>
    public static void Matches(UIElement element, string name, int tolerance = DefaultTolerance)
    {
        Image actual = element.RenderToImage();

        Directory.CreateDirectory(SnapshotDirectory);
        string expectedPath = Path.Combine(SnapshotDirectory, name + ".raw");

        if (!File.Exists(expectedPath))
        {
            SaveRaw(expectedPath, actual);

            throw new Xunit.Sdk.XunitException(
                $"Эталон '{name}' не найден и был создан. Проверьте его и добавьте в репозиторий.");
        }

        Image expected = LoadRaw(expectedPath);

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            SaveFailure(name, actual);

            throw new Xunit.Sdk.XunitException(
                $"Размер снимка '{name}' изменился: было {expected.Width}x{expected.Height}, " +
                $"стало {actual.Width}x{actual.Height}.");
        }

        int different = CountDifferentPixels(expected, actual, tolerance);

        if (different > 0)
        {
            SaveFailure(name, actual);

            float percent = different * 100f / (actual.Width * actual.Height);

            throw new Xunit.Sdk.XunitException(
                $"Снимок '{name}' отличается: {different} пикселей ({percent:0.##}%). " +
                $"Фактический результат сохранён в {FailureDirectory}.");
        }
    }

    private static int CountDifferentPixels(Image expected, Image actual, int tolerance)
    {
        int different = 0;

        for (int i = 0; i < expected.Pixels.Length; i += 4)
        {
            // допуск нужен из-за сглаживания: одна и та же картинка на разных
            // машинах может отличаться на единицу в младшем разряде
            if (Math.Abs(expected.Pixels[i] - actual.Pixels[i]) > tolerance ||
                Math.Abs(expected.Pixels[i + 1] - actual.Pixels[i + 1]) > tolerance ||
                Math.Abs(expected.Pixels[i + 2] - actual.Pixels[i + 2]) > tolerance ||
                Math.Abs(expected.Pixels[i + 3] - actual.Pixels[i + 3]) > tolerance)
            {
                different++;
            }
        }

        return different;
    }

    private static void SaveRaw(string path, Image image)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write(image.Width);
        writer.Write(image.Height);
        writer.Write(image.Pixels);
    }

    private static Image LoadRaw(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        byte[] pixels = reader.ReadBytes(width * height * 4);

        return new Image(width, height, pixels);
    }

    private static void SaveFailure(string name, Image image)
    {
        Directory.CreateDirectory(FailureDirectory);
        SaveRaw(Path.Combine(FailureDirectory, name + ".raw"), image);
    }
}
