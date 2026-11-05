using System.Globalization;
using ZeppelinForms.Forms.Controls;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Controls.Text;
using ZeppelinForms.Forms.Enums;

namespace ZeppelinForms.Accessibility.Peers;

/// <summary>Rich, code or document fields: the plain text is the value.</summary>
public class RichTextBoxPeer : UIElementPeer
{
    private readonly RichTextBox _box;

    public RichTextBoxPeer(RichTextBox owner) : base(owner)
    {
        _box = owner;
        owner.TextChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
        owner.SelectionChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Caret);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TextBox;
    public override string? Value => _box.Text;
    public override int CaretIndex => _box.CaretIndex;
    public override int SelectionStart => _box.SelectionStart;
    public override int SelectionLength => _box.SelectionLength;

    protected override AccessibilityStates ControlStates =>
        (_box.IsReadOnly ? AccessibilityStates.ReadOnly : 0) | AccessibilityStates.Multiline;

    public override bool SetValue(string value)
    {
        if (_box.IsReadOnly || !Owner.IsEffectivelyEnabled) return false;
        _box.Text = value;
        return true;
    }
}

/// <summary>A code editor field.</summary>
public class CodeEditPeer : UIElementPeer
{
    private readonly CodeEdit _box;

    public CodeEditPeer(CodeEdit owner) : base(owner)
    {
        _box = owner;
        owner.TextChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
        owner.SelectionChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Caret);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TextBox;
    public override string? Value => _box.Text;
    public override int CaretIndex => _box.CaretIndex;
    public override int SelectionStart => _box.SelectionStart;
    public override int SelectionLength => _box.SelectionLength;

    protected override AccessibilityStates ControlStates =>
        (_box.IsReadOnly ? AccessibilityStates.ReadOnly : 0) | AccessibilityStates.Multiline;

    public override bool SetValue(string value)
    {
        if (_box.IsReadOnly || !Owner.IsEffectivelyEnabled) return false;
        _box.Text = value;
        return true;
    }
}

/// <summary>A paginated document field.</summary>
public class DocumentEditPeer : UIElementPeer
{
    private readonly DocumentEdit _box;

    public DocumentEditPeer(DocumentEdit owner) : base(owner)
    {
        _box = owner;
        owner.TextChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Value);
        owner.SelectionChanged += (_, _) => RaisePropertyChanged(AccessibilityProperty.Caret);
    }

    protected override AccessibilityRole DefaultRole => AccessibilityRole.TextBox;
    public override string? Value => _box.Text;
    public override int CaretIndex => _box.CaretIndex;
    public override int SelectionStart => _box.SelectionStart;
    public override int SelectionLength => _box.SelectionLength;

    protected override AccessibilityStates ControlStates =>
        (_box.IsReadOnly ? AccessibilityStates.ReadOnly : 0) | AccessibilityStates.Multiline;

    public override bool SetValue(string value)
    {
        if (_box.IsReadOnly || !Owner.IsEffectivelyEnabled) return false;
        _box.Text = value;
        return true;
    }
}
