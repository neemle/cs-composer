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
        int start = output.Length;
        write();
        Dangling(node, start);
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
        // A trailing comment joins the end of its node's line, block comments first (GENE-29 F6, as in a list: a line
        // comment runs to its line end). After a trailing line comment, the next one starts its own line rather than
        // ending up inside it (GENE-27 D5), where it reads back on another owner, which the meaning guard refuses.
        bool afterLineComment = false;
        foreach (var comment in node.Comments.Where(c => c.Placement == placement).OrderBy(c => placement == CommentPlacement.Trailing && DocumentationWriter.Text(c).StartsWith("//", StringComparison.Ordinal)))
        {
            if (placement != CommentPlacement.Trailing) { CommentNode(comment); continue; }
            string text = DocumentationWriter.Comment(comment);
            if (afterLineComment) { Indent(); Write(text); }
            else
            {
                if (output.Length > 0 && output[^1] == '\n') output.Length--;
                // A document's trailing comment with nothing before it starts the output.
                Write(output.Length == 0 ? text : " " + text);
            }
            if (comment.Text.Length == 0 || comment.Text[^1] is not ('\r' or '\n')) output.Append('\n');
            afterLineComment = text.StartsWith("//", StringComparison.Ordinal);
        }
    }

    // GENE-29 F5 (AD B1): an item's, statement's or member's own container comments, which a parse finds before its final
    // `;` after a token no node ends at (`return /* c */;`, `using X /* c */;`): written there when the text written from
    // `start` ends with `;` (a line comment puts the `;` on the next line, one indent unit further), else on lines of their
    // own before that text, where they read back as its leading comments, which the meaning guard does not tell apart.
    void Dangling(CsNode node, int start)
    {
        // A type, an extension and a switch statement end their list with their container comments (Content).
        if (node is CsTypeDeclaration or CsExtension or CsStatement { Kind: StatementKind.Switch }) return;
        var comments = node.Comments.Where(c => c.Placement == CommentPlacement.Container).ToArray();
        if (comments.Length == 0) return;
        string written = output.ToString(start, output.Length - start);
        if (written.EndsWith(";\n", StringComparison.Ordinal) && !nested)
        {
            string indent = string.Concat(Enumerable.Repeat(options.Indent, depth));
            var text = new System.Text.StringBuilder();
            foreach (var comment in comments)
            {
                text.Append(' ').Append(Layout.Print(DocumentationWriter.Comment(comment), indent, options.Indent, 0, 0));
                if (comment.Kind != CommentKind.Block) text.Append('\n').Append(indent).Append(options.Indent);
            }
            output.Insert(output.Length - 2, text);
            return;
        }
        output.Length = start;
        foreach (var comment in comments) CommentNode(comment);
        output.Append(written);
    }

    // A comment on lines of its own, each at the current indentation (AD B4).
    void CommentNode(CsComment comment) => Opaque(DocumentationWriter.Comment(comment));

    // Opaque text and a comment's own text; an empty opaque item is not written at all (Content).
    void Opaque(string text)
    {
        Indent();
        Write(text);
        if (!text.EndsWith('\n') && !text.EndsWith('\r')) output.Append('\n');
    }

    // AD C3: `#region` and `#endregion` lines at the code's indentation, every other directive at column 0.
    void Directive(CsDirective directive)
    {
        if (directive.Kind is DirectiveKind.Region or DirectiveKind.EndRegion) Indent();
        output.Append(SyntaxTriviaWriter.Protect(directive, includeComments: false)).Append('\n');
    }
}
