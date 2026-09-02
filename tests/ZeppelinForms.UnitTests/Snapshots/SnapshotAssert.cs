using System.Runtime.CompilerServices;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Skia;

namespace ZeppelinForms.UnitTests.Snapshots;

public static class SnapshotAssert
{
    private static bool IsContinuousIntegration =>
         Environment.GetEnvironmentVariable("CI") == "true" ||
         Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <summary>Разрешено создавать отсутствующие эталоны.</summary>
    private static bool AllowCreate =>
        !IsContinuousIntegration ||
        Environment.GetEnvironmentVariable("ZF_CREATE_SNAPSHOTS") == "true" ||
        ForceOverwrite;

    /// <summary>Перезаписать эталоны, даже если они уже есть.</summary>
    private static bool ForceOverwrite =>
        Environment.GetEnvironmentVariable("ZF_UPDATE_SNAPSHOTS") == "true";

    private const int DefaultTolerance = 4;

    /// <summary>Доля различающихся пикселей, ниже которой снимок считается совпавшим.</summary>
    private const float AllowedDifferenceRatio = 0.002f;

    /// <summary>Эталоны лежат в исходниках, а не в bin — иначе их нельзя
    /// закоммитить, и каждый прогон CI создавал бы их заново.</summary>
    private static readonly string ExpectedDirectory = ResolveExpectedDirectory();

    private static string FailedDirectory => Path.Combine(AppContext.BaseDirectory, "Snapshots", "Failed");

    /// <summary>Шрифт из файла — системные различаются между машинами
    /// и делают снимки невоспроизводимыми.</summary>
    public static Font TestFont { get; } = new("Snapshot", 14)
    {
        FilePath = Path.Combine(AppContext.BaseDirectory, "Snapshots", "Fonts", "DejaVuSans.ttf"),
    };

    public static void Matches(UIElement element, string name, int tolerance = DefaultTolerance)
    {
        Compare(element.RenderToImage(), name, tolerance);
    }

    public static void Matches(Form form, string name, float scale = 1f, int tolerance = DefaultTolerance)
    {
        Image actual = new SkiaOffscreenRenderer().RenderForm(
            form,
            (int)MathF.Ceiling(form.ClientSize.Width * scale),
            (int)MathF.Ceiling(form.ClientSize.Height * scale),
            scale);

        // суффикс только для нестандартного масштаба — иначе обычные снимки
        // получают лишнее «@1x» в имени без всякой пользы
        string snapshotName = Math.Abs(scale - 1f) < 0.001f ? name : $"{name}@{scale:0.##}x";

        Compare(actual, snapshotName, tolerance);
    }

    private static void Compare(Image actual, string name, int tolerance)
    {
        string expectedRaw = Path.Combine(ExpectedDirectory, name + ".raw");

        if (ForceOverwrite)
        {
            WriteExpected(expectedRaw, name, actual);
            return;
        }

        if (!File.Exists(expectedRaw))
        {
            if (!AllowCreate)
            {
                SaveFailure(name, actual, expected: null);

                throw new Xunit.Sdk.XunitException(
                    $"Эталон '{name}' отсутствует в репозитории. " +
                    "Снимите его локально или прогоном с ZF_CREATE_SNAPSHOTS и закоммитьте.");
            }

            WriteExpected(expectedRaw, name, actual);

            throw new Xunit.Sdk.XunitException(
                $"Эталон '{name}' создан в {ExpectedDirectory}. Проверьте PNG и добавьте файлы в git.");
        }

        Image expected = LoadRaw(expectedRaw);

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            SaveFailure(name, actual, expected: null);

            throw new Xunit.Sdk.XunitException(
                $"Размер снимка '{name}' изменился: было {expected.Width}x{expected.Height}, " +
                $"стало {actual.Width}x{actual.Height}.");
        }

        int different = CountDifferentPixels(expected, actual, tolerance);
        int total = actual.Width * actual.Height;

        if (different > total * AllowedDifferenceRatio)
        {
            SaveFailure(name, actual, expected);

            throw new Xunit.Sdk.XunitException(
                $"Снимок '{name}' отличается: {different} из {total} пикселей " +
                $"({different * 100f / total:0.##}%). Сравните PNG в {FailedDirectory}.");
        }
    }

    private static int CountDifferentPixels(Image expected, Image actual, int tolerance)
    {
        int different = 0;

        for (int i = 0; i < expected.Pixels.Length; i += 4)
        {
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

    private static void SaveFailure(string name, Image actual, Image? expected)
    {
        Directory.CreateDirectory(FailedDirectory);

        SaveRaw(Path.Combine(FailedDirectory, name + ".raw"), actual);
        SkiaOffscreenRenderer.SavePng(actual, Path.Combine(FailedDirectory, name + ".actual.png"));

        if (expected is not null)
        {
            SkiaOffscreenRenderer.SavePng(expected, Path.Combine(FailedDirectory, name + ".expected.png"));
            SkiaOffscreenRenderer.SavePng(BuildDiff(expected, actual), Path.Combine(FailedDirectory, name + ".diff.png"));
        }
    }

    /// <summary>Карта различий: совпавшее приглушается, отличия красным.</summary>
    private static Image BuildDiff(Image expected, Image actual)
    {
        byte[] pixels = new byte[actual.Pixels.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            bool same =
                expected.Pixels[i] == actual.Pixels[i] &&
                expected.Pixels[i + 1] == actual.Pixels[i + 1] &&
                expected.Pixels[i + 2] == actual.Pixels[i + 2];

            if (same)
            {
                byte grey = (byte)((actual.Pixels[i] + actual.Pixels[i + 1] + actual.Pixels[i + 2]) / 6 + 128);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = grey;
                pixels[i + 3] = 255;
            }
            else
            {
                pixels[i] = 255;
                pixels[i + 1] = pixels[i + 2] = 0;
                pixels[i + 3] = 255;
            }
        }

        return new Image(actual.Width, actual.Height, pixels);
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

        return new Image(width, height, reader.ReadBytes(width * height * 4));
    }

    private static string PlatformFolder =>
        OperatingSystem.IsWindows() ? "win"
        : OperatingSystem.IsLinux() ? "linux"
        : "other";

    private static string ResolveExpectedDirectory([CallerFilePath] string sourceFilePath = "")
    {
        string? directory = Path.GetDirectoryName(sourceFilePath);

        // отрисовка текста заметно отличается между платформами,
        // поэтому эталоны храним отдельно для каждой
        string root = directory is not null && Directory.Exists(directory)
            ? Path.Combine(directory, "Expected")
            : Path.Combine(AppContext.BaseDirectory, "Snapshots", "Expected");

        return Path.Combine(root, PlatformFolder);
    }

    private static void WriteExpected(string rawPath, string name, Image actual)
    {
        Directory.CreateDirectory(ExpectedDirectory);

        SaveRaw(rawPath, actual);
        SkiaOffscreenRenderer.SavePng(actual, Path.Combine(ExpectedDirectory, name + ".png"));

        Console.WriteLine($"[снимок] записан эталон: {rawPath}");
    }
}