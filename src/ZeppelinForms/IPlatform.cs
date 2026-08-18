using ZeppelinForms.Forms;

namespace ZeppelinForms;

public interface IPlatform
{
    IPlatformWindow CreateWindow(Form form);
    void Run();
    void Exit();
}
