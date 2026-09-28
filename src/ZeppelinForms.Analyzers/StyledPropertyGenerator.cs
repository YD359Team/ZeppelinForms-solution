using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ZeppelinForms.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class StyledPropertyGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "ZeppelinForms.Forms.Styling.StyledAttribute";
    private const string ElementMetadataName = "ZeppelinForms.Forms.Controls.Base.UIElement";

    /// <summary>The same default as StyledAttribute.Category. The generator passes the
    /// category to the registration explicitly, so the attribute's own default never
    /// reached it — and this used to be the Russian "Прочее".</summary>
    private const string DefaultCategory = "Other";

    /// <summary>Type names with global:: and with nullable annotations kept.</summary>
    /// <remarks>
    /// A plain ToDisplayString gives "ZeppelinForms.Forms.Enums.TextTransform", which
    /// resolves wrongly wherever its first segment is shadowed: inside ZeppelinForms.Android
    /// the name Android is our own namespace, and a styled property typed from the Android
    /// SDK would not compile there.
    /// </remarks>
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor PropertyNotPartial = new(
        id: "ZF0003",
        title: "A property with [Styled] must be partial",
        messageFormat: "Property '{0}' is marked [Styled] but is not declared partial — " +
                       "the generator has nowhere to add the accessors",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeNotPartial = new(
        id: "ZF0004",
        title: "A type with [Styled] properties must be partial",
        messageFormat: "Type '{0}' contains [Styled] properties but is not declared partial",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeNotElement = new(
        id: "ZF0005",
        title: "[Styled] applies only to descendants of UIElement",
        messageFormat: "Type '{0}' doesn't derive from UIElement, and that is where the value source is stored",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => Read(ctx))
            .Collect();

        context.RegisterSourceOutput(models, Emit);
    }

    private static Model Read(GeneratorAttributeSyntaxContext context)
    {
        var property = (IPropertySymbol)context.TargetSymbol;
        var syntax = (PropertyDeclarationSyntax)context.TargetNode;
        INamedTypeSymbol owner = property.ContainingType;

        Location location = syntax.Identifier.GetLocation();

        var attribute = context.Attributes[0];
        bool external = Argument(attribute, "External") is true;

        // an external property's accessors are written by the control, the generator
        // has nothing to add — so partial isn't required either
        if (!external && !syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            return Model.Failed(PropertyNotPartial, location, property.Name);

        if (!IsPartial(owner))
            return Model.Failed(TypeNotPartial, location, owner.Name);

        if (!DerivesFromElement(owner))
            return Model.Failed(TypeNotElement, location, owner.Name);

        return new Model(
            Namespace: owner.ContainingNamespace.ToDisplayString(),
            OwnerName: owner.Name,
            PropertyName: property.Name,
            ValueType: property.Type.ToDisplayString(TypeFormat),
            Category: Argument(attribute, "Category") as string ?? DefaultCategory,
            AffectsLayout: Argument(attribute, "AffectsLayout") is true,
            Inherits: Argument(attribute, "Inherits") is true,
            External: external,
            HasDefault: HasDefaultProperty(owner, property.Name),
            Error: null,
            ErrorLocation: null,
            ErrorArgument: null);
    }

    private static object? Argument(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
            if (pair.Key == name)
                return pair.Value.Value;

        return null;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;

        return false;
    }

    private static bool DerivesFromElement(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == ElementMetadataName)
                return true;

        return false;
    }

    /// <summary>The default is looked for among static properties, not fields.
    /// Static fields are initialized in declaration order, and the declarations
    /// spread over different files of a partial type — then registration could read
    /// the default before it is computed. A property is computed on access,
    /// and the order stops mattering.</summary>
    private static bool HasDefaultProperty(INamedTypeSymbol owner, string propertyName)
    {
        foreach (ISymbol member in owner.GetMembers(propertyName + "Default"))
            if (member is IPropertySymbol { IsStatic: true })
                return true;

        return false;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Model> models)
    {
        foreach (Model model in models)
        {
            if (model.Error is not null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(model.Error, model.ErrorLocation, model.ErrorArgument));
            }
        }

        var byOwner = models
            .Where(static model => model.Error is null)
            .GroupBy(static model => (model.Namespace, model.OwnerName));

        foreach (var group in byOwner)
        {
            string source = Render(group.Key.Namespace, group.Key.OwnerName, group.ToList());

            // the namespace is part of the name: two partial types with the same name in
            // different namespaces used to produce the same hint name, and Roslyn throws
            // on a repeated AddSource — the whole generator failed, and none of the
            // assembly's styled properties compiled
            context.AddSource(
                $"{group.Key.Namespace}.{group.Key.OwnerName}.Styled.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string Render(string ns, string owner, List<Model> properties)
    {
        var text = new StringBuilder();

        text.AppendLine("// <auto-generated/>");
        text.AppendLine("#nullable enable");
        text.AppendLine();
        text.AppendLine("using ZeppelinForms.Forms.Styling;");
        text.AppendLine();
        text.AppendLine($"namespace {ns};");
        text.AppendLine();
        text.AppendLine($"partial class {owner}");
        text.AppendLine("{");

        foreach (Model property in properties)
        {
            string field = Field(property.PropertyName);
            string @default = property.HasDefault
                ? $"{property.PropertyName}Default"
                : "default!";

            if (property.External)
            {
                // The value lives in another object, so both reading and writing go
                // through the property itself: the registration delegates lead to the
                // accessors the control wrote. There is no field here.
                text.AppendLine($"    public static readonly StyledProperty<{property.ValueType}> {property.PropertyName}Property =");
                text.AppendLine($"        StyledProperty<{property.ValueType}>.Register<{owner}>(");
                text.AppendLine($"            \"{property.PropertyName}\",");
                text.AppendLine($"            static owner => owner.{property.PropertyName},");
                text.AppendLine($"            static (owner, value) => owner.Write{property.PropertyName}(value),");
                text.AppendLine($"            {@default},");
                text.AppendLine($"            \"{property.Category}\",");
                text.AppendLine($"            {Literal(property.AffectsLayout)},");
                text.AppendLine($"            {Literal(property.Inherits)});");
                text.AppendLine();

                continue;
            }

            text.AppendLine($"    public static readonly StyledProperty<{property.ValueType}> {property.PropertyName}Property =");
            text.AppendLine($"        StyledProperty<{property.ValueType}>.Register<{owner}>(");
            text.AppendLine($"            \"{property.PropertyName}\",");
            text.AppendLine($"            static owner => owner.{field},");
            text.AppendLine($"            static (owner, value) => owner.{field} = value,");
            text.AppendLine($"            {@default},");
            text.AppendLine($"            \"{property.Category}\",");
            text.AppendLine($"            {Literal(property.AffectsLayout)},");
            text.AppendLine($"            {Literal(property.Inherits)});");
            text.AppendLine();
            text.AppendLine($"    private {property.ValueType} {field} = {@default};");
            text.AppendLine();
            text.AppendLine($"    public partial {property.ValueType} {property.PropertyName}");
            text.AppendLine("    {");

            // the getter goes through Presented: inside drawing it returns the
            // intermediate value of a running transition, outside — the target itself
            text.AppendLine(property.Inherits
                ? $"        get => Presented({property.PropertyName}Property, GetInheritedValue({property.PropertyName}Property));"
                : $"        get => Presented({property.PropertyName}Property, {field});");

            text.AppendLine($"        set => SetValue({property.PropertyName}Property, ref {field}, value);");
            text.AppendLine("    }");
            text.AppendLine();
        }

        text.AppendLine("}");

        return text.ToString();
    }

    private static string Field(string propertyName) =>
        "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);

    private static string Literal(bool value) => value ? "true" : "false";

    private sealed record Model
    {
        public string Namespace { get; set; }
        public string OwnerName { get; set; }
        public string PropertyName { get; set; }
        public string ValueType { get; set; }
        public string Category { get; set; }
        public bool AffectsLayout { get; set; }
        public bool Inherits { get; set; }
        public bool External { get; set; }
        public bool HasDefault { get; set; }
        public DiagnosticDescriptor? Error { get; set; }
        public Location? ErrorLocation { get; set; }
        public string? ErrorArgument { get; set; }

        public Model(
        string Namespace,
        string OwnerName,
        string PropertyName,
        string ValueType,
        string Category,
        bool AffectsLayout,
        bool Inherits,
        bool External,
        bool HasDefault,
        DiagnosticDescriptor? Error,
        Location? ErrorLocation,
        string? ErrorArgument)
        {
            this.Namespace = Namespace;
            this.OwnerName = OwnerName;
            this.PropertyName = PropertyName;
            this.ValueType = ValueType;
            this.Category = Category;
            this.AffectsLayout = AffectsLayout;
            this.Inherits = Inherits;
            this.External = External;
            this.HasDefault = HasDefault;
            this.Error = Error;
            this.ErrorLocation = ErrorLocation;
            this.ErrorArgument = ErrorArgument;
        }
        public static Model Failed(DiagnosticDescriptor error, Location location, string argument) =>
            new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                false, false, false, false, error, location, argument);
    }
}