using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    // `sourceDirectives`: a node rendered again in the place of its parsed text (not reformatted) writes each parsed
    // directive it owns at the directive's source indentation (GENE-27 O2a).
    internal static string Node(CsNode node, RenderOptions options, CsLanguageVersion version, string basePrefix = "",
        bool statementList = false, bool sourceDirectives = false)
    {
        using var trivia = SyntaxTriviaWriter.Begin(options.Indent, basePrefix, sourceDirectives);
        using var scope = Enter(options);
        var writer = new Renderer(options) { basePrefix = basePrefix, languageVersion = version };
        // A top-level statement's value reaches here as the statement it lowers to (GENE-27 phase 4), which writes its
        // own terminator.
        if (statementList && node is CsBody body) writer.SwitchBody(body);
        else writer.WriteNode(node);
        return trivia.Complete(writer.output.ToString());
    }

    void WriteNode(CsNode node)
    {
        switch (node)
        {
            case CsExternAlias alias: Line(SyntaxTriviaWriter.Apply(alias, "extern alias " + Names.Id(alias.Name) + ";", inline: false)); break;
            case CsGlobalStatement statement: SwitchBody(SymbolModel.ToBody(Cs.Tree(statement.Value))); break;
            case CsDocument document: Document(document); break;
            case CsEnumMember value: EnumMember(value); break;
            case CsMember member: Member(member); break;
            case CsNamespace space: NamespaceNode(space); break;
            case CsRaw raw: Opaque(raw.Text); break;
            case CsBody body: Body(body); break;
            case CsStatement statement: Statement(statement); break;
            case CsSymbol symbol: Write(Names.Expression(SymbolModel.ToExpression(symbol))); break;
            case CsExpression expression: Write(Names.Expression(expression)); break;
            case CsQueryClause clause: Write(QueryWriter.Clause(clause)); break;
            case CsLambdaParameter parameter: Write(LambdaWriter.Parameter(parameter)); break;
            case CsForInitializer initializer: Write(LoopHeaderWriter.Initializer(initializer)); break;
            case CsVariableDeclarator variable: Write(LoopHeaderWriter.Variable(variable)); break;
            case CsPattern pattern: Write(PatternWriter.Write(pattern)); break;
            case CsSwitchArm arm: Write(ExpressionWriter.Arm(arm)); break;
            case CsSwitchLabel label: SwitchLabel(label); break;
            case CsSwitchSection section: SwitchSection(section); break;
            case CsType type: Write(Names.Type(type)); break;
            case CsParameter parameter: Write(Names.Parameter(parameter)); break;
            case CsAttribute attribute: Write(Names.AttributeText(attribute, false)); break;
            case CsAccessor accessor: Accessor(accessor); break;
            case CsUsing use: Line(Using(use)); break;
            case CsComment comment: CommentNode(comment); break;
            case TypeParameter parameter: output.Append(parameter.Name); break;
            // A node rendered on its own is a document or namespace item (the kinds graph validation allows there) or
            // a node with a parse origin; constraints, tuple elements, attribute arguments, syntax trivia and
            // documentation nodes never have one, and the last kind a parse binds is a catch clause.
            default: Catch((CsCatch)node); break;
        }
    }

    void NamespaceNode(CsNamespace space) => WithSyntaxTrivia(space, () => NamespaceCore(space));

    void NamespaceCore(CsNamespace space)
    {
        Comments(space, CommentPlacement.Leading);
        Line("namespace " + Names.Qualified(space.Name) + (space.IsFileScoped ? ";" : ""));
        if (!space.IsFileScoped) Open();
        Content(space, space.Items, WriteNode);
        if (!space.IsFileScoped) Close();
        Comments(space, CommentPlacement.Trailing);
    }

    void Comments(CsNode node, CommentPlacement placement)
    {
        // A trailing comment joins the end of its node's line, except after a trailing line comment, which runs to
        // its line end: the next comment then starts its own line rather than ending up inside it (GENE-27 D5).
        bool afterLineComment = false;
        foreach (var comment in node.Comments.Where(c => c.Placement == placement))
        {
            if (placement != CommentPlacement.Trailing) { CommentNode(comment); continue; }
            string text = DocumentationWriter.Comment(comment);
            if (afterLineComment) { Indent(); Write(text); }
            else
            {
                if (output.Length > 0 && output[^1] == '\n') output.Length--;
                Write(" " + text);
            }
            if (comment.Text.Length == 0 || comment.Text[^1] is not ('\r' or '\n')) output.Append('\n');
            afterLineComment = text.StartsWith("//", StringComparison.Ordinal);
        }
    }

    // A comment rendered on its own: a `///` documentation comment indents every line, like a leading comment.
    void CommentNode(CsComment comment)
    {
        if (comment.Documentation != null && !comment.BlockDocumentation)
            foreach (string line in DocumentationWriter.Comment(comment).Split('\n')) Line(line);
        else Opaque(DocumentationWriter.Comment(comment));
    }

    void Opaque(string text)
    {
        if (text.Length == 0) return;
        Indent();
        Write(text);
        if (text[^1] is not ('\n' or '\r')) output.Append('\n');
    }

    // AD C3: `#region` and `#endregion` lines at the code's indentation, every other directive at column 0.
    void Directive(CsDirective directive)
    {
        if (directive.Kind is DirectiveKind.Region or DirectiveKind.EndRegion) Indent();
        output.Append(SyntaxTriviaWriter.Protect(directive, includeComments: false)).Append('\n');
    }
}
