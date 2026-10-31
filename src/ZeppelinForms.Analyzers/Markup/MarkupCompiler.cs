using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ZeppelinForms.Analyzers.Markup;

/// <summary>One markup file into the C# of its class.</summary>
/// <remarks>
/// <para>
/// The file needs no namespace declarations: <see cref="MarkupDocument"/> reads it as if
/// the root declared the framework's namespace and <c>x:</c>. The class is named by
/// <c>x:Class</c> or a plain <c>Class</c> on the root, a field by <c>x:Name</c> or a plain
/// <c>Name</c> — as WPF and Avalonia take Name.
/// </para>
/// <para>
/// Elements of the default namespace are the framework's types, found by their short
/// names; <c>xmlns:my="using:App.Controls"</c> names a namespace of the application's.
/// An attribute is a property — set from its text by <see cref="MarkupValues"/> — or
/// an event, given the name of a method of the class. <c>Owner.Property</c> reads as
/// <c>Property</c>: <c>Grid.Row="1"</c> is the element's own Row.
/// </para>
/// <para>
/// Children go to the first property that can take them: one marked [Content], then
/// Children, Items, Nodes, Content and Child — a collection whose items are of the
/// child's type, or a property of it. A SplitView so takes SplitViewItems into Items
/// and an element into Content. <c>&lt;Type.Property&gt;</c> names the property itself.
/// </para>
/// <para>
/// <c>{Bind Name.Property}</c> binds a styled property to a property of a named element,
/// <c>{Bind Name.Property, Mode=TwoWay}</c> both ways; <c>{x:Null}</c> is null. Under
/// <c>x:DataType="vm:OrderViewModel"</c> any other path reads from the data context —
/// <c>{Bind Customer.Name}</c> — each step a property read compiled here; a property that
/// isn't styled, a Command, is kept set one way.
/// </para>
/// <para>
/// A property marked [SetAfterContent] — a selected index — is set after the children,
/// wherever its attribute is written.
/// </para>
/// <para>
/// A property of a command type takes the name of a command: <c>Command="Save"</c> is a
/// property or a field of the class, <c>Command="AppCommands.Save"</c> a static one of
/// another type — found in the class's namespace, the <c>using:</c> namespaces of the
/// markup, or by its full name.
/// </para>
/// <para>
/// <c>&lt;Type.Styles&gt;</c> holds styles as XAML writes them — <c>&lt;Style
/// Selector="…"&gt;</c> with <c>&lt;Setter&gt;</c>, <c>&lt;Transition&gt;</c> and nested
/// styles, <c>^</c> for the enclosing selector — and <c>&lt;StyleInclude
/// Source="…"/&gt;</c> for a sheet of the assets folder. The styles become the text of a
/// style sheet, line for line where the markup has them (<see cref="MarkupStyleText"/>):
/// checked here (<see cref="InlineStyleChecker"/>), parsed when the element is built.
/// </para>
/// </remarks>
internal sealed class MarkupCompiler
{
    public const string DefaultNamespace = "https://zeppelinforms.dev/markup";
    public const string XNamespace = "https://zeppelinforms.dev/markup/x";

    private const string ElementMetadataName = "ZeppelinForms.Forms.Controls.Base.UIElement";
    private const string StyledPropertyMetadataName = "ZeppelinForms.Forms.Styling.StyledProperty`1";
    private const string ContentAttributeMetadataName = "ZeppelinForms.Forms.Markup.ContentAttribute";
    private const string AfterContentAttributeMetadataName = "ZeppelinForms.Forms.Markup.SetAfterContentAttribute";
    private const string CommandMetadataName = "System.Windows.Input.ICommand";
    private const string StylesMetadataName = "ZeppelinForms.Forms.Styling.Styles";
    private const string StyleSheetType = "global::ZeppelinForms.Forms.Styling.StyleSheet";
    private const string StyleSheetLinkType = "global::ZeppelinForms.Forms.Styling.StyleSheetLink";

    /// <summary>Where children go when no property is marked [Content], in this order.</summary>
    private static readonly string[] ContentNames = ["Children", "Items", "Nodes", "Content", "Child"];

    private static readonly SymbolDisplayFormat TypeFormat = MarkupValues.TypeFormat;

    /// <summary>The type as declared, with its <c>?</c>: a generic argument the generated
    /// lambdas return and take as they are.</summary>
    private static readonly SymbolDisplayFormat AnnotatedTypeFormat =
        TypeFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly string _path;
    private readonly string _text;
    private readonly Compilation _compilation;
    private readonly IReadOnlyList<string> _styleSheets;
    private readonly List<Diagnostic> _diagnostics = [];

    private readonly StringBuilder _body = new();
    private readonly List<(string Name, INamedTypeSymbol Type, string Modifier)> _fields = [];
    private readonly Dictionary<string, INamedTypeSymbol> _names = new(StringComparer.Ordinal);
    private readonly List<PendingBinding> _bindings = [];

    /// <summary>The links of the style sheets the markup adds: a field each, so that a
    /// build again — Hot Reload — removes the old sheet before it adds the new.</summary>
    private readonly List<string> _styleLinks = [];

    /// <summary>The framework's types by short name: null for a name two of them share.</summary>
    private readonly Dictionary<string, INamedTypeSymbol?> _frameworkTypes = new(StringComparer.Ordinal);

    private INamedTypeSymbol? _class;
    private string _namespace = string.Empty;
    private XElement? _root;
    private XAttribute? _classAttribute;
    private INamedTypeSymbol? _element;
    private StyleNames? _styleNames;
    private int _counter;

    /// <summary>The data types of the elements being built, innermost on top: what
    /// <c>x:DataType</c> says their data context is. Null — none said.</summary>
    private readonly Stack<INamedTypeSymbol?> _dataTypes = new();

    private sealed class PendingBinding(
        string target, IPropertySymbol property, INamedTypeSymbol targetType,
        string path, INamedTypeSymbol? dataType, bool twoWay, IXmlLineInfo line)
    {
        public string Target { get; } = target;
        public IPropertySymbol Property { get; } = property;
        public INamedTypeSymbol TargetType { get; } = targetType;

        /// <summary>As written: "NameBox.Text", "Customer.Name".</summary>
        public string Path { get; } = path;

        /// <summary>The data context's type where the binding is written, if the markup said it.</summary>
        public INamedTypeSymbol? DataType { get; } = dataType;

        public bool TwoWay { get; } = twoWay;
        public IXmlLineInfo Line { get; } = line;
    }

    private MarkupCompiler(string path, string text, Compilation compilation, IReadOnlyList<string> styleSheets)
    {
        _path = path;
        _text = text;
        _compilation = compilation;
        _styleSheets = styleSheets;
    }

    /// <param name="styleSheets">The project's <c>.zss</c> files, for
    /// <c>StyleSheet="…"</c> to check that its sheets are there.</param>
    public static MarkupResult Compile(string path, string text, Compilation compilation, IReadOnlyList<string> styleSheets)
    {
        var compiler = new MarkupCompiler(path, text, compilation, styleSheets);
        string? source = compiler.Run();

        string hint = Path.GetFileName(path) + ".g.cs";

        // a source with errors would only add the compiler's to ours
        bool failed = compiler._diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        return new MarkupResult(hint, failed ? null : source, compiler._diagnostics);
    }

    // ===== the file =====

    private string? Run()
    {
        XDocument document;

        try
        {
            // the namespaces are declared for the file: <Form Class="…"> is a whole root
            document = MarkupDocument.Parse(_text);
        }
        catch (XmlException e)
        {
            Report(MarkupDiagnostics.Unreadable, e.LineNumber, e.LinePosition, e.Message);
            return null;
        }

        XElement root = document.Root!;

        if (_compilation.GetTypeByMetadataName(ElementMetadataName) is not { } element)
            return null;

        _element = element;
        _root = root;

        CollectFrameworkTypes(element.ContainingAssembly);

        INamedTypeSymbol? rootType = ResolveType(root.Name, root);
        if (rootType is null) return null;

        string? className = ClassName(root, rootType);

        if (string.IsNullOrWhiteSpace(className))
        {
            Report(MarkupDiagnostics.NoClass, root);
            return null;
        }

        int dot = className!.LastIndexOf('.');
        string ns = dot < 0 ? string.Empty : className.Substring(0, dot);
        string name = className.Substring(dot + 1);

        _namespace = ns;

        _class = _compilation.GetTypeByMetadataName(className);

        // the root's members are the class's own
        Line(root);
        PushDataType(root);
        ApplyAttributes(root, "this", rootType, isRoot: true, afterContent: false);
        ApplyChildren(root, "this", rootType, isRoot: true);
        ApplyAttributes(root, "this", rootType, isRoot: true, afterContent: true);
        _dataTypes.Pop();
        EmitBindings();

        return Emit(ns, name, rootType);
    }

    /// <summary>The class the markup builds: <c>x:Class="App.MainForm"</c>, as XAML names
    /// it, or a plain <c>Class="App.MainForm"</c> — unless the root's type has a property
    /// of that name, which the plain attribute then sets.</summary>
    private string? ClassName(XElement root, INamedTypeSymbol rootType)
    {
        if ((string?)root.Attribute(XName.Get("Class", XNamespace)) is { } written)
            return written.Trim();

        if (root.Attribute("Class") is { } plain && FindProperty(rootType, "Class") is null && FindEvent(rootType, "Class") is null)
        {
            _classAttribute = plain;
            return plain.Value.Trim();
        }

        return null;
    }

    private string Emit(string ns, string name, INamedTypeSymbol rootType)
    {
        var source = new StringBuilder();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine("#pragma warning disable");
        source.AppendLine();

        if (ns.Length > 0)
        {
            source.Append("namespace ").AppendLine(ns);
            source.AppendLine("{");
        }

        // a class the markup alone declares is public, as XAML's are; one with code of its
        // own has its accessibility there, and a partial part may leave it out
        source.Append(_class is null ? "    public partial class " : "    partial class ").Append(name).Append(" : ").AppendLine(rootType.ToDisplayString(TypeFormat));
        source.AppendLine("    {");

        foreach (var (field, type, modifier) in _fields)
            source.Append("        ").Append(modifier).Append(' ').Append(type.ToDisplayString(TypeFormat))
                .Append(' ').Append(field).AppendLine(" = default!;");

        foreach (string link in _styleLinks)
            source.Append("        private ").Append(StyleSheetLinkType).Append("? ").Append(link).AppendLine(";");

        if (_fields.Count > 0 || _styleLinks.Count > 0) source.AppendLine();

        // a class the markup alone declares, or one whose code declares no constructor,
        // gets one that builds the markup
        if (_class is null || _class.InstanceConstructors.All(c => c.IsImplicitlyDeclared))
        {
            source.Append("        public ").Append(name).AppendLine("()");
            source.AppendLine("        {");
            source.AppendLine("            InitializeComponent();");
            source.AppendLine("        }");
            source.AppendLine();
        }

        source.Append("        /// <summary>Builds what ").Append(Escape(Path.GetFileName(_path)))
            .AppendLine(" describes. Called again, it builds it anew: the root's children are replaced.</summary>");
        source.AppendLine("        [global::System.CodeDom.Compiler.GeneratedCode(\"ZeppelinForms.Analyzers\", null)]");
        source.AppendLine("        private void InitializeComponent()");
        source.AppendLine("        {");
        source.Append(_body);
        source.AppendLine("#line default");
        source.AppendLine("        }");

        // Hot Reload: an edit of the markup changes InitializeComponent; the class runs
        // it again unless its own code reacts to edits itself
        if (InheritsFrom(rootType, ElementMetadataName) || InheritsFrom(rootType, "ZeppelinForms.Forms.Form"))
        {
            bool ownHook = _class?.GetMembers("OnHotReload").Any(m => !m.IsImplicitlyDeclared) == true;

            if (!ownHook)
            {
                source.AppendLine();
                source.AppendLine("        [global::System.CodeDom.Compiler.GeneratedCode(\"ZeppelinForms.Analyzers\", null)]");
                source.AppendLine("        protected override void OnHotReload(global::ZeppelinForms.Forms.HotReloadEventArgs e)");
                source.AppendLine("        {");
                source.AppendLine("            base.OnHotReload(e);");
                source.Append("            if (e.Changed(typeof(").Append(name).AppendLine("))) InitializeComponent();");
                source.AppendLine("        }");
            }
        }

        source.AppendLine("    }");

        if (ns.Length > 0) source.AppendLine("}");

        return source.ToString();
    }

    // ===== elements =====

    /// <summary>Create an element and everything in it; returns its variable.</summary>
    private string? CreateElement(XElement node)
    {
        if (node.Name.NamespaceName == XNamespace && node.Name.LocalName == "String")
            return SymbolDisplay.FormatLiteral(node.Value, quote: true);

        INamedTypeSymbol? type = ResolveType(node.Name, node);
        if (type is null) return null;

        if (type.IsAbstract || !type.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
        {
            Report(MarkupDiagnostics.NotCreatable, node, type.Name);
            return null;
        }

        string variable = "e" + ++_counter;

        Line(node);
        _body.Append("            var ").Append(variable).Append(" = new ").Append(type.ToDisplayString(TypeFormat)).AppendLine("();");

        if (FieldName(node, type) is { } name)
            DeclareName(node, name, variable, type);

        PushDataType(node);
        ApplyAttributes(node, variable, type, isRoot: false, afterContent: false);
        ApplyChildren(node, variable, type, isRoot: false);
        ApplyAttributes(node, variable, type, isRoot: false, afterContent: true);
        _dataTypes.Pop();

        return variable;
    }

    /// <summary>The field an element is kept in: <c>x:Name</c>, or — as WPF and Avalonia
    /// take it — a plain <c>Name</c> of an element whose type has one and that can name a
    /// field. A name that can't, <c>Name="main-panel"</c>, is only the element's own, for
    /// <c>#main-panel</c> in a style sheet.</summary>
    private static string? FieldName(XElement node, INamedTypeSymbol type)
    {
        if ((string?)node.Attribute(XName.Get("Name", XNamespace)) is { } written)
            return written;

        if ((string?)node.Attribute("Name") is { } plain &&
            SyntaxFacts.IsValidIdentifier(plain) &&
            FindProperty(type, "Name") is { Type.SpecialType: SpecialType.System_String })
        {
            return plain;
        }

        return null;
    }

    private void DeclareName(XElement node, string name, string variable, INamedTypeSymbol type)
    {
        if (_names.ContainsKey(name))
        {
            Report(MarkupDiagnostics.DuplicateName, node, name);
            return;
        }

        _names[name] = type;

        string modifier = (string?)node.Attribute(XName.Get("FieldModifier", XNamespace)) ?? "private";

        _fields.Add((name, type, modifier.Trim().ToLowerInvariant()));
        _body.Append("            this.").Append(name).Append(" = ").Append(variable).AppendLine(";");

        // an element's own name too, for #name in a style sheet — unless set explicitly
        if (FindProperty(type, "Name") is { SetMethod: not null, Type.SpecialType: SpecialType.System_String } &&
            node.Attribute("Name") is null)
        {
            _body.Append("            ").Append(variable).Append(".Name = ")
                .Append(SymbolDisplay.FormatLiteral(name, quote: true)).AppendLine(";");
        }
    }

    // ===== attributes =====

    /// <param name="afterContent">The second pass, after the children: the properties
    /// marked [SetAfterContent] — a selected index means nothing before the items it
    /// selects among. The first pass does everything else.</param>
    private void ApplyAttributes(XElement node, string target, INamedTypeSymbol type, bool isRoot, bool afterContent)
    {
        foreach (XAttribute attribute in node.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            if (attribute.Name.NamespaceName == XNamespace) continue;
            if (isRoot && ReferenceEquals(attribute, _classAttribute)) continue;

            // Grid.Row="1": the element's own Row
            string memberName = attribute.Name.LocalName;
            int dot = memberName.LastIndexOf('.');
            if (dot >= 0) memberName = memberName.Substring(dot + 1);

            IEventSymbol? @event = FindEvent(type, memberName);
            IPropertySymbol? property = @event is null ? FindProperty(type, memberName) : null;

            // each attribute in one pass: what isn't a property marked to wait — events,
            // mistakes — in the first
            if (afterContent != (property is not null && SetsAfterContent(property))) continue;

            Line(attribute);

            if (@event is not null)
            {
                ApplyEvent(attribute, target, type, @event, isRoot);
                continue;
            }

            if (property is null)
            {
                Report(MarkupDiagnostics.UnknownMember, attribute, type.Name, memberName);
                continue;
            }

            string value = attribute.Value;

            if (value.StartsWith("{", StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal))
            {
                ApplyExtension(attribute, target, type, property, value.Substring(1, value.Length - 2).Trim());
                continue;
            }

            // a collection to be filled, not set: Classes="primary large"
            if (property.SetMethod is null && CollectionItemType(property.Type) is { SpecialType: SpecialType.System_String })
            {
                foreach (string item in value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    _body.Append("            ").Append(target).Append('.').Append(property.Name).Append(".Add(")
                        .Append(SymbolDisplay.FormatLiteral(item, quote: true)).AppendLine(");");
                }

                continue;
            }

            if (!IsSettable(property))
            {
                Report(MarkupDiagnostics.UnknownMember, attribute, type.Name, memberName);
                continue;
            }

            // Command="Save": a command of the class, not a value written in the markup
            if (IsCommand(property.Type))
            {
                ApplyCommand(attribute, target, property, value);
                continue;
            }

            string? expression = MarkupValues.Write(value, property.Type, _compilation, out string? error);

            if (expression is null)
            {
                Report(MarkupDiagnostics.BadValue, attribute, value, Describe(property.Type), error ?? string.Empty);
                continue;
            }

            Assign(target, property, expression);
        }
    }

    private void ApplyEvent(XAttribute attribute, string target, INamedTypeSymbol type, IEventSymbol @event, bool isRoot)
    {
        string handler = attribute.Value.Trim();

        bool exists = _class is not null && Members(_class).OfType<IMethodSymbol>().Any(m => m.Name == handler);

        if (!SyntaxFacts.IsValidIdentifier(handler) || !exists)
        {
            Report(MarkupDiagnostics.NoHandler, attribute, _class?.Name ?? "The class", handler, @event.Name);
            return;
        }

        // the root lives on: built again by Hot Reload, it must not get the handler twice
        if (isRoot)
            _body.Append("            ").Append(target).Append('.').Append(@event.Name).Append(" -= this.").Append(handler).AppendLine(";");

        _body.Append("            ").Append(target).Append('.').Append(@event.Name).Append(" += this.").Append(handler).AppendLine(";");
    }

    // ===== commands =====

    private bool IsCommand(ITypeSymbol type)
    {
        INamedTypeSymbol? command = _compilation.GetTypeByMetadataName(CommandMetadataName);

        return command is not null &&
            (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, command) ||
             type.AllInterfaces.Contains(command, SymbolEqualityComparer.Default));
    }

    /// <summary>Command="Save" — a property or a field of the class; Command="Type.Save" —
    /// a static one of another type.</summary>
    private void ApplyCommand(XAttribute attribute, string target, IPropertySymbol property, string value)
    {
        string written = value.Trim();
        string? expression = CommandExpression(written, property.Type, out string reason);

        if (expression is null)
        {
            Report(MarkupDiagnostics.NoCommand, attribute, written, reason);
            return;
        }

        Assign(target, property, expression);
    }

    private string? CommandExpression(string written, ITypeSymbol commandType, out string reason)
    {
        reason = string.Empty;

        string[] parts = written.Split('.');

        if (parts.Any(p => !SyntaxFacts.IsValidIdentifier(p)))
        {
            reason = "a command is named as Member of the class or Type.Member";
            return null;
        }

        if (parts.Length == 1)
        {
            if (_class is null)
            {
                reason = "the class has no code of its own to declare it in";
                return null;
            }

            ISymbol? member = CommandMember(_class, written, commandType, staticOnly: false, out reason);

            if (member is null) return null;

            return member.IsStatic
                ? _class.ToDisplayString(TypeFormat) + "." + member.Name
                : "this." + member.Name;
        }

        string typeName = string.Join(".", parts, 0, parts.Length - 1);
        string memberName = parts[parts.Length - 1];

        INamedTypeSymbol? owner = FindNamedType(typeName);

        if (owner is null)
        {
            reason = "there is no type '" + typeName + "' in the class's namespace, the markup's using: namespaces or by that full name";
            return null;
        }

        ISymbol? found = CommandMember(owner, memberName, commandType, staticOnly: true, out reason);

        return found is null ? null : owner.ToDisplayString(TypeFormat) + "." + found.Name;
    }

    /// <summary>A property or a field of the type, its bases included, whose value is a
    /// command the property takes.</summary>
    private ISymbol? CommandMember(INamedTypeSymbol type, string name, ITypeSymbol commandType, bool staticOnly, out string reason)
    {
        reason = string.Empty;
        bool own = SymbolEqualityComparer.Default.Equals(type, _class);

        foreach (ISymbol member in Members(type))
        {
            if (member.Name != name) continue;

            ITypeSymbol? valueType = member switch
            {
                IPropertySymbol { IsIndexer: false, GetMethod: not null } p => p.Type,
                IFieldSymbol f => f.Type,
                _ => null,
            };

            if (valueType is null) continue;

            if (staticOnly && !member.IsStatic)
            {
                reason = "'" + type.Name + "." + name + "' is not static";
                return null;
            }

            // the class's own members are reached from inside it; another type's must be visible
            if (!own && member.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                reason = "'" + type.Name + "." + name + "' is not public or internal";
                return null;
            }

            if (!Converts(valueType, commandType))
            {
                reason = "'" + name + "' is " + Describe(valueType) + ", not " + Describe(commandType);
                return null;
            }

            return member;
        }

        reason = "'" + type.Name + "' has no property or field '" + name + "'";
        return null;
    }

    /// <summary>A type by a name written in the markup: in the class's namespace, in one
    /// of the markup's using: namespaces, or by its full name.</summary>
    private INamedTypeSymbol? FindNamedType(string name)
    {
        var namespaces = new List<string>();

        if (_namespace.Length > 0) namespaces.Add(_namespace);

        if (_root is not null)
        {
            foreach (XAttribute declaration in _root.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                string value = declaration.Value;

                if (value.StartsWith("using:", StringComparison.Ordinal))
                    namespaces.Add(value.Substring(6));
                else if (value.StartsWith("clr-namespace:", StringComparison.Ordinal))
                    namespaces.Add(value.Substring(14).Split(';')[0]);
            }
        }

        foreach (string ns in namespaces)
        {
            if (_compilation.GetTypeByMetadataName(ns + "." + name) is { } inNamespace)
                return inNamespace;
        }

        if (_compilation.GetTypeByMetadataName(name) is { } full)
            return full;

        return name.IndexOf('.') < 0 && _frameworkTypes.TryGetValue(name, out INamedTypeSymbol? framework) ? framework : null;
    }

    // ===== style sheets =====

    /// <summary>A new field for the link of a sheet; the code to remove the sheet it held.</summary>
    private string NewStyleLink()
    {
        string field = "__zfStyleSheet" + (_styleLinks.Count + 1);
        _styleLinks.Add(field);

        _body.Append("            this.").Append(field).AppendLine("?.Dispose();");

        return field;
    }

    /// <summary>&lt;Type.Styles&gt;: its &lt;Style&gt;s and &lt;StyleInclude&gt;s in their
    /// order — the order of the cascade. The styles between two includes go as one sheet.</summary>
    private void ApplyStyles(XElement node, string target)
    {
        var styles = new List<XElement>();

        foreach (XNode child in node.Nodes())
        {
            switch (child)
            {
                case XElement element when MarkupStyleText.IsElement(element, "Style"):
                    styles.Add(element);
                    break;

                case XElement element when MarkupStyleText.IsElement(element, "StyleInclude"):
                    EmitStyles(styles, target);
                    styles.Clear();
                    ApplyStyleInclude(element, target);
                    break;

                case XElement element:
                    Report(MarkupDiagnostics.NoPlace, element, node.Name.LocalName, element.Name.LocalName,
                        "it holds <Style> and <StyleInclude>");
                    break;

                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    Report(MarkupDiagnostics.NoPlace, text, node.Name.LocalName, "text", "it holds <Style> and <StyleInclude>");
                    break;
            }
        }

        EmitStyles(styles, target);
    }

    /// <summary>&lt;StyleInclude Source="Styles/app.zss"/&gt;: a sheet of the assets folder,
    /// as <c>StyleSheet.LoadAsset</c> finds it. One that isn't among the project's .zss
    /// files is a warning — it may come into the assets another way.</summary>
    private void ApplyStyleInclude(XElement element, string target)
    {
        string? source = ((string?)element.Attribute("Source"))?.Trim();

        if (string.IsNullOrEmpty(source))
        {
            Report(MarkupDiagnostics.BadStyles, element, "A StyleInclude names its sheet: Source=\"Styles/app.zss\"");
            return;
        }

        if (!IsProjectStyleSheet(source!))
            Report(MarkupDiagnostics.NoStyleSheet, element, source!);

        Line(element);

        string field = NewStyleLink();

        // watched while a debugger is attached: a saved sheet restyles the running form
        _body.Append("            this.").Append(field).Append(" = ").Append(target).Append(".Styles.Load(")
            .Append("global::System.IO.Path.Combine(global::ZeppelinForms.Assets.Root, ")
            .Append(SymbolDisplay.FormatLiteral(source!, quote: true))
            .AppendLine("), watch: global::System.Diagnostics.Debugger.IsAttached);");
    }

    /// <summary>The project has the sheet: one of its .zss files ends with the path —
    /// under an Assets folder or not.</summary>
    private bool IsProjectStyleSheet(string path)
    {
        string wanted = "/" + path.Replace('\\', '/').TrimStart('/');

        return _styleSheets.Any(file =>
            file.Replace('\\', '/').EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Styles as one style sheet: its text is written line for line where the
    /// markup has them (<see cref="MarkupStyleText"/>), so what the check here and the
    /// sheet at run time report has the markup's lines.</summary>
    private void EmitStyles(List<XElement> styles, string target)
    {
        if (styles.Count == 0) return;

        string text = MarkupStyleText.Write(styles, issue =>
        {
            if (issue.Misplaced)
                Report(MarkupDiagnostics.NoPlace, issue.At, issue.Holder, issue.Child, issue.Message);
            else
                Report(MarkupDiagnostics.BadStyles, issue.At, issue.Message);
        });

        if (text.Length == 0) return;

        _styleNames ??= StyleNames.Collect(_compilation, _element!);

        foreach (StyleProblem problem in InlineStyleChecker.Check(text, _styleNames))
        {
            (int line, int column) = Position(text, problem.Offset, 1, 1);

            Report(problem.IsError ? MarkupDiagnostics.BadStyles : MarkupDiagnostics.StyleWarning,
                line, column, problem.Message);
        }

        Line(styles[0]);

        string field = NewStyleLink();

        // the empty lines before the first style in the code as a count, not as text
        int blank = 0;
        while (blank < text.Length && text[blank] == '\n') blank++;

        _body.Append("            this.").Append(field).Append(" = ").Append(target).Append(".Styles.Add(")
            .Append(StyleSheetType).Append(".Parse(new string('\\n', ").Append(blank).Append(") + ")
            .Append(SymbolDisplay.FormatLiteral(text.Substring(blank), quote: true)).Append(", ")
            .Append(SymbolDisplay.FormatLiteral(Path.GetFileName(_path), quote: true))
            .AppendLine(", global::ZeppelinForms.Assets.Root));");
    }

    /// <summary>The line and column of an offset in a text that starts at the given place.</summary>
    private static (int Line, int Column) Position(string text, int offset, int line, int column)
    {
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>{x:Null}, {Bind Name.Property[, Mode=TwoWay]}.</summary>
    private void ApplyExtension(XAttribute attribute, string target, INamedTypeSymbol type, IPropertySymbol property, string body)
    {
        if (body == "x:Null")
        {
            if (property.Type.IsValueType && property.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
            {
                Report(MarkupDiagnostics.BadValue, attribute, "{x:Null}", Describe(property.Type), "it can't be null");
                return;
            }

            Assign(target, property, "null");
            return;
        }

        if (!body.StartsWith("Bind ", StringComparison.Ordinal))
        {
            Report(MarkupDiagnostics.BadValue, attribute, "{" + body + "}", Describe(property.Type),
                "markup knows {Bind Path} and {x:Null}");
            return;
        }

        string[] parts = body.Substring(5).Split(',');
        string path = parts[0].Trim();
        bool twoWay = false;

        for (int i = 1; i < parts.Length; i++)
        {
            string option = parts[i].Replace(" ", string.Empty);

            if (option.Equals("Mode=TwoWay", StringComparison.OrdinalIgnoreCase)) twoWay = true;
            else if (!option.Equals("Mode=OneWay", StringComparison.OrdinalIgnoreCase))
            {
                Report(MarkupDiagnostics.BadBinding, attribute, "'" + parts[i].Trim() + "' is not Mode=OneWay or Mode=TwoWay");
                return;
            }
        }

        if (path.Length == 0 || path.Split('.').Any(segment => !SyntaxFacts.IsValidIdentifier(segment)))
        {
            Report(MarkupDiagnostics.BadBinding, attribute,
                "A binding names a path of properties: {Bind Customer.Name}, or {Bind NameBox.Text} of a named element — not '" + path + "'");
            return;
        }

        // checked once every element has been created: a named source may come later
        _bindings.Add(new PendingBinding(target, property, type, path, _dataTypes.Peek(), twoWay, attribute));
    }

    /// <summary>The bindings, once every element is made. A path whose first name is a
    /// named element starts from it — {Bind NameBox.Text}; any other from the data
    /// context, whose type x:DataType gave — {Bind Customer.Name}. Every step is a
    /// property read compiled here: no reflection at run time.</summary>
    private void EmitBindings()
    {
        foreach (PendingBinding binding in _bindings)
        {
            IXmlLineInfo line = binding.Line;
            string[] segments = binding.Path.Split('.');

            string? source;
            INamedTypeSymbol sourceType;
            string[] steps;

            if (segments.Length > 1 && _names.TryGetValue(segments[0], out INamedTypeSymbol? named))
            {
                source = "this." + segments[0];
                sourceType = named;
                steps = segments.Skip(1).ToArray();
            }
            else if (binding.DataType is { } dataType)
            {
                source = null;
                sourceType = dataType;
                steps = segments;
            }
            else
            {
                Report(MarkupDiagnostics.BadBinding, line, segments.Length > 1
                    ? "No element is named '" + segments[0] + "', and no x:DataType says what the data context is"
                    : "'" + binding.Path + "' is read from the data context: say what it is with x:DataType=\"…\" here or on an element above");
                continue;
            }

            if (!InheritsFrom(binding.TargetType, ElementMetadataName))
            {
                Report(MarkupDiagnostics.BadBinding, line,
                    "'" + binding.TargetType.Name + "' is not an element, and only elements bind: bind the elements inside it");
                continue;
            }

            // the styled property's descriptor: TextProperty for Text
            IFieldSymbol? descriptor = FindStyledDescriptor(binding.TargetType, binding.Property.Name);

            if (descriptor is null && binding.TwoWay)
            {
                Report(MarkupDiagnostics.BadBinding, line,
                    "'" + binding.Property.Name + "' of " + binding.TargetType.Name + " is not a styled property: it binds one way only");
                continue;
            }

            string? path = PathExpression(binding, sourceType, steps, out IPropertySymbol? last);
            if (path is null || last is null) continue;

            Line(line);

            if (descriptor is not null)
            {
                _body.Append("            ").Append(binding.Target).Append(".Bind(")
                    .Append(descriptor.ContainingType.ToDisplayString(TypeFormat)).Append('.').Append(descriptor.Name).Append(", ");

                if (source is not null) _body.Append(source).Append(", ");

                _body.Append(path).Append(", global::ZeppelinForms.Data.BindingMode.")
                    .Append(binding.TwoWay ? "TwoWay" : "OneWay").AppendLine(");");

                continue;
            }

            EmitValueBinding(binding, source, path, last);
        }
    }

    /// <summary>A property that isn't styled — a Command — bound one way: kept set to the
    /// path's value, which must fit it as it is.</summary>
    private void EmitValueBinding(PendingBinding binding, string? source, string path, IPropertySymbol last)
    {
        IPropertySymbol property = binding.Property;

        if (property.Name == "DataContext")
        {
            Report(MarkupDiagnostics.BadBinding, binding.Line,
                "The data context is given, not bound: set it from code, or make it here as <" + binding.TargetType.Name + ".DataContext>");
            return;
        }

        if (!IsSettable(property))
        {
            Report(MarkupDiagnostics.BadBinding, binding.Line,
                "'" + property.Name + "' of " + binding.TargetType.Name + " can't be set");
            return;
        }

        if (!Converts(last.Type, property.Type))
        {
            Report(MarkupDiagnostics.BadBinding, binding.Line,
                "'" + binding.Path + "' is " + Describe(last.Type) + ", which " + property.Name + " (" + Describe(property.Type) + ") doesn't take");
            return;
        }

        _body.Append("            ").Append(binding.Target).Append(".BindValue(")
            .Append(SymbolDisplay.FormatLiteral(property.Name, quote: true)).Append(", ");

        if (source is not null) _body.Append(source).Append(", ");

        _body.Append(path).Append(", static (e, v) => ((").Append(TypeName(binding.TargetType)).Append(")e).")
            .Append(property.Name).Append(" = ").Append(Unbox("v", property.Type)).AppendLine(");");
    }

    /// <summary>new BindingPath("Customer.Name", typeof(Vm), typeof(string), new BindingStep("Customer", …), …)
    /// with the last property as its own type — Value, read and for a path both ways
    /// written without a box — and Set for a path both ways. Null — a step that isn't
    /// there, reported.</summary>
    private string? PathExpression(PendingBinding binding, INamedTypeSymbol sourceType, string[] steps, out IPropertySymbol? last)
    {
        last = null;

        var code = new StringBuilder();
        ITypeSymbol current = sourceType;

        code.Append("new global::ZeppelinForms.Data.BindingPath(")
            .Append(SymbolDisplay.FormatLiteral(binding.Path, quote: true))
            .Append(", typeof(").Append(TypeName(sourceType)).Append("), typeof(");

        var reads = new StringBuilder();

        foreach (string step in steps)
        {
            if (current is not INamedTypeSymbol owner || PathProperty(owner, step) is not { } property)
            {
                Report(MarkupDiagnostics.BadBinding, binding.Line,
                    "'" + Describe(current) + "' has no property '" + step + "' the markup can read");
                return null;
            }

            reads.Append(", new global::ZeppelinForms.Data.BindingStep(")
                .Append(SymbolDisplay.FormatLiteral(step, quote: true))
                .Append(", static s => ((").Append(TypeName(owner)).Append(")s).").Append(property.Name).Append(')');

            last = property;
            current = property.Type;
        }

        code.Append(TypeName(last!.Type)).Append(')').Append(reads).Append(')');

        if (binding.TwoWay && !IsWritable(last))
        {
            Report(MarkupDiagnostics.BadBinding, binding.Line,
                "'" + binding.Path + "' can't be written, and Mode=TwoWay writes it: its last property needs a setter the markup can call");
            return null;
        }

        string lastOwner = TypeName(last.ContainingType);

        // the last property as its type: a value type bound to a property of that type
        // moves without a box. The type as declared, nullability included — the read
        // returns it as it is
        code.Append(" { Value = new global::ZeppelinForms.Data.BindingValue<")
            .Append(last.Type.ToDisplayString(AnnotatedTypeFormat))
            .Append(">(static s => ((").Append(lastOwner).Append(")s).").Append(last.Name);

        if (binding.TwoWay)
            code.Append(", static (o, v) => ((").Append(lastOwner).Append(")o).").Append(last.Name).Append(" = v");

        code.Append(')');

        if (binding.TwoWay)
        {
            code.Append(", Set = static (o, v) => ((").Append(lastOwner).Append(")o).")
                .Append(last.Name).Append(" = ").Append(Unbox("v", last.Type));
        }

        code.Append(" }");

        return code.ToString();
    }

    /// <summary>A property a path can read: an instance one with a getter the class can
    /// call — public, or internal of the same assembly, or the class's own.</summary>
    private IPropertySymbol? PathProperty(INamedTypeSymbol type, string name)
    {
        IEnumerable<ISymbol> members = type.TypeKind == TypeKind.Interface
            ? new[] { type }.Concat(type.AllInterfaces).SelectMany(i => i.GetMembers())
            : Members(type);

        return members.OfType<IPropertySymbol>().FirstOrDefault(p =>
            p.Name == name && !p.IsStatic && !p.IsIndexer &&
            p.GetMethod is { } getter && IsAccessible(getter));
    }

    private bool IsWritable(IPropertySymbol property) =>
        property.SetMethod is { IsInitOnly: false } setter && IsAccessible(setter);

    private bool IsAccessible(ISymbol symbol) =>
        _compilation.IsSymbolAccessibleWithin(symbol, (ISymbol?)_class ?? _compilation.Assembly);

    /// <summary>A type for typeof and a cast: fully qualified, without the nullable
    /// annotation of a reference type, which neither takes.</summary>
    private static string TypeName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);

    /// <summary>An object as a value of the type: a null of a value type is its default,
    /// not a NullReferenceException in the middle of a change.</summary>
    private static string Unbox(string value, ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? value + " is " + TypeName(type) + " __value ? __value : default"
            : "(" + type.ToDisplayString(TypeFormat) + ")" + value;

    // ===== data types =====

    /// <summary>x:DataType="vm:OrderViewModel" of an element, for it and what is inside it;
    /// without one, the data type of the element around it.</summary>
    private void PushDataType(XElement node)
    {
        INamedTypeSymbol? outer = _dataTypes.Count > 0 ? _dataTypes.Peek() : null;

        if (node.Attribute(XName.Get("DataType", XNamespace)) is not { } attribute)
        {
            _dataTypes.Push(outer);
            return;
        }

        INamedTypeSymbol? type = DataType(node, attribute.Value.Trim());

        if (type is null)
            Report(MarkupDiagnostics.UnknownDataType, attribute, attribute.Value.Trim());

        _dataTypes.Push(type ?? outer);
    }

    /// <summary>"vm:OrderViewModel" — a prefix of a using: namespace; "OrderViewModel" — in
    /// the class's namespace or the markup's using: namespaces; or a full name.</summary>
    private INamedTypeSymbol? DataType(XElement node, string written)
    {
        int colon = written.IndexOf(':');

        if (colon < 0) return FindNamedType(written);

        XNamespace? ns = node.GetNamespaceOfPrefix(written.Substring(0, colon));
        if (ns is null) return null;

        return ResolveType(ns + written.Substring(colon + 1), node, report: false);
    }

    /// <summary>The property's value means something only once the content is there: a
    /// selected index among the items.</summary>
    private static bool SetsAfterContent(IPropertySymbol property)
    {
        for (IPropertySymbol? current = property; current is not null; current = current.OverriddenProperty)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AfterContentAttributeMetadataName))
                return true;
        }

        return false;
    }

    // ===== children =====

    private void ApplyChildren(XElement node, string target, INamedTypeSymbol type, bool isRoot)
    {
        var content = new List<XElement>();

        foreach (XNode child in node.Nodes())
        {
            switch (child)
            {
                case XElement element when IsPropertyElement(element, out string? propertyName):
                    ApplyPropertyElement(element, target, type, propertyName!);
                    break;

                case XElement element:
                    content.Add(element);
                    break;

                case XText text when !string.IsNullOrWhiteSpace(text.Value):
                    Report(MarkupDiagnostics.NoPlace, text, type.Name, "text",
                        "write text in an attribute — Text=\"…\" — or as <x:String>");
                    break;
            }
        }

        if (content.Count == 0) return;

        bool cleared = false;

        foreach (XElement element in content)
        {
            string? variable = CreateElement(element);
            if (variable is null) continue;

            ITypeSymbol childType = element.Name.NamespaceName == XNamespace
                ? _compilation.GetSpecialType(SpecialType.System_String)
                : ResolveType(element.Name, element, report: false)!;

            (IPropertySymbol Property, bool Collection)? place = ContentPlace(type, childType);

            if (place is not { } found)
            {
                Report(MarkupDiagnostics.NoPlace, element, type.Name, childType.Name,
                    "none of its properties takes one — mark one [Content], or name it: <" + type.Name + ".Property>");
                continue;
            }

            Line(element);

            if (found.Collection)
            {
                // built anew by Hot Reload, the root must not keep the old children
                if (isRoot && !cleared)
                {
                    _body.Append("            ").Append(target).Append('.').Append(found.Property.Name).AppendLine(".Clear();");
                    cleared = true;
                }

                _body.Append("            ").Append(target).Append('.').Append(found.Property.Name)
                    .Append(".Add(").Append(variable).AppendLine(");");
            }
            else
            {
                Assign(target, found.Property, variable);
            }
        }
    }

    private bool IsPropertyElement(XElement element, out string? propertyName)
    {
        propertyName = null;

        string local = element.Name.LocalName;
        int dot = local.IndexOf('.');

        if (dot <= 0) return false;

        propertyName = local.Substring(dot + 1);
        return true;
    }

    /// <summary>&lt;Type.Property&gt;: a collection is filled with its children, any
    /// other property takes its one child or its text.</summary>
    private void ApplyPropertyElement(XElement element, string target, INamedTypeSymbol type, string propertyName)
    {
        IPropertySymbol? property = FindProperty(type, propertyName);

        if (property is null)
        {
            Report(MarkupDiagnostics.UnknownMember, element, type.Name, propertyName);
            return;
        }

        // <Form.Styles>: styles as XAML writes them, not elements to create
        if (property.Type.ToDisplayString() == StylesMetadataName)
        {
            Line(element);
            ApplyStyles(element, target);
            return;
        }

        List<XElement> children = element.Elements().ToList();

        if (children.Count == 0)
        {
            string? expression = MarkupValues.Write(element.Value, property.Type, _compilation, out string? error);

            if (expression is null || !IsSettable(property))
            {
                Report(MarkupDiagnostics.BadValue, element, element.Value, Describe(property.Type), error ?? "the property can't be set");
                return;
            }

            Line(element);
            Assign(target, property, expression);
            return;
        }

        bool collection = CollectionItemType(property.Type) is not null;

        if (!collection && children.Count > 1)
        {
            Report(MarkupDiagnostics.NoPlace, element, type.Name + "." + property.Name, "second element", "it holds one");
            return;
        }

        foreach (XElement child in children)
        {
            string? variable = CreateElement(child);
            if (variable is null) continue;

            Line(child);

            if (collection)
                _body.Append("            ").Append(target).Append('.').Append(property.Name).Append(".Add(").Append(variable).AppendLine(");");
            else
                Assign(target, property, variable);
        }
    }

    /// <summary>Where a child of a type goes: a property marked [Content] first, then
    /// the usual names — the first that takes it.</summary>
    private (IPropertySymbol Property, bool Collection)? ContentPlace(INamedTypeSymbol type, ITypeSymbol child)
    {
        IEnumerable<IPropertySymbol> marked = AllProperties(type)
            .Where(p => p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ContentAttributeMetadataName));

        IEnumerable<IPropertySymbol> named = ContentNames
            .Select(n => FindProperty(type, n))
            .Where(p => p is not null)!;

        foreach (IPropertySymbol property in marked.Concat(named))
        {
            if (CollectionItemType(property.Type) is { } item && Converts(child, item))
                return (property, true);

            if (IsSettable(property) && Converts(child, property.Type))
                return (property, false);
        }

        return null;
    }

    private bool Converts(ITypeSymbol from, ITypeSymbol to) =>
        _compilation.ClassifyConversion(from, to) is { IsImplicit: true, IsUserDefined: false };

    // ===== symbols =====

    private void CollectFrameworkTypes(IAssemblySymbol assembly)
    {
        var namespaces = new Stack<INamespaceSymbol>();
        namespaces.Push(assembly.GlobalNamespace);

        while (namespaces.Count > 0)
        {
            INamespaceSymbol current = namespaces.Pop();

            foreach (INamespaceSymbol inner in current.GetNamespaceMembers())
                namespaces.Push(inner);

            foreach (INamedTypeSymbol type in current.GetTypeMembers())
            {
                if (type.DeclaredAccessibility != Accessibility.Public || type.IsGenericType || type.IsStatic) continue;
                if (type.TypeKind != TypeKind.Class) continue;

                // a name two types share is ambiguous, unless one is a control
                if (_frameworkTypes.TryGetValue(type.Name, out INamedTypeSymbol? existing))
                {
                    bool existingIsElement = existing is not null && InheritsFrom(existing, ElementMetadataName);
                    bool thisIsElement = InheritsFrom(type, ElementMetadataName);

                    _frameworkTypes[type.Name] = existingIsElement == thisIsElement ? null
                        : existingIsElement ? existing : type;
                }
                else
                {
                    _frameworkTypes[type.Name] = type;
                }
            }
        }
    }

    private INamedTypeSymbol? ResolveType(XName name, XObject at, bool report = true)
    {
        string ns = name.NamespaceName;
        string local = name.LocalName;

        if (ns == DefaultNamespace)
        {
            if (_frameworkTypes.TryGetValue(local, out INamedTypeSymbol? type) && type is not null)
                return type;

            if (report)
            {
                Report(MarkupDiagnostics.UnknownType, at, local,
                    _frameworkTypes.ContainsKey(local) ? " the framework names only once — it has several" : " in the framework");
            }

            return null;
        }

        string? clr = ns.StartsWith("using:", StringComparison.Ordinal) ? ns.Substring(6)
            : ns.StartsWith("clr-namespace:", StringComparison.Ordinal) ? ns.Substring(14).Split(';')[0]
            : null;

        if (clr is not null && _compilation.GetTypeByMetadataName(clr + "." + local) is { } own)
            return own;

        if (report)
            Report(MarkupDiagnostics.UnknownType, at, local,
                clr is null ? " — its namespace '" + ns + "' is neither the framework's nor using:Namespace" : " in " + clr);

        return null;
    }

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            foreach (ISymbol member in current.GetMembers())
                yield return member;
    }

    private static IEnumerable<IPropertySymbol> AllProperties(INamedTypeSymbol type) =>
        Members(type).OfType<IPropertySymbol>().Where(p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public);

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name) =>
        AllProperties(type).FirstOrDefault(p => p.Name == name);

    private static IEventSymbol? FindEvent(INamedTypeSymbol type, string name) =>
        Members(type).OfType<IEventSymbol>().FirstOrDefault(e => e.Name == name && !e.IsStatic && e.DeclaredAccessibility == Accessibility.Public);

    private IFieldSymbol? FindStyledDescriptor(INamedTypeSymbol type, string property)
    {
        INamedTypeSymbol? styled = _compilation.GetTypeByMetadataName(StyledPropertyMetadataName);

        return Members(type).OfType<IFieldSymbol>().FirstOrDefault(f =>
            f.IsStatic && f.Name == property + "Property" && f.DeclaredAccessibility == Accessibility.Public &&
            f.Type is INamedTypeSymbol t && SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, styled));
    }

    private static bool IsSettable(IPropertySymbol property) =>
        property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false };

    private static ITypeSymbol? CollectionItemType(ITypeSymbol type)
    {
        IEnumerable<INamedTypeSymbol?> candidates = new[] { type as INamedTypeSymbol }.Concat(type.AllInterfaces);

        foreach (INamedTypeSymbol? candidate in candidates)
        {
            if (candidate is { IsGenericType: true } generic &&
                generic.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.ICollection<T>")
            {
                return generic.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string metadataName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == metadataName)
                return true;

        return false;
    }

    private static string Describe(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private void Assign(string target, IPropertySymbol property, string expression) =>
        _body.Append("            ").Append(target).Append('.').Append(property.Name).Append(" = ").Append(expression).AppendLine(";");

    // ===== lines and diagnostics =====

    /// <summary>The generated code of what follows comes from this line of the markup.</summary>
    private void Line(IXmlLineInfo at)
    {
        if (!at.HasLineInfo()) return;

        _body.Append("#line ").Append(at.LineNumber).Append(" \"").Append(Escape(_path)).AppendLine("\"");
    }

    private void Line(XObject at) => Line((IXmlLineInfo)at);

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private void Report(DiagnosticDescriptor descriptor, XObject at, params object[] args) =>
        Report(descriptor, (IXmlLineInfo)at, args);

    private void Report(DiagnosticDescriptor descriptor, IXmlLineInfo at, params object[] args)
    {
        int line = at.HasLineInfo() ? at.LineNumber : 1;
        int column = at.HasLineInfo() ? at.LinePosition : 1;

        Report(descriptor, line, column, args);
    }

    private void Report(DiagnosticDescriptor descriptor, int line, int column, params object[] args)
    {
        var start = new LinePosition(Math.Max(0, line - 1), Math.Max(0, column - 1));
        Location location = Location.Create(_path, default, new LinePositionSpan(start, start));

        _diagnostics.Add(Diagnostic.Create(descriptor, location, args));
    }
}