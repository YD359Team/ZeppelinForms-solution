using ZeppelinForms.Browser;
using ZeppelinForms.Drawing;
using ZeppelinForms.Theming;
using ZF_SharedLib;
using ZF_SharedLib.Showcase;

namespace ZF_Wasm;

public class Program
{
    static Task Main()
    {
        // Inter has Latin, Cyrillic and Greek, but no Hebrew and no Arabic, and the
        // browser has no fonts to fall back to: the text input showcase drew them as
        // boxes. Noto fills in (SIL Open Font License, see fonts/OFL.txt)
        FontFallbacks.Add("/fonts/NotoSansHebrew-Regular.ttf");
        FontFallbacks.Add("/fonts/NotoSansArabic-Regular.ttf");
        FontFallbacks.Add("/fonts/NotoSansJP-Regular.otf"); // CJK: kana and kanji

        // the showcase's native control here: an element of the page — a map in an iframe
        NativePage.Sample = () => new HtmlElementHost
        {
            TagName = "iframe",
            Attributes =
            {
                ["src"] = "https://www.openstreetmap.org/export/embed.html?bbox=37.58,55.74,37.66,55.77&layer=mapnik",
                ["title"] = "OpenStreetMap",
            },
        };

        return BrowserApp.RunAsync(
            () => new ExampleMainForm(),
            font: "/fonts/Inter-Regular.ttf",
            preload: ["/Assets/Laughing.png", "/Assets/styles.zss"],
            theme: Themes.FluentLight);
    }
}