namespace ZeppelinForms;

/// <summary>
/// A nested event loop. Exists only where the platform owns the loop:
/// in the browser it is impossible to block the thread and keep receiving
/// events, so synchronous modality isn't supported there in principle.
/// </summary>
public interface INestedLoopSupport
{
    /// <summary>Spin events while the window is alive. Returns after it closes.</summary>
    void RunNestedLoop(IPlatformWindow until);
}