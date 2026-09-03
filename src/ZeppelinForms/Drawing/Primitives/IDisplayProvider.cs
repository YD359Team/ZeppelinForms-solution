namespace ZeppelinForms.Drawing.Primitives;

public interface IDisplayProvider
{
    IReadOnlyList<DisplayInfo> GetDisplays();
}