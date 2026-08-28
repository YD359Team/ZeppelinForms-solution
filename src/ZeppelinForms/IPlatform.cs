using ZeppelinForms.Forms;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms;

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);
    void RunModal(IPlatformWindow dialog, IPlatformWindow? owner);
    void Run();
    void Exit();
    void SetWindowState(WindowState state);
}
