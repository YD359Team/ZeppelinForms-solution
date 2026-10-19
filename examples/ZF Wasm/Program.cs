using ZeppelinForms.Browser;
using ZeppelinForms.Drawing;
using ZeppelinForms.Theming;
using ZF_SharedLib;

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
        FontFallbacks.Add("/fonts/NotoSansJP-Regular.ttf"); // a new one: CJK, for example

        return BrowserApp.RunAsync(
            () => new ExampleMainForm(),
            font: "/fonts/Inter-Regular.ttf",
            preload: ["/Assets/Laughing.png", "/Assets/styles.zss"],
            theme: Themes.FluentLight);
    }
}