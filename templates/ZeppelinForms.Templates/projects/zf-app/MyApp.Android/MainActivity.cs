using Android.App;
using Android.Content.PM;
using Android.OS;
using ZeppelinForms.Android;

namespace MyApp.Android;

[Activity(
    Label = "MyApp",
    MainLauncher = true,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize,

    // a turn of the screen doesn't make the activity, and the forms with it, anew:
    // the form gets its new size and keeps what was typed and scrolled
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.Density)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Startup.Configure();

        // files of the Assets folder the application reads go into assets: ["logo.png"]
        AndroidApp.Run(this, () => new MainForm());
    }
}
