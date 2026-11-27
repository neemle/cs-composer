using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    // A document or namespace item.
    void WriteNode(CsNode node)
    {
        switch (node)
        {
            case CsExternAlias alias: Commented(alias, () => Line(SyntaxTriviaWriter.Apply(alias, "extern alias " + Names.Id(alias.Name) + ";", inline: false))); break;
            case CsGlobalStatement statement: GlobalStatement(statement); break;
            case CsMember member: Member(member); break;
            case CsNamespace space: NamespaceNode(space); break;
            case CsRaw raw: Opaque(raw.Text); break;
            // Graph validation, which every render runs, admits only these kinds, attributes (which the document writes
            // itself) and usings as document and namespace items.
            default: Commented(node, () => Line(Using((CsUsing)node))); break;
        }
    }

    // An item written with its own leading and trailing comments (GENE-29 F4: a using's or an extern alias's comments
    // were written from source by the preserving renderer).
    void Commented(CsNode node, Action write)
    {
        Comments(node, CommentPlacement.Leading);
        write();
        Comments(node, CommentPlacement.Trailing);
    }

    // A top-level statement, as the statements its value lowers to, with its own comments (GENE-29 F4: the preserving
    // renderer wrote them from source; the guard refuses a render that loses them).
    void GlobalStatement(CsGlobalStatement statement) => Commented(statement, () => SwitchBody(SymbolModel.ToBody(Cs.Tree(statement.Value))));

    void NamespaceNode(CsNamespace space) => WithSyntaxTrivia(space, () => NamespaceCore(space));

    void NamespaceCore(CsNamespace space)
    {
        Comments(space, CommentPlacement.Leading);
        Line("namespace " + Names.Qualified(space.Name) + (space.IsFileScoped ? ";" : ""));
        if (!space.IsFileScoped) Open();
        // AD C3: one blank line after a file-scoped namespace line.
        else if (space.Items.Count != 0 || space.Comments.Any(c => c.Placement == CommentPlacement.Container)) Line();
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
