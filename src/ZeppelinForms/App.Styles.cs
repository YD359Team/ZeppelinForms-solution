using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Styling;

namespace ZeppelinForms;

/// <summary>The application's styles: the widest scope, under every form.</summary>
public partial class App
{
    private static Styles? s_styles;

    private static readonly System.Threading.Lock s_stylesSync = new();

    /// <summary>Styles for every form of the application. A form's own styles and an
    /// element's beat these at equal specificity; any style beats the theme.</summary>
    /// <remarks>
    /// Static, like <see cref="Theme"/>: there is one application per process, and
    /// styles are usually added before the first form is built, where no instance
    /// is at hand yet.
    /// </remarks>
    public static Styles Styles
    {
        get
        {
            if (s_styles is not null) return s_styles;

            lock (s_stylesSync)
                return s_styles ??= new Styles(Form.RestyleOpenForms);
        }
    }

    internal static Styles? StylesOrNull => s_styles;
}