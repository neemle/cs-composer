using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Rendering;
internal static class PreservedRenderer
{
    internal static string Render(CsDocument document, RenderOptions options)
    {
        var errors = LanguageFeatures.RenderErrors(document, options);
        if (errors.Count != 0) throw new GenValidationException(errors);
        return document.Origin == null ? Renderer.Render(document, options) : Node(document, options, document.LanguageVersion);
    }
    static string Node(CsNode node, RenderOptions options, CsLanguageVersion version)
    {
        var origin = node.Origin;
        if (origin == null || node.Formatting != null)
            return Renderer.Node(node, node.Formatting ?? options, version, Indent(origin, options));
        if (node.Equals(origin.Original) && !HasFormatting(node))
            return origin.Snapshot.Text[origin.Start..origin.End];
        var patches = new List<(int Start, int End, string Text)>();
        foreach (var binding in origin.Bindings)
        {
            string? value = Scalar(node, binding.Property);
            if (value != null && value != binding.Original)
                patches.Add((binding.Start, binding.End, binding.Property == "Name" ? Names.Id(value) : value));
        }
        var list = List(node);
        var oldList = List(origin.Original);
        bool listChanged = origin.ListStart >= 0 && !list.Select(n => n.Origin)
            .SequenceEqual(oldList.Select(n => n.Origin));
        if (!listChanged && UnboundChange(node, origin.Original))
            return Renderer.Node(node, options, version, Indent(origin, options));
        if (listChanged)
        {
            string original = origin.Snapshot.Text[origin.ListStart..origin.ListEnd];
            string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            int indent = Indent(origin, options) + (node is CsDocument || node is CsNamespace { IsFileScoped: true } ? 0 : 1);
            string prefix = origin.ListStart == 0 ? "" : newline;
            string suffix = origin.ListEnd == origin.Snapshot.Text.Length ? "" : new string(' ', Indent(origin, options) * options.Indent.Length);
            string items = string.Join("", list.Select(child => Relocate(Node(child, options, version), child.Origin, indent, options, newline)));
            patches.Add((origin.ListStart, origin.ListEnd, prefix + items + suffix));
        }
        else
        {
            foreach (var child in node.Children.Where(c => c.Origin != null && c is not CsComment))
            {
                var o = child.Origin!;
                if (o.Start < origin.Start || o.End > origin.End) continue;
                patches.Add((o.Start, o.End, Node(child, options, version)));
            }
        }
        if (patches.Count == 0) return Renderer.Node(node, options, version, Indent(origin, options));
        string text = origin.Snapshot.Text[origin.Start..origin.End];
        int boundary = origin.End;
        foreach (var patch in patches.OrderByDescending(t => t.Start))
        {
            if (patch.End > boundary) continue;
            text = text[..(patch.Start - origin.Start)] + patch.Text + text[(patch.End - origin.Start)..];
            boundary = patch.Start;
        }
        return text;
    }
    static bool HasFormatting(CsNode node) => node.Formatting != null || node.Children.Any(HasFormatting);
    static bool UnboundChange(CsNode node, CsNode original)
    {
        var before = original.Children;
        var after = node.Children;
        if (before.Count != after.Count) return true;
        for (int i = 0; i < after.Count; i++)
            if (after[i].Origin == null && !after[i].Equals(before[i])) return true;
        int index = 0;
        var shallow = NodeRewrite.Children(node, _ => before[index++]);
        shallow = (shallow, original) switch
        {
            (CsMember a, CsMember b) => a with { Name = b.Name },
            (CsType a, CsType b) => a with { Name = b.Name },
            (CsExpression a, CsExpression b) => a with { Text = b.Text },
            (CsStatement a, CsStatement b) => a with { Text = b.Text },
            (CsParameter a, CsParameter b) => a with { Name = b.Name },
            (TypeParameter a, TypeParameter b) => a with { Name = b.Name },
            (CsNamespace a, CsNamespace b) => a with { Name = b.Name },
            (CsUsing a, CsUsing b) => a with { Name = b.Name },
            _ => shallow
        };
        return !shallow.Equals(original);
    }
    static string? Scalar(CsNode node, string property) => (node, property) switch
    {
        (CsMember m, "Name") => m.Name, (CsType t, "Name") => t.Name,
        (CsParameter p, "Name") => p.Name, (TypeParameter p, "Name") => p.Name,
        (CsNamespace n, "Name") => n.Name, (CsUsing u, "Name") => u.Name,
        (CsExpression e, "Text") => e.Text, (CsStatement s, "Text") => s.Text,
        _ => null
    };
    static IEnumerable<CsNode> List(CsNode node) => node switch
    {
        CsDocument d => d.Items, CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Members, CsBody b => b.Statements,
        _ => Array.Empty<CsNode>()
    };
    static int Indent(SourceOrigin? origin, RenderOptions options)
    {
        if (origin == null || options.Indent.Length == 0) return 0;
        int start = origin.CoreStart;
        while (start > 0 && origin.Snapshot.Text[start - 1] is not ('\n' or '\r')) start--;
        int length = 0;
        while (start + length < origin.CoreStart && origin.Snapshot.Text[start + length] is ' ' or '\t') length++;
        return length / options.Indent.Length;
    }
    static string Relocate(string text, SourceOrigin? origin, int indent, RenderOptions options, string newline)
    {
        string old = new(' ', Indent(origin, options) * options.Indent.Length);
        string target = string.Concat(Enumerable.Repeat(options.Indent, indent));
        var protectedSpans = Lexer.Scan(text).Where(t => t.Text.StartsWith('"') || t.Text.StartsWith('@') ||
            t.Text.StartsWith('$') || t.IsComment && t.Text.StartsWith("/*", StringComparison.Ordinal)).ToArray();
        var output = new System.Text.StringBuilder();
        int position = 0;
        while (position < text.Length)
        {
            int lineEnd = text.IndexOf('\n', position);
            if (lineEnd < 0) lineEnd = text.Length;
            bool literal = protectedSpans.Any(t => t.Start < position && t.End > position);
            string line = text[position..lineEnd];
            if (!literal) { if (line.StartsWith(old, StringComparison.Ordinal)) line = line[old.Length..]; output.Append(target); }
            output.Append(line);
            if (lineEnd < text.Length) output.Append('\n');
            position = lineEnd + 1;
        }
        if (output.Length != 0 && output[^1] is not ('\r' or '\n')) output.Append(newline);
        return output.ToString();
    }
}
