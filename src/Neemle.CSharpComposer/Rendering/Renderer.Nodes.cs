using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    internal static string Node(CsNode node, RenderOptions options, CsLanguageVersion version, string basePrefix = "",
        bool statementList = false)
    {
        using var trivia = SyntaxTriviaWriter.Begin();
        var writer = new Renderer(options) { basePrefix = basePrefix, languageVersion = version };
        if (statementList && node is CsBody body) writer.SwitchBody(body);
        // A top-level statement's value is a statement, not an expression: render it with its terminator.
        else if (statementList && node is CsSymbol or CsInstruction) writer.SwitchBody(SymbolModel.ToBody(Cs.Tree(node)));
        else writer.WriteNode(node);
        return trivia.Complete(writer.output.ToString());
    }

    void WriteNode(CsNode node)
    {
        switch (node)
        {
            case CsExternAlias alias: Line(SyntaxTriviaWriter.Apply(alias, "extern alias " + Names.Id(alias.Name) + ";")); break;
            case CsGlobalStatement statement: SwitchBody(SymbolModel.ToBody(Cs.Tree(statement.Value))); break;
            case CsDocument document: Document(document); break;
            case CsMember member: Member(member); break;
            case CsNamespace space: NamespaceNode(space); break;
            case CsRaw raw: Opaque(raw.Text); break;
            case CsBody body: Body(body); break;
            case CsStatement statement: Statement(statement); break;
            case CsSymbol symbol: output.Append(Names.Expression(SymbolModel.ToExpression(symbol))); break;
            case CsExpression expression: output.Append(Names.Expression(expression)); break;
            case CsQueryClause clause: output.Append(QueryWriter.Clause(clause)); break;
            case CsLambdaParameter parameter: output.Append(LambdaWriter.Parameter(parameter)); break;
            case CsForInitializer initializer: output.Append(LoopHeaderWriter.Initializer(initializer)); break;
            case CsVariableDeclarator variable: output.Append(LoopHeaderWriter.Variable(variable)); break;
            case CsPattern pattern: output.Append(PatternWriter.Write(pattern)); break;
            case CsSwitchArm arm: output.Append(ExpressionWriter.Arm(arm)); break;
            case CsSwitchLabel label: SwitchLabel(label); break;
            case CsSwitchSection section: SwitchSection(section); break;
            case CsType type: output.Append(Names.Type(type)); break;
            case CsParameter parameter: output.Append(Names.Parameter(parameter)); break;
            case CsAttribute attribute: output.Append(Names.AttributeText(attribute, false)); break;
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
        foreach (var comment in node.Comments.Where(c => c.Placement == placement))
        {
            if (placement != CommentPlacement.Trailing) { CommentNode(comment); continue; }
            if (output.Length > 0 && output[^1] == '\n') output.Length--;
            output.Append(' ').Append(DocumentationWriter.Comment(comment));
            if (comment.Text.Length == 0 || comment.Text[^1] is not ('\r' or '\n')) output.Append('\n');
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
        output.Append(text);
        if (text[^1] is not ('\n' or '\r')) output.Append('\n');
    }
}
