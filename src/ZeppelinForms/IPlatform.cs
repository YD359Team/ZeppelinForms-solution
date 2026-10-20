using ZeppelinForms.Forms;

namespace ZeppelinForms;

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);

    /// <summary>Start the application. On desktop platforms it doesn't return until
    /// exit; where the host owns the loop — the browser, Android — it returns
    /// immediately, and frames come through IFrameDriver.</summary>
    void Start();

    void Exit();


    /// <summary>What this platform can do beyond the minimum. None by default: a
    /// platform of an external package that says nothing promises nothing.</summary>
    PlatformCapabilities Capabilities => PlatformCapabilities.None;
}