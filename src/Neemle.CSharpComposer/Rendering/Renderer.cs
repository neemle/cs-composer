using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
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

    // GENE-29 F2: a block body written inside an expression (a block lambda or an anonymous method). Its lines are not
    // printed here: each starts with a nested line break, and the line writer of the expression prints them at the
    // indentation the expression reaches.
    bool nested;

    // The options of the render in progress, for the writers below the line level that render a nested body.
    [ThreadStatic] static RenderOptions? current;

    Renderer(RenderOptions options) { this.options = options; }

    // GENE-29 F4 (AD C1): every render of a document (CsDocument.Render, ToBytes and WriteTo) formats the whole document.
    // The output depends only on the model, the options and the document's newline (AD R0); a checked render then reads it
    // back through the meaning guard (AD D1).
    internal static string Render(CsDocument document, RenderOptions options)
    {
        var newline = Newline(options) ?? (document.SourceInfo is { } source ? SourceNewline.Dominant(source.Snapshot.Text) : "\n");
        using var scope = Enter(options);
        var errors = LanguageFeatures.RenderErrors(document, options);
        if (errors.Count != 0) throw new GenValidationException(errors);
        // An unchecked render (Validate = false) can still meet a symbol the lowering refuses; that refusal is
        // reported as the public validation exception, never as the internal one (GENE-27 D9).
        string Once(CsDocument model)
        {
            string text;
            try { text = Text(model, options); }
            catch (SymbolModel.InvalidModel error) { throw new GenValidationException(new[] { error.Diagnostic }); }
            return SourceNewline.Convert(text, newline);
        }
        // GENE-29 F3 (AD D1): a checked render reads its output back with the document's language version and the
        // preprocessor symbols it was parsed with. Output that is the parsed source itself, for the same language version,
        // reads back as that parse, which is kept: it is not parsed a second time.
        return !options.Validate ? Once(document) : MeaningGuard.Run(document, model => MeaningGuard.Intended(model, options), Once, text =>
            document.SourceInfo is { } source && source.Snapshot.Text == text && ((CsDocument)source.Parsed).LanguageVersion == document.LanguageVersion ? source.Parsed :
            DocumentParser.Parse(new SourceSnapshot(text, System.Text.Encoding.UTF8, []), new ParseOptions { LanguageVersion = document.LanguageVersion,
                PreprocessorSymbols = document.SourceInfo?.Snapshot.Symbols ?? Values<string>.Empty }, CancellationToken.None).Root);
    }

    // GENE-29 F2 (AD C3): RenderOptions.Newline, checked; null leaves the choice to the render: a parsed document's or tree's
    // dominant newline, "\n" for a built one (F4).
    internal static string? Newline(RenderOptions options) => options.Newline is null or "\n" or "\r\n" ? options.Newline :
        throw new ArgumentException("Newline must be \"\\n\" or \"\\r\\n\".", nameof(options));

    // The statements a tree lowers to, as the lines CsTree.Render writes (GENE-29 F4: formatted like any other render).
    internal static string Statements(CsBody body, RenderOptions options, CsLanguageVersion version)
    {
        CheckIndent(options);
        using var trivia = SyntaxTriviaWriter.Begin(options.Indent);
        var writer = new Renderer(options) { languageVersion = version };
        writer.SwitchBody(body);
        return trivia.Complete(writer.output.ToString());
    }

    static void CheckIndent(RenderOptions options)
    {
        if (options.Indent == null || options.Indent.Any(character => character != ' ' && character != '\t'))
            throw new ArgumentException("Indent must contain only spaces or tabs.", nameof(options));
    }

    static string Text(CsDocument document, RenderOptions options)
    {
        CheckIndent(options);
        using var trivia = SyntaxTriviaWriter.Begin(options.Indent);
        var writer = new Renderer(options);
        writer.Document(document);
        // GENE-29 F2 (AD C3): exactly one final line break.
        return trivia.Complete(writer.output.ToString()).TrimEnd('\n') + "\n";
    }

    // Makes `options` the options of the render in progress until the result is disposed: every render enters them
    // (CsDocument.Render, CsTree.Render and each node rendered on its own).
    internal static Scope Enter(RenderOptions options)
    {
        var previous = current;
        current = options;
        return new Scope(previous);
    }

    internal readonly struct Scope(RenderOptions? previous) : IDisposable
    {
        public void Dispose() => current = previous;
    }

    // A block body written inside an expression, as text for the expression's line: every line of the body starts with
    // a nested line break, and a line break inside the body's verbatim text stays as it is.
    internal static string Nested(CsBody body)
    {
        var options = current!;
        var writer = new Renderer(options) { nested = true, languageVersion = CsLanguageVersion.CSharp15 };
        writer.Body(body);
        // Each line closes the depth it opened, before the next line's break; a nested line break inside a line belongs
        // to a body nested deeper.
        var lines = ("\n" + writer.output.ToString().TrimEnd('\n')).Split("\n" + Layout.Nested);
        var text = new StringBuilder();
        foreach (string line in lines.Skip(1))
            text.Append(Layout.Nested).Append(line).Append(Layout.Shallower, line.TakeWhile(c => c == Layout.Deeper).Count());
        return text.ToString();
    }

    void WithSyntaxTrivia(CsNode? owner, Action write)
    {
        int start = output.Length;
        write();
        if (owner == null || owner.SyntaxTrivia.Count == 0) return;
        string rendered = SyntaxTriviaWriter.Apply(owner, output.ToString(start, output.Length - start), inline: false);
        output.Length = start; output.Append(rendered);
    }

    void Line(string text = "")
    {
        if (text.Length != 0)
        {
            Indent();
            Write(text);
        }
        output.Append('\n');
    }

    // A line's indentation; a nested body's line starts with a nested line break and its depth (GENE-29 F2).
    void Indent()
    {
        if (nested) { output.Append(Layout.Nested).Append(Layout.Deeper, depth); return; }
        for (int i = 0; i < depth; i++) output.Append(options.Indent);
    }

    // Text with layout, printed at the current column of the current line (GENE-29 F2); a nested body keeps it for the
    // expression's line writer.
    void Write(string text)
    {
        if (nested) { output.Append(text); return; }
        int column = 0;
        while (column < output.Length && output[output.Length - 1 - column] != '\n') column++;
        string indent = string.Concat(Enumerable.Repeat(options.Indent, depth));
        output.Append(Layout.Print(text, indent, options.Indent, options.MaxLineLength, column));
    }

    void Open() { Line("{"); depth++; }

    void Close() { depth--; Line("}"); }

    // Opaque text (AD C4): its first line starts at the formatter's indentation, without the text's own leading
    // whitespace; every later line is written as it is. GENE-29 F5: text that starts with a directive (one that did not
    // start its line in source, kept raw) continues the line before it, where it reads back the same; at the start of a
    // line it would read back as a directive.
    void Raw(string raw)
    {
        string text = Names.NormalizeLines(raw).TrimStart(' ', '\t');
        if (text.Length == 0) return;
        if (text[0] == '#' && DocumentParser.Directive(text.Split('\n')[0]) && Continuable()) { output[^1] = ' '; output.Append(text); }
        else { Indent(); Write(text); }
        if (text[text.Length - 1] != '\n') output.Append('\n');
    }

    // Whether the last line written can go on: one that is neither a directive or inactive text (written as a trivia
    // handle until the render completes) nor ends with a line comment. A blank line the blank-line table wrote before the
    // text goes, so that it continues the last line that holds anything. GENE-29 F6: a block body inside an expression
    // (a block lambda) holds its lines with their layout, which is read without it.
    bool Continuable()
    {
        if (output.Length == 0) return false;
        if (output.Length > 1 && output[^2] == '\n') output.Length--;
        int start = output.Length - 1;
        while (start > 0 && output[start - 1] != '\n') start--;
        string line = Layout.Flat(output.ToString(start, output.Length - start)).TrimStart();
        var tokens = Lexer.Scan(line);
        return !line.StartsWith('\u0001') && !tokens[Math.Max(0, tokens.Count - 2)].Text.StartsWith("//", StringComparison.Ordinal);
    }

    void Attributes(IEnumerable<CsAttribute> attributes)
    {
        foreach (var attribute in attributes) Line(Names.Attribute(attribute));
    }

    void Document(CsDocument document) => WithSyntaxTrivia(document, () => DocumentCore(document));

    void DocumentCore(CsDocument document)
    {
        languageVersion = document.LanguageVersion;
        // AD C3: the file preamble (the document's leading comments) is followed by one blank line.
        Comments(document, CommentPlacement.Leading);
        if (document.Comments.Any(c => c.Placement == CommentPlacement.Leading)) Line();
        Header(document);
        var items = Items(document, options);
        Content(document, items, node =>
        {
            if (node is CsAttribute attribute) Line(Names.Attribute(attribute));
            else WriteNode(node);
        });
        if (output.Length == 0) Line();
    }

    // GENE-29 F4 (AD C3, R0): a document's items as they are written, decided by the model and the options alone, for a
    // parsed and a built document alike. Without directives or inactive text among them, extern aliases come first, then
    // usings (each once), then attributes, then the rest, each in model order (a parsed document already holds them so);
    // with them, the model order stands. Each maximal run of consecutive usings is sorted when RenderOptions.SortUsings
    // asks for it, unless a comment could be carried away from the place it describes: when one of the usings holds a
    // leading comment (it moves with its using) or the document holds container comments (its end-of-file comments, F5).
    // A namespace named by CsDocument.Namespace is a namespace item around everything after the leading extern aliases,
    // usings and attributes.
    internal static CsNode[] Items(CsDocument document, RenderOptions options)
    {
        var usings = new HashSet<CsUsing>();
        var items = document.Items.Any(n => n is CsDirective or CsDisabledText) ? document.Items.ToArray() :
            document.Items.Where(n => n is not CsUsing u || usings.Add(u)).OrderBy(n => n switch { CsExternAlias => 0, CsUsing => 1, CsAttribute => 2, _ => 3 }).ToArray();
        if (options.SortUsings && !document.Comments.Any(c => c.Placement == CommentPlacement.Container))
            for (int start = 0, end; start < items.Length; start = end + 1)
            {
                for (end = start; end < items.Length && items[end] is CsUsing; end++) { }
                if (items[start..end].Any(u => u.Comments.Any(c => c.Placement == CommentPlacement.Leading))) continue;
                items[start..end].OrderBy(u => Layout.Flat(Using((CsUsing)u)), StringComparer.Ordinal).ToArray().CopyTo(items, start);
            }
        if (document.Namespace == null) return items;
        int prelude = items.TakeWhile(n => n is CsExternAlias or CsUsing or CsAttribute).Count();
        return [.. items[..prelude], new CsNamespace(document.Namespace, new(items[prelude..]), options.NamespaceStyle == NamespaceStyle.FileScoped)];
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

    internal static string Using(CsUsing value) => SyntaxTriviaWriter.Apply(value,
        (value.IsGlobal ? "global " : "") + "using " + (value.IsStatic ? "static " : "") + (value.IsUnsafe ? "unsafe " : "") +
        (value.Alias == null ? "" : Names.Id(value.Alias) + " = ") + (value.Target == null ? Names.Qualified(value.Name) : Names.Type(value.Target)) + ";", inline: false);

    static string Prefix(CsMember member)
    {
        string modifiers = string.Join(" ", Names.OrderModifiers(member.Modifiers));
        return modifiers.Length == 0 ? "" : modifiers + " ";
    }

    void Constraints(Values<TypeParameter> generics)
    {
        foreach (var parameter in generics.Where(value => value.Constraints.Count > 0)) Line(ConstraintClause(parameter));
    }

    internal static string ConstraintClause(TypeParameter parameter) => "where " + Names.Id(parameter.Name) + " : " +
        string.Join(", ", Names.Constraints(parameter).Select(Constraint));

    static string Constraint(CsConstraint constraint) =>
        InlineComments.Apply(constraint, constraint.Kind == "type" ? Names.Type(constraint.Type!) : constraint.Kind);

    void Member(CsMember member) => WithSyntaxTrivia(member, () => MemberCore(member));

    void MemberCore(CsMember member)
    {
        Comments(member, CommentPlacement.Leading);
        int start = output.Length;
        Attributes(member.Attributes);
        switch (member)
        {
            case CsTypeDeclaration type: Type(type); break;
            case CsProperty property: Property(property); break;
            case CsField field: Field(field); break;
            case CsMethod method: Method(method); break;
            case CsDisabledText text: Disabled(text); break;
            case CsDirective d: Directive(d); break;
            case CsDelegate d: Delegate(d); break;
            case CsDestructor d: Body(d.Body, Prefix(d) + "~" + Names.Id(d.Name) + "()"); break;
            case CsOperatorDeclaration o: Operator(o); break;
            case CsExtension e: Extension(e); break;
            case CsConstructor constructor: Constructor(constructor); break;
            case CsEvent @event: Event(@event); break;
            case CsRawMember raw: Raw(raw.Source); break;
            default: throw new ArgumentException("Unsupported member node.", nameof(member));
        }
        Dangling(member, start);
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
        Content(type, type.Members, member =>
        {
            if (member is CsEnumMember value) EnumMember(value);
            else Member((CsMember)member);
        });
        Close();
    }

    string TypeKind(TypeKind kind) =>
        kind == Neemle.CSharpComposer.Model.TypeKind.RecordClass ?
            (languageVersion < CsLanguageVersion.CSharp10 ? "record" : "record class") :
        kind == Neemle.CSharpComposer.Model.TypeKind.RecordStruct ? "record struct" :
        kind.ToString().ToLowerInvariant();

    static string PrimaryParameters(CsTypeDeclaration type) => type.PrimaryParameters.Count == 0 && !type.HasPrimaryConstructor
        ? "" : Names.Parameters("(", type.PrimaryParameters, ")");

    static string BaseTypes(CsTypeDeclaration type) => type.BaseTypes.Count == 0
        ? "" : " : " + string.Join(", ", type.BaseTypes.Select((t, i) => i == 0 && type.BaseArguments != null ? ExpressionWriter.Arguments(Names.Type(t) + "(", type.BaseArguments, ")") : Names.Type(t)));

    // A member ends with its comma, also on its own: a parsed member's source span runs through its comma.
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
        string head = Prefix(property) + Names.Type(property.Type) + " " + InterfacePrefix(property) + (property.IsIndexer ? Names.Parameters("this[", property.Parameters, "]") : Names.Id(property.Name));
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
        string signature = Names.Parameters(Prefix(method) + Names.Type(method.ReturnType) + " " + InterfacePrefix(method) + Names.Id(method.Name) +
            Names.Generics(method.TypeParameters) + "(", method.Parameters, ")");
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
        string signature = Names.Parameters(Prefix(constructor) + Names.Id(constructor.Name) + "(", constructor.Parameters, ")");
        if (constructor.InitializerKind != null)
            signature += ExpressionWriter.Arguments(" : " + constructor.InitializerKind + "(", constructor.InitializerArguments, ")");
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
        using (Names.Enter(accessor: Names.BacksField(accessor))) Body(accessor.Body, AccessorPrefix(accessor) + accessor.Kind);
        Comments(accessor, CommentPlacement.Trailing);
    }

    void Body(CsBody? body, string? signature = null) => WithSyntaxTrivia(body, () => BodyCore(body, signature));

    void BodyCore(CsBody? body, string? signature = null)
    {
        if (body == null) { Line((signature ?? "") + ";"); return; }
        if (body.Tree != null) body = SymbolModel.ToBody(body.Tree) with { Comments = new(body.Comments.Concat(body.Tree.Comments)) };
        if (body.Expression != null)
        {
            // GENE-29 F5: an expression body spans from its `=>`, so its leading comments come before the `=>`; its own
            // container comments (F4: a comment after `=>`) come before its expression.
            Line((signature == null ? "" : signature + " ") + string.Concat(body.Comments.Where(c => c.Placement == CommentPlacement.Leading).Select(InlineComments.Text)) +
                "=> " + string.Concat(body.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text)) + Names.Expression(body.Expression) + ";");
            return;
        }
        Comments(body, CommentPlacement.Leading);
        if (signature != null) Line(signature);
        Open();
        if (body.Raw != null) Raw(body.Raw);
        else Content(body, body.Statements, statement => Statement((CsStatement)statement));
        Close();
        Comments(body, CommentPlacement.Trailing);
    }

}
