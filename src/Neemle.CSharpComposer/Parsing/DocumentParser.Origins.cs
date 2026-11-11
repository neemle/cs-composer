using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
internal sealed partial class DocumentParser
{
    T Bind<T>(T node, int start, string? property = null, int tokenStart = -1, int tokenLength = 0,
        int listStart = -1, int listEnd = -1) where T : CsNode
    {
        int coreStart = tokens[start].Start, coreEnd = Last.End;
        int envelopeStart = LeadingStart(coreStart);
        int envelopeEnd = TrailingEnd(coreEnd);
        var comments = all.Where(t => t.IsComment && t.Start >= envelopeStart && t.End <= envelopeEnd)
            .Where(t => t.End <= coreStart || t.Start >= coreEnd)
            .Select(t => Comment(t, t.Start >= coreEnd ? CommentPlacement.Trailing : CommentPlacement.Leading));
        node = (T)(node with { Comments = new(comments) });
        var bindings = property == null || tokenStart < 0 ? Values<TokenBinding>.Empty :
            new Values<TokenBinding>(new TokenBinding(property, tokenStart, tokenStart + tokenLength,
                source.Text.Substring(tokenStart, tokenLength)));
        return (T)(node with { Origin = new(source, node, envelopeStart, envelopeEnd)
            { CoreStart = coreStart, CoreEnd = coreEnd, NameStart = tokenStart, NameLength = tokenLength,
                ListStart = listStart, ListEnd = listEnd, Bindings = bindings } });
    }
    int LeadingStart(int start)
    {
        int line = LineStart(start);
        int result = source.Text[line..start].All(char.IsWhiteSpace) ? line : start;
        foreach (var comment in all.Where(t => t.IsComment && t.End <= start).Reverse())
        {
            string between = source.Text[comment.End..result];
            if (!between.All(char.IsWhiteSpace) || CountLines(between) > 1) break;
            int beginning = LineStart(comment.Start);
            if (!source.Text[beginning..comment.Start].All(char.IsWhiteSpace)) break;
            result = beginning;
        }
        return result;
    }
    int TrailingEnd(int end)
    {
        int result = end;
        foreach (var comment in all.Where(t => t.IsComment && t.Start >= end))
        {
            string between = source.Text[result..comment.Start];
            if (!between.All(char.IsWhiteSpace) || CountLines(between) != 0) break;
            result = comment.End;
        }
        int p = result;
        while (p < source.Text.Length && source.Text[p] is ' ' or '\t') p++;
        if (p < source.Text.Length && source.Text[p] == '\r') p++;
        if (p < source.Text.Length && source.Text[p] == '\n') p++;
        return p > result && source.Text[result..p].Any(c => c is '\r' or '\n') ? p : result;
    }
    int LineStart(int p)
    {
        while (p > 0 && source.Text[p - 1] is not ('\r' or '\n')) p--;
        return p;
    }
    static int CountLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Count(c => c is '\r' or '\n');
    static CsComment Comment(Token token, CommentPlacement placement) => new(token.Text,
        token.Text.StartsWith("///", StringComparison.Ordinal) || token.Text.StartsWith("/**", StringComparison.Ordinal)
            ? CommentKind.Documentation : token.Text.StartsWith("/*", StringComparison.Ordinal)
                ? CommentKind.Block : CommentKind.Line, placement);
    Values<CsComment> ContainerComments(int start, int end, IEnumerable<CsNode> children)
    {
        var occupied = children.Select(c => c.Origin).Where(o => o != null).ToArray();
        return new(all.Where(t => t.IsComment && t.Start >= start && t.End <= end &&
            !occupied.Any(o => t.Start >= o!.Start && t.End <= o.End))
            .Select(t => Comment(t, CommentPlacement.Container)));
    }
    CsRaw RecoverNode(string message, bool malformed = true)
    {
        int start = p, depth = 0;
        do
        {
            string text = Current.Text;
            if (text.Length == 0) break;
            if (depth == 0 && text == "}") break;
            p++;
            if (text.StartsWith('#')) break;
            if (text == "{") depth++;
            if (text == "}") { depth--; if (depth == 0) { Take(";"); break; } }
            if (depth == 0 && text == ";") break;
        } while (Current.Text.Length != 0);
        if (p == start && Current.Text.Length != 0) p++;
        int end = p == start ? Current.Start : Last.End;
        Error(malformed ? "PARSE001" : "PARSE002", message, tokens[start].Start, end - tokens[start].Start);
        return Bind(new CsRaw(source.Text[tokens[start].Start..end]), start);
    }
}
