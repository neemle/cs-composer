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
        using var trivia = SyntaxTriviaWriter.Begin();
        var writer = new Renderer(options);
        writer.Document(document);
        return trivia.Complete(writer.output.ToString());
    }

    void WithSyntaxTrivia(CsNode? owner, Action write)
    {
        int start = output.Length;
        write();
        if (owner == null || owner.SyntaxTrivia.Count == 0) return;
        string rendered = SyntaxTriviaWriter.Apply(owner, output.ToString(start, output.Length - start));
        output.Length = start; output.Append(rendered);
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

    void Document(CsDocument document) => WithSyntaxTrivia(document, () => DocumentCore(document));

    void DocumentCore(CsDocument document)
    {
        languageVersion = document.LanguageVersion;
        Comments(document, CommentPlacement.Leading);
        Header(document);
        if (document.Origin != null || document.Items.Any(n => n is CsDirective or CsDisabledText or CsExternAlias or CsGlobalStatement))
        {
            Namespace(document.Namespace);
            Content(document, document.Items, WriteNode);
            if (document.Namespace != null && options.NamespaceStyle == NamespaceStyle.BlockScoped) Close();
            Comments(document, CommentPlacement.Trailing);
            if (output.Length == 0) Line();
            return;
        }
        Comments(document, CommentPlacement.Container);
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

    static string Using(CsUsing value) => SyntaxTriviaWriter.Apply(value,
        (value.IsGlobal ? "global " : "") + "using " + (value.IsStatic ? "static " : "") + (value.IsUnsafe ? "unsafe " : "") +
        (value.Alias == null ? "" : Names.Id(value.Alias) + " = ") + (value.Target == null ? Names.Qualified(value.Name) : Names.Type(value.Target)) + ";");

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
        InlineComments.Apply(constraint, constraint.Kind == "type" ? Names.Type(constraint.Type!) : constraint.Kind);

    void Member(CsMember member) => WithSyntaxTrivia(member, () => MemberCore(member));

    void MemberCore(CsMember member)
    {
        Comments(member, CommentPlacement.Leading);
        Attributes(member.Attributes);
        switch (member)
        {
            case CsTypeDeclaration type: Type(type); break;
            case CsProperty property: Property(property); break;
            case CsField field: Field(field); break;
            case CsMethod method: Method(method); break;
            case CsDisabledText text: Disabled(text); break;
            case CsDirective d: output.Append(SyntaxTriviaWriter.Protect(d, includeComments: false)).Append('\n'); break;
            case CsDelegate d: Delegate(d); break;
            case CsDestructor d: Body(d.Body, Prefix(d) + "~" + Names.Id(d.Name) + "()"); break;
            case CsOperatorDeclaration o: Operator(o); break;
            case CsExtension e: Extension(e); break;
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
            (type.Kind == Neemle.CSharpComposer.Model.TypeKind.Union ? "(" + string.Join(", ", type.UnionCases.Select(Names.Type)) + ")" : PrimaryParameters(type)) + BaseTypes(type));
        depth++;
        Constraints(type.TypeParameters);
        depth--;
        Open();
        int i = 0; CsNode? previous = null;
        Content(type, type.Members, member =>
        {
            if (i > 0 && type.Kind != Neemle.CSharpComposer.Model.TypeKind.Enum && member is not (CsDirective or CsDisabledText) && previous is not (CsDirective or CsDisabledText)) Line();
            if (member is CsEnumMember value) EnumMember(value);
            else Member((CsMember)member);
            i++; previous = member;
        });
        Close();
    }

    string TypeKind(TypeKind kind) =>
        kind == Neemle.CSharpComposer.Model.TypeKind.RecordClass ?
            (languageVersion < CsLanguageVersion.CSharp10 ? "record" : "record class") :
        kind == Neemle.CSharpComposer.Model.TypeKind.RecordStruct ? "record struct" :
        kind.ToString().ToLowerInvariant();

    static string PrimaryParameters(CsTypeDeclaration type) => type.PrimaryParameters.Count == 0 && !type.HasPrimaryConstructor
        ? "" : "(" + string.Join(", ", type.PrimaryParameters.Select(Names.Parameter)) + ")";

    static string BaseTypes(CsTypeDeclaration type) => type.BaseTypes.Count == 0
        ? "" : " : " + string.Join(", ", type.BaseTypes.Select((t, i) => Names.Type(t) + (i == 0 && type.BaseArguments != null ? "(" + string.Join(", ", type.BaseArguments.Select(Names.Expression)) + ")" : "")));

    void EnumMember(CsEnumMember value) => WithSyntaxTrivia(value, () => EnumMemberCore(value));

    void EnumMemberCore(CsEnumMember value)
    {
        Comments(value, CommentPlacement.Leading);
        Attributes(value.Attributes);
        Line(Names.Id(value.Name) + (value.Value == null ? "" : " = " + Names.Expression(value.Value)) + ",");
        Comments(value, CommentPlacement.Trailing);
    }

    void Property(CsProperty property)
    {
        string head = Prefix(property) + Names.Type(property.Type) + " " + InterfacePrefix(property) + (property.IsIndexer ? "this[" + string.Join(", ", property.Parameters.Select(Names.Parameter)) + "]" : Names.Id(property.Name));
        if (property.Expression != null)
        {
            Line(head + " => " + Names.Expression(property.Expression) + ";");
            return;
        }
        if (property.Accessors.All(accessor => accessor.Body == null && accessor.Attributes.Count == 0 && accessor.Comments.Count == 0 && accessor.SyntaxTrivia.Count == 0))
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
        (accessor.Accessibility == null ? "" : accessor.Accessibility + " ") + (accessor.Modifiers.Count == 0 ? "" : string.Join(" ", accessor.Modifiers) + " ");

    static string Initializer(CsExpression? expression, string suffix = "") =>
        expression == null ? "" : " = " + Names.Expression(expression) + suffix;

    void Field(CsField field) =>
        Line(Prefix(field) + Names.Type(field.Type) + " " + Names.Id(field.Name) + (field.FixedSize == null ? "" : "[" + Names.Expression(field.FixedSize) + "]") +
            Initializer(field.Initializer) + string.Concat(field.AdditionalVariables.Select(v => ", " + LoopHeaderWriter.Variable(v))) + ";");

    void Method(CsMethod method)
    {
        string signature = Prefix(method) + Names.Type(method.ReturnType) + " " + InterfacePrefix(method) + Names.Id(method.Name) +
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
        string signature = Prefix(@event) + "event " + Names.Type(@event.Type) + " " + InterfacePrefix(@event) + Names.Id(@event.Name);
        if (@event.Accessors.Count == 0) Line(signature + Initializer(@event.Initializer) + string.Concat(@event.AdditionalVariables.Select(v => ", " + LoopHeaderWriter.Variable(v))) + ";");
        else
        {
            Line(signature);
            Open();
            foreach (var accessor in @event.Accessors) Accessor(accessor);
            Close();
        }
    }

    void Accessor(CsAccessor accessor) => WithSyntaxTrivia(accessor, () => AccessorCore(accessor));

    void AccessorCore(CsAccessor accessor)
    {
        Comments(accessor, CommentPlacement.Leading); Comments(accessor, CommentPlacement.Container);
        Attributes(accessor.Attributes);
        Body(accessor.Body, AccessorPrefix(accessor) + accessor.Kind);
        Comments(accessor, CommentPlacement.Trailing);
    }

    void Body(CsBody? body, string? signature = null) => WithSyntaxTrivia(body, () => BodyCore(body, signature));

    void BodyCore(CsBody? body, string? signature = null)
    {
        if (body == null) { Line((signature ?? "") + ";"); return; }
        if (body.Tree != null) body = SymbolModel.ToBody(body.Tree) with { Comments = new(body.Comments.Concat(body.Tree.Comments)) };
        Comments(body, CommentPlacement.Leading);
        if (body.Expression != null)
        {
            Line((signature == null ? "" : signature + " ") + "=> " + Names.Expression(body.Expression) + ";");
            return;
        }
        if (signature != null) Line(signature);
        Open();
        if (body.Raw != null) Raw(body.Raw);
        else Content(body, body.Statements, statement => Statement((CsStatement)statement));
        Close();
        Comments(body, CommentPlacement.Trailing);
    }

}
