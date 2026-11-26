using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal static class SourceLayout
{
    internal static IEnumerable<CsNode> List(CsNode node) => node switch
    {
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Members,
        CsBody b => b.Statements,
        CsStatement s => s.Sections,
        _ => Array.Empty<CsNode>()
    };

    internal static int LineStart(string text, int position)
    {
        while (position > 0 && text[position - 1] is not ('\r' or '\n')) position--;
        return position;
    }

    internal static string LineIndent(SourceOrigin origin)
    {
        string text = origin.Snapshot.Text;
        int start = LineStart(text, origin.CoreStart), end = start;
        while (end < origin.CoreStart && text[end] is ' ' or '\t') end++;
        return text[start..end];
    }

    internal static string IndentText(SourceOrigin? origin)
    {
        if (origin == null) return "";
        string text = origin.Snapshot.Text;
        string prefix = text[LineStart(text, origin.CoreStart)..origin.CoreStart];
        return prefix.All(c => c is ' ' or '\t') ? prefix : "";
    }

    internal static bool StartsLine(SourceOrigin origin) =>
        LineStart(origin.Snapshot.Text, origin.Start) == origin.Start;

    internal static string? InferUnit(SourceOrigin? self, LayoutScope? scope)
    {
        string? unit = self == null ? null : SubtreeUnit(self.Original, self);
        var root = self;
        for (var outer = scope; outer != null; outer = outer.Parent)
        {
            unit ??= Unit(outer.Origin.Original, outer.Origin);
            root = outer.Origin;
        }
        return unit ?? (root == null ? null : SubtreeUnit(root.Original, root));
    }

    // The first unit in pre-order, with an explicit stack (GENE-19 D10).
    static string? SubtreeUnit(CsNode root, SourceOrigin rootOrigin)
    {
        var stack = new Stack<(CsNode Node, SourceOrigin Origin)>();
        stack.Push((root, rootOrigin));
        while (stack.Count != 0)
        {
            var (node, origin) = stack.Pop();
            if (Unit(node, origin) is { } unit) return unit;
            var children = node.Children;
            for (int i = children.Count - 1; i >= 0; i--)
                if (children[i].Origin is { } childOrigin) stack.Push((children[i], childOrigin));
        }
        return null;
    }

    static string? Unit(CsNode container, SourceOrigin origin)
    {
        if (origin.ListStart < 0) return null;
        string outer = LineIndent(origin);
        foreach (var child in List(container))
        {
            string inner = IndentText(child.Origin);
            if (inner.Length > outer.Length && inner.StartsWith(outer, StringComparison.Ordinal))
                return inner[outer.Length..];
        }
        return null;
    }

    internal static string Relocate(string text, SourceOrigin? origin, string target, string newline)
    {
        string old = IndentText(origin);
        var protectedSpans = Lexer.Scan(text).Where(t => t.Text.StartsWith('"') || t.Text.StartsWith('@') ||
            t.Text.StartsWith('$') || t.IsComment && t.Text.StartsWith("/*", StringComparison.Ordinal)).ToArray();
        var output = new StringBuilder();
        int position = 0;
        while (position < text.Length)
        {
            int lineEnd = SourceNewline.NextBreak(text, position);
            if (lineEnd < 0) lineEnd = text.Length;
            string line = text[position..lineEnd], lineBreak = SourceNewline.At(text, lineEnd) ?? "";
            if (line.Length != 0 && !protectedSpans.Any(t => t.Start < position && t.End > position))
                line = target + (line.StartsWith(old, StringComparison.Ordinal) ? line[old.Length..] : line);
            output.Append(line).Append(lineBreak);
            position = lineEnd + Math.Max(1, lineBreak.Length);
        }
        if (output.Length != 0 && output[^1] is not ('\r' or '\n')) output.Append(newline);
        return output.ToString();
    }
}
