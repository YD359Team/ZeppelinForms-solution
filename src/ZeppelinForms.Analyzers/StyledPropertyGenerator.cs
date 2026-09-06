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

    private static readonly DiagnosticDescriptor PropertyNotPartial = new(
        id: "ZF0003",
        title: "Свойство с [Styled] должно быть partial",
        messageFormat: "Свойство '{0}' помечено [Styled], но не объявлено partial — " +
                       "генератору некуда дописать аксессоры",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeNotPartial = new(
        id: "ZF0004",
        title: "Тип со свойствами [Styled] должен быть partial",
        messageFormat: "Тип '{0}' содержит свойства с [Styled], но не объявлен partial",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeNotElement = new(
        id: "ZF0005",
        title: "[Styled] применимо только к наследникам UIElement",
        messageFormat: "Тип '{0}' не наследует UIElement, а источник значения хранится именно там",
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

        if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
            return Model.Failed(PropertyNotPartial, location, property.Name);

        if (!IsPartial(owner))
            return Model.Failed(TypeNotPartial, location, owner.Name);

        if (!DerivesFromElement(owner))
            return Model.Failed(TypeNotElement, location, owner.Name);

        var attribute = context.Attributes[0];

        return new Model(
            Namespace: owner.ContainingNamespace.ToDisplayString(),
            OwnerName: owner.Name,
            PropertyName: property.Name,
            ValueType: property.Type.ToDisplayString(),
            Category: Argument(attribute, "Category") as string ?? "Прочее",
            AffectsLayout: Argument(attribute, "AffectsLayout") is true,
            Inherits: Argument(attribute, "Inherits") is true,
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

    /// <summary>Умолчание ищем именно среди статических свойств, а не полей.
    /// Статические поля инициализируются в порядке объявления, а объявления
    /// разъезжаются по разным файлам partial-типа — тогда регистрация могла бы
    /// прочитать умолчание до того, как оно вычислено. Свойство вычисляется
    /// при обращении, и порядок перестаёт иметь значение.</summary>
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

            context.AddSource(
                $"{group.Key.OwnerName}.Styled.g.cs",
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

            text.AppendLine(property.Inherits
                ? $"        get => GetInheritedValue({property.PropertyName}Property);"
                : $"        get => {field};");

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

    private sealed record Model {
        public string Namespace { get; set; }
        public string OwnerName { get; set; }
        public string PropertyName { get; set; }
        public string ValueType { get; set; }
        public string Category { get; set; }
        public bool AffectsLayout { get; set; }
        public bool Inherits { get; set; }
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
            this.HasDefault = HasDefault;
            this.Error = Error;
            this.ErrorLocation = ErrorLocation;
            this.ErrorArgument = ErrorArgument;
        }
        public static Model Failed(DiagnosticDescriptor error, Location location, string argument) =>
            new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                false, false, false, error, location, argument);
    }
}