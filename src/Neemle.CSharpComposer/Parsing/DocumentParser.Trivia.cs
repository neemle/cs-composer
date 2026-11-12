using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    void ExtractSyntaxTrivia()
    {
        var excluded = new HashSet<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsDisabled)
            {
                var text = new CsDisabledText(token.Text);
                syntaxTrivia.Add(text with { SyntaxPosition = new(syntaxIdentity, token.Start, token.End), Origin = new(source, text, token.Start, token.End) { CoreStart = token.Start, CoreEnd = token.End } });
                excluded.Add(token.Start);
            }
            else if (token.Text.StartsWith('#') && source.Text[LineStart(token.Start)..token.Start].All(char.IsWhiteSpace))
            {
                p = i;
                try { syntaxTrivia.Add(Directive()); excluded.Add(token.Start); }
                catch (ParseFailure) { } // malformed directives retain normal diagnostic recovery
            }
        }
        foreach (var trivia in syntaxTrivia)
            foreach (var comment in trivia.Comments)
                if (comment.Origin is { } span) triviaComments.Add((span.CoreStart, span.CoreEnd));
        tokens.RemoveAll(t => excluded.Contains(t.Start));
        p = 0;
    }

    CsNode AttachSyntaxTrivia(CsNode node, bool includeOutside = false)
    {
        if (node.SyntaxPosition == null && node.Origin is { } location) node = node with { SyntaxPosition = new(syntaxIdentity, location.CoreStart, location.CoreEnd) };
        node = NodeRewrite.Children(node, child => AttachSyntaxTrivia(child));
        var origin = node.Origin;
        if (origin == null || !ReferenceEquals(origin.Snapshot, source)) return node;
        var owned = new List<CsNode>();
        foreach (var trivia in syntaxTrivia.ToArray())
        {
            var span = trivia.Origin!;
            if (!includeOutside && (span.CoreStart < origin.CoreStart || span.CoreEnd > origin.CoreEnd)) continue;
            owned.Add(trivia); syntaxTrivia.RemoveAt(syntaxTrivia.FindIndex(t => ReferenceEquals(t, trivia)));
        }
        if (owned.Count > 0)
        {
            // Preserve the existing list API at ordinary declaration/statement
            // boundaries. Only trivia embedded inside a construct is attached.
            var embedded = new List<CsSyntaxTrivia>();
            foreach (var trivia in owned)
            {
                var span = trivia.Origin!;
                bool inList = origin.ListStart >= 0 && span.CoreStart >= origin.ListStart && span.CoreEnd <= origin.ListEnd;
                if (inList && node is CsDocument document) node = document with { Items = Merge(document.Items, trivia) };
                else if (inList && node is CsNamespace space) node = space with { Items = Merge(space.Items, trivia) };
                else if (inList && node is CsTypeDeclaration type) node = type with { Members = new(Merge(type.Members.Cast<CsNode>(), trivia).Cast<CsMember>()) };
                else if (inList && node is CsBody { Expression: null } body)
                {
                    var statement = trivia is CsDirective directive ? new CsStatement(StatementKind.Directive) { Directive = directive with { Origin = null } } :
                        new CsStatement(StatementKind.DisabledText) { Disabled = (CsDisabledText)(trivia with { Origin = null }) };
                    statement = statement with { SyntaxPosition = trivia.SyntaxPosition, Origin = span with { Original = statement } };
                    node = body with { Statements = new(Merge(body.Statements.Cast<CsNode>(), statement).Cast<CsStatement>()) };
                }
                else
                {
                    var activeTokens = tokens.Where(t => t.Text != "" && t.Start >= origin.CoreStart && t.End <= origin.CoreEnd).ToArray();
                    var attachment = new CsSyntaxTrivia(trivia) { Anchor = new(new(activeTokens.Select(t => t.Text)), activeTokens.Count(t => t.End <= span.CoreStart)) };
                    embedded.Add(attachment);
                }
            }
            if (embedded.Count > 0) node = node with { SyntaxTrivia = new(node.SyntaxTrivia.Concat(embedded)) };
        }
        return node with { Origin = origin with { Original = node with { Origin = null } } };
    }

    static Values<CsNode> Merge(IEnumerable<CsNode> list, CsNode trivia) => new(list.Append(trivia).OrderBy(n => n.Origin?.CoreStart ?? int.MaxValue));
}
