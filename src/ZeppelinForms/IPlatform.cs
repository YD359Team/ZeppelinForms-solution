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
}