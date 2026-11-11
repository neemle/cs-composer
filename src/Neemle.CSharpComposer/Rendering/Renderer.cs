using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    readonly StringBuilder output = new StringBuilder();
    readonly RenderOptions options;
    int depth;
    CsLanguageVersion languageVersion;

    Renderer(RenderOptions options) { this.options = options; }

    internal static string Render(CsDocument document, RenderOptions options)
    {
        if (options.Indent == null || options.Indent.Any(character => character != ' ' && character != '\t'))
            throw new ArgumentException("Indent must contain only spaces or tabs.", nameof(options));
        var errors = LanguageFeatures.RenderErrors(document, options);
        if (errors.Count != 0) throw new GenValidationException(errors);
        var writer = new Renderer(options);
        writer.Document(document);
        return writer.output.ToString();
    }

    void Line(string text = "")
    {
        if (text.Length != 0)
        {
            Indent();
            output.Append(text);
        }
        output.Append('\n');
    }

    void Indent()
    {
        for (int i = 0; i < depth; i++) output.Append(options.Indent);
    }

    void Open() { Line("{"); depth++; }
    void Close() { depth--; Line("}"); }

    void Raw(string raw)
    {
        string text = Names.NormalizeLines(raw);
        if (text.Length == 0) return;
        Indent();
        output.Append(text);
        if (text[text.Length - 1] != '\n') output.Append('\n');
    }

    void Attributes(IEnumerable<CsAttribute> attributes)
    {
        foreach (var attribute in attributes) Line(Names.Attribute(attribute));
    }

    void Document(CsDocument document)
    {
        languageVersion = document.LanguageVersion;
        Comments(document, CommentPlacement.Leading);
        Comments(document, CommentPlacement.Container);
        Header(document);
        Usings(document);
        Attributes(document.Attributes);
        if (document.Attributes.Count != 0) Line();
        Namespace(document.Namespace);
        var content = document.Items.Where(n => n is not (CsUsing or CsAttribute)).ToArray();
        for (int i = 0; i < content.Length; i++)
        {
            if (i > 0) Line();
            WriteNode(content[i]);
        }
        if (document.Namespace != null && options.NamespaceStyle == NamespaceStyle.BlockScoped) Close();
        if (output.Length == 0) Line();
    }

    void Header(CsDocument document)
    {
        if (document.Header != null) { Raw(document.Header); Line(); }
        var nullable = options.Nullable ?? document.NullableContext;
        if (!Enum.IsDefined(typeof(NullableMode), nullable))
            throw new ArgumentException("Unknown nullable mode.", nameof(options));
        if (!Enum.IsDefined(typeof(NamespaceStyle), options.NamespaceStyle))
            throw new ArgumentException("Unknown namespace style.", nameof(options));
        if (nullable != NullableMode.Omit)
        {
            Line("#nullable " + nullable.ToString().ToLowerInvariant());
            Line();
        }
    }

    void Usings(CsDocument document)
    {
        var usings = document.Usings.Distinct();
        if (options.SortUsings) usings = usings.OrderBy(Using, StringComparer.Ordinal);
        foreach (var @using in usings) Line(Using(@using));
        if (document.Usings.Count != 0) Line();
    }

    void Namespace(string? name)
    {
        if (name == null) return;
        Line("namespace " + Names.Qualified(name) +
            (options.NamespaceStyle == NamespaceStyle.FileScoped ? ";" : ""));
        if (options.NamespaceStyle == NamespaceStyle.BlockScoped) Open();
        else Line();
    }

    static string Using(CsUsing value) =>
        (value.IsGlobal ? "global " : "") + "using " + (value.IsStatic ? "static " : "") +
        (value.Alias == null ? "" : Names.Id(value.Alias) + " = ") + Names.Qualified(value.Name) + ";";

    static string Prefix(CsMember member)
    {
        string modifiers = string.Join(" ", Names.OrderModifiers(member.Modifiers));
        return modifiers.Length == 0 ? "" : modifiers + " ";
    }

    void Constraints(Values<TypeParameter> generics)
    {
        foreach (var parameter in generics.Where(value => value.Constraints.Count > 0))
            Line("where " + Names.Id(parameter.Name) + " : " +
                string.Join(", ", Names.Constraints(parameter).Select(Constraint)));
    }

    static string Constraint(CsConstraint constraint) =>
        constraint.Kind == "type" ? Names.Type(constraint.Type!) : constraint.Kind;

    void Member(CsMember member)
    {
        Comments(member, CommentPlacement.Leading);
        Attributes(member.Attributes);
        switch (member)
        {
            case CsTypeDeclaration type: Type(type); break;
            case CsProperty property: Property(property); break;
            case CsField field: Field(field); break;
            case CsMethod method: Method(method); break;
            case CsConstructor constructor: Constructor(constructor); break;
            case CsEvent @event: Event(@event); break;
            case CsRawMember raw: Raw(raw.Source); break;
            default: throw new ArgumentException("Unsupported member node.", nameof(member));
        }
        Comments(member, CommentPlacement.Trailing);
    }

    void Type(CsTypeDeclaration type)
    {
        Line(Prefix(type) + TypeKind(type.Kind) + " " + Names.Id(type.Name) + Names.Generics(type.TypeParameters) +
            PrimaryParameters(type) + BaseTypes(type));
        depth++;
        Constraints(type.TypeParameters);
        depth--;
        Open();
        Comments(type, CommentPlacement.Container);
        for (int i = 0; i < type.Members.Count; i++)
        {
            if (i > 0 && type.Kind != Neemle.CSharpComposer.Model.TypeKind.Enum) Line();
            if (type.Members[i] is CsEnumMember value) EnumMember(value);
            else Member(type.Members[i]);
        }
        Close();
    }

    string TypeKind(TypeKind kind) =>
        kind == Neemle.CSharpComposer.Model.TypeKind.RecordClass ?
            (languageVersion < CsLanguageVersion.CSharp10 ? "record" : "record class") :
        kind == Neemle.CSharpComposer.Model.TypeKind.RecordStruct ? "record struct" :
        kind.ToString().ToLowerInvariant();

    static string PrimaryParameters(CsTypeDeclaration type) => type.PrimaryParameters.Count == 0
        ? "" : "(" + string.Join(", ", type.PrimaryParameters.Select(Names.Parameter)) + ")";

    static string BaseTypes(CsTypeDeclaration type) => type.BaseTypes.Count == 0
        ? "" : " : " + string.Join(", ", type.BaseTypes.Select(Names.Type));

    void EnumMember(CsEnumMember value)
    {
        Attributes(value.Attributes);
        Line(Names.Id(value.Name) + (value.Value == null ? "" : " = " + Names.Expression(value.Value)) + ",");
    }

    void Property(CsProperty property)
    {
        string head = Prefix(property) + Names.Type(property.Type) + " " + Names.Id(property.Name);
        if (property.Expression != null)
        {
            Line(head + " => " + Names.Expression(property.Expression) + ";");
            return;
        }
        if (property.Accessors.All(accessor => accessor.Body == null && accessor.Attributes.Count == 0))
            Line(head + " { " + string.Join(" ", property.Accessors.Select(AutoAccessor)) + " }" +
                Initializer(property.Initializer, ";"));
        else PropertyAccessors(property, head);
    }

    void PropertyAccessors(CsProperty property, string head)
    {
        Line(head);
        Open();
        foreach (var accessor in property.Accessors) Accessor(accessor);
        depth--;
        Line("}" + Initializer(property.Initializer, ";"));
    }

    static string AutoAccessor(CsAccessor accessor) =>
        AccessorPrefix(accessor) + accessor.Kind + ";";

    static string AccessorPrefix(CsAccessor accessor) =>
        accessor.Accessibility == null ? "" : accessor.Accessibility + " ";

    static string Initializer(CsExpression? expression, string suffix = "") =>
        expression == null ? "" : " = " + Names.Expression(expression) + suffix;

    void Field(CsField field) =>
        Line(Prefix(field) + Names.Type(field.Type) + " " + Names.Id(field.Name) +
            Initializer(field.Initializer) + ";");

    void Method(CsMethod method)
    {
        string signature = Prefix(method) + Names.Type(method.ReturnType) + " " + Names.Id(method.Name) +
            Names.Generics(method.TypeParameters) + "(" +
            string.Join(", ", method.Parameters.Select(Names.Parameter)) + ")";
        if (method.TypeParameters.Any(parameter => parameter.Constraints.Count > 0))
        {
            Line(signature);
            depth++;
            Constraints(method.TypeParameters);
            depth--;
            Body(method.Body);
        }
        else Body(method.Body, signature);
    }

    void Constructor(CsConstructor constructor)
    {
        string signature = Prefix(constructor) + Names.Id(constructor.Name) + "(" +
            string.Join(", ", constructor.Parameters.Select(Names.Parameter)) + ")";
        if (constructor.InitializerKind != null)
            signature += " : " + constructor.InitializerKind + "(" +
                string.Join(", ", constructor.InitializerArguments.Select(Names.Expression)) + ")";
        Body(constructor.Body, signature);
    }

    void Event(CsEvent @event)
    {
        string signature = Prefix(@event) + "event " + Names.Type(@event.Type) + " " + Names.Id(@event.Name);
        if (@event.Accessors.Count == 0) Line(signature + Initializer(@event.Initializer) + ";");
        else
        {
            Line(signature);
            Open();
            foreach (var accessor in @event.Accessors) Accessor(accessor);
            Close();
        }
    }

    void Accessor(CsAccessor accessor)
    {
        Attributes(accessor.Attributes);
        Body(accessor.Body, AccessorPrefix(accessor) + accessor.Kind);
    }

    void Body(CsBody? body, string? signature = null)
    {
        if (body == null) { Line((signature ?? "") + ";"); return; }
        if (body.Expression != null)
        {
            Line((signature == null ? "" : signature + " ") + "=> " + Names.Expression(body.Expression) + ";");
            return;
        }
        if (signature != null) Line(signature);
        Open();
        if (body.Raw != null) Raw(body.Raw);
        else foreach (var statement in body.Statements) Statement(statement);
        Close();
    }

}
