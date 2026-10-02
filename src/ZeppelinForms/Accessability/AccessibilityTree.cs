using System.Globalization;
using System.Text;

namespace ZeppelinForms.Accessibility;

/// <summary>The accessibility tree as text: what a screen reader would find, one
/// peer per line. For tests — the semantics checked the way snapshots check
/// pixels — and for looking at a form's tree while debugging.</summary>
/// <remarks>
/// A line reads <c>Role "Name" = "Value" [min..max: value] {states} (index/count) level</c>,
/// each part only where the peer has it. Layout containers with no role of their
/// own are left out by default and their children lifted, as most bridges do.
/// </remarks>
public static class AccessibilityTree
{
    public static string Dump(AccessibilityPeer root, bool includeGeneric = false)
    {
        var builder = new StringBuilder();
        Write(root, 0, includeGeneric, builder);

        return builder.ToString();
    }

    private static void Write(AccessibilityPeer peer, int depth, bool includeGeneric, StringBuilder builder)
    {
        bool shown = includeGeneric || peer.Role != AccessibilityRole.None;

        if (shown)
        {
            builder.Append(' ', depth * 2).Append(Line(peer)).Append('\n');
            depth++;
        }

        foreach (AccessibilityPeer child in peer.Children)
            Write(child, depth, includeGeneric, builder);
    }

    /// <summary>One peer as one line, without the indent.</summary>
    public static string Line(AccessibilityPeer peer)
    {
        var line = new StringBuilder(peer.Role.ToString());

        if (peer.Name.Length > 0)
            line.Append(" \"").Append(peer.Name).Append('"');

        if (peer.Value is { } value)
            line.Append(" = \"").Append(value).Append('"');

        if (peer.Range is { } range)
        {
            line.Append(CultureInfo.InvariantCulture,
                $" [{range.Minimum:G}..{range.Maximum:G}: {range.Value:G}]");
        }

        if (peer.States != AccessibilityStates.None)
        {
            IEnumerable<string> states = Enum.GetValues<AccessibilityStates>()
                .Where(state => state != AccessibilityStates.None && peer.States.HasFlag(state))
                .Select(state => state.ToString().ToLowerInvariant());

            line.Append(" {").Append(string.Join(", ", states)).Append('}');
        }

        if (peer.Position is { } position)
            line.Append(CultureInfo.InvariantCulture, $" ({position.Index}/{position.Count})");

        if (peer.Level > 0)
            line.Append(CultureInfo.InvariantCulture, $" level {peer.Level}");

        return line.ToString();
    }
}