using System.Text;
using ZeppelinForms.Forms.Controls.Base;

namespace ZeppelinForms.Forms.Controls.Tools;

public static class UIElementDebugExtensions
{
    public static string DumpTree(this UIElement root)
    {
        var sb = new StringBuilder();
        Walk(root, 0, sb);
        return sb.ToString();
    }

    private static void Walk(UIElement element, int depth, StringBuilder sb)
    {
        sb.AppendLine(
            $"{new string(' ', depth * 2)}{element.GetType().Name} \"{element.Name}\" " +
            $"Pos={element.Position.X:0},{element.Position.Y:0} " +
            $"Size={element.Size.Width:0}x{element.Size.Height:0} " +
            $"Dock={element.Docking} Visible={element.IsVisible}");

        switch (element)
        {
            case WrapControl wrap when wrap.Child is not null:
                Walk(wrap.Child, depth + 1, sb);
                break;

            case PanelControl panel:
                foreach (var child in panel.Children)
                    Walk(child, depth + 1, sb);
                break;
        }
    }
}