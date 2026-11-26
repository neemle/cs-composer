using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

// Reads indentation from parsed source text so that edited regions reuse the layout around them
// instead of synthesizing whitespace from RenderOptions units.
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

    // Leading spaces and tabs of the line that holds the node's first token.
    internal static string LineIndent(SourceOrigin origin)
    {
        string text = origin.Snapshot.Text;
        int start = LineStart(text, origin.CoreStart), end = start;
        while (end < origin.CoreStart && text[end] is ' ' or '\t') end++;
        return text[start..end];
    }

    // Whitespace in front of the node's first token when nothing else precedes it on its line, else "".
    internal static string IndentText(SourceOrigin? origin)
    {
        if (origin == null) return "";
        string text = origin.Snapshot.Text;
        string prefix = text[LineStart(text, origin.CoreStart)..origin.CoreStart];
        return prefix.All(c => c is ' ' or '\t') ? prefix : "";
    }

    internal static bool StartsLine(SourceOrigin origin) =>
        LineStart(origin.Snapshot.Text, origin.Start) == origin.Start;

    // The text by which parsed children are indented past their container. Searched in the node
    // itself, then the enclosing containers outward, then the whole document in source order.
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

    static string? SubtreeUnit(CsNode node, SourceOrigin origin) => Unit(node, origin) ?? node.Children
        .Where(child => child.Origin != null).Select(child => SubtreeUnit(child, child.Origin!))
        .FirstOrDefault(unit => unit != null);

    static string? Unit(CsNode container, SourceOrigin origin)
    {
        if (origin.ListStart < 0) return null;
        string outer = LineIndent(origin);
        foreach (var child in List(container))
        {
            string inner = IndentText(child.Origin);
            if (inner.Length > outer.Length && inner.StartsWith(outer, StringComparison.Ordinal)) return inner[outer.Length..];
        }
        return null;
    }

    // Moves text to a new base indentation: the source base is replaced, inner relative whitespace is
    // kept, and lines that start inside a string literal or block comment are left untouched.
    internal static string Relocate(string text, SourceOrigin? origin, string target, string newline)
    {
        string old = IndentText(origin);
        var protectedSpans = Lexer.Scan(text).Where(t => t.Text.StartsWith('"') || t.Text.StartsWith('@') ||
            t.Text.StartsWith('$') || t.IsComment && t.Text.StartsWith("/*", StringComparison.Ordinal)).ToArray();
        var output = new StringBuilder();
        int position = 0;
        while (position < text.Length)
        {
            int lineEnd = text.IndexOf('\n', position);
            if (lineEnd < 0) lineEnd = text.Length;
            string line = text[position..lineEnd];
            if (!protectedSpans.Any(t => t.Start < position && t.End > position) && line.TrimEnd('\r').Length != 0)
                line = target + (line.StartsWith(old, StringComparison.Ordinal) ? line[old.Length..] : line);
            output.Append(line);
            if (lineEnd < text.Length) output.Append('\n');
            position = lineEnd + 1;
        }
        if (output.Length != 0 && output[^1] is not ('\r' or '\n')) output.Append(newline);
        return output.ToString();
    }
}
