using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    // Whether a line is a directive (GENE-29 F5: opaque text that starts with one must not start a line).
    internal static bool Directive(string line) =>
        new DocumentParser(new SourceSnapshot(line, System.Text.Encoding.UTF8, []), CsLanguageVersion.CSharp15, null, CancellationToken.None).syntaxTrivia.Count != 0;

    void ExtractSyntaxTrivia()
    {
        var excluded = new HashSet<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsDisabled)
            {
                syntaxTrivia.Add(new CsDisabledText(token.Text) { SyntaxPosition = new(syntaxIdentity, token.Start, token.End) });
                excluded.Add(token.Start);
            }
            else if (token.Text.StartsWith('#') && Blank(LineStart(token.Start), token.Start))
            {
                p = i;
                try { syntaxTrivia.Add(Directive()); excluded.Add(token.Start); }
                catch (ParseFailure) { } // malformed directives retain normal diagnostic recovery
            }
        }
        tokens.RemoveAll(t => excluded.Contains(t.Start));
        p = 0;
    }

    // Post-order over the whole result with an explicit stack (GENE-19 D10): a node takes the directives and inactive
    // text its span holds that no descendant took first. `outside`: the root of a fragment, which takes the rest, its
    // whole source being its list when `list` is set.
    CsNode AttachSyntaxTrivia(CsNode root, bool outside = false, bool list = false) =>
        Spine.Rewrite(root, _ => true, (original, node) => AttachTo(node, outside && ReferenceEquals(original, root), list && ReferenceEquals(original, root)));

    CsNode AttachTo(CsNode node, bool outside, bool root)
    {
        if (syntaxTrivia.Count == 0) return node;
        (int Start, int End) span;
        if (node is CsDocument || root) span = (0, source.Text.Length);
        else if (node.SyntaxPosition is { } at) span = (at.Offset, at.End);
        else return node;
        (int Start, int End) list = node is CsDocument || root ? span : lists.TryGetValue(span.Start, out var found) ? found : (-1, -1);
        var owned = new List<CsNode>();
        foreach (var trivia in syntaxTrivia.ToArray())
        {
            var position = trivia.SyntaxPosition!.Value;
            if (!outside && (position.Offset < span.Start || position.End > span.End)) continue;
            owned.Add(trivia); syntaxTrivia.RemoveAt(syntaxTrivia.FindIndex(t => ReferenceEquals(t, trivia)));
        }
        if (owned.Count == 0) return node;
        // Directives and inactive text between the items of a list are items of that list; any other one is attached to
        // the construct it stands in, anchored to the construct's tokens.
        var embedded = new List<CsSyntaxTrivia>();
        foreach (var trivia in owned)
        {
            var position = trivia.SyntaxPosition!.Value;
            bool inList = list.Start >= 0 && position.Offset >= list.Start && position.End <= list.End;
            if (inList && node is CsDocument document) node = document with { Items = Merge(document.Items, trivia) };
            else if (inList && node is CsNamespace space) node = space with { Items = Merge(space.Items, trivia) };
            else if (inList && node is CsTypeDeclaration type) node = type with { Members = new(Merge(type.Members.Cast<CsNode>(), trivia).Cast<CsMember>()) };
            else if (inList && node is CsBody { Expression: null } body)
            {
                var statement = trivia is CsDirective directive ? new CsStatement(StatementKind.Directive) { Directive = directive } :
                    new CsStatement(StatementKind.DisabledText) { Disabled = (CsDisabledText)trivia };
                node = body with { Statements = new(Merge(body.Statements.Cast<CsNode>(), statement with { SyntaxPosition = position }).Cast<CsStatement>()) };
            }
            else
            {
                var activeTokens = tokens.Where(t => t.Text != "" && t.Start >= span.Start && t.End <= span.End).ToArray();
                embedded.Add(new CsSyntaxTrivia(trivia) { Anchor = new(new(activeTokens.Select(t => t.Text)), activeTokens.Count(t => t.End <= position.Offset)) });
            }
        }
        return embedded.Count == 0 ? node : node with { SyntaxTrivia = new(node.SyntaxTrivia.Concat(embedded)) };
    }

    // Every parsed list item and every trivia node is bound to its source span. A directive or inactive text item counts as
    // a significant token for the ownership pass (a comment before it is its leading comment).
    Values<CsNode> Merge(IEnumerable<CsNode> list, CsNode trivia)
    {
        marks.Add(trivia.SyntaxPosition!.Value.Offset);
        return new(list.Append(trivia).OrderBy(n => n.SyntaxPosition!.Value.Offset));
    }

    // The starts of the directive and inactive text items.
    readonly List<int> marks = new();
}
