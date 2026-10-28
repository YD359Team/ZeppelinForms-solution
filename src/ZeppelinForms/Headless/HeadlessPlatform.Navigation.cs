namespace ZeppelinForms.Headless;

/// <summary>The address a page "was opened at", for the tests of deep links.</summary>
public sealed partial class HeadlessPlatform
{
    /// <summary>The route a navigation stack with routes builds itself by, once.</summary>
    internal string? InitialRoute { get; set; }

    internal string? TakeInitialRoute()
    {
        string? route = InitialRoute;
        InitialRoute = null;
        return route;
    }
}
