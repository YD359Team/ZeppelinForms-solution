using ZeppelinForms.Forms;

namespace ZeppelinForms;

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);
    void RunModal(IPlatformWindow dialog, IPlatformWindow? owner);
    void Run();
    void Exit();
}
