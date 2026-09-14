using Android.App;
using Android.Content.PM;
using Android.OS;
using ZeppelinForms.Android;
using ZF_SharedLib;

namespace ZF_Android;

[Activity(
    Label = "ZF Android",
    MainLauncher = true,
    // без этого поворот экрана и смена плотности пересоздают активность,
    // а с ней и всё дерево форм — состояние примера терялось бы каждый раз.
    // Размер приедет сам, через ZeppelinView.OnSizeChanged
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.Density)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        AndroidApp.Run(this, () => new ExampleMainForm(), assets: ["Laughing.png"]);
    }
}