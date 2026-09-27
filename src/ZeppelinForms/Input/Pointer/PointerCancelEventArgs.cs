using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Input.Pointer;

/// <summary>The interaction was cut off, there will be no completion.</summary>
/// <remarks>
/// There is deliberately no Handled here. A cancel is not an offer: by the time
/// it is delivered the contact is already gone, and refusing it means staying
/// pressed forever.
/// </remarks>
public sealed record class PointerCancelEventArgs(
    int PointerId,
    PointerKind Kind,
    Point Location,
    PointerCancelReason Reason) : ZfEventArgs;