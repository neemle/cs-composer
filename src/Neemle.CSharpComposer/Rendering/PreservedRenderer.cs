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


    internal static string RenderStatements(CsBody body, RenderOptions options, CsLanguageVersion version) =>
        Node(body, options, version, statementList: true);

    static string Node(CsNode node, RenderOptions options, CsLanguageVersion version, bool statementList = false)
    {
        var origin = node.Origin;
        if (origin == null || node.Formatting != null)
            return Renderer.Node(node, node.Formatting ?? options, version, Indent(origin, options), statementList);
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
            return Fresh(node, options, version, origin, statementList);
        if (listChanged)
        {
            string original = origin.Snapshot.Text[origin.ListStart..origin.ListEnd];
            string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            int indent = Indent(origin, options) + (node is CsDocument || node is CsNamespace { IsFileScoped: true } ? 0 : 1);
            string prefix = origin.ListStart == 0 ? "" : newline;
            string suffix = origin.ListEnd == origin.Snapshot.Text.Length ? "" : new string(' ', Indent(origin, options) * options.Indent.Length);
            var originalNodes = oldList.ToArray();
            string destinationIndent = originalNodes.Where(n => n.Origin != null).Select(n => IndentText(n.Origin!))
                .FirstOrDefault(s => s.Length > IndentText(origin).Length) ??
                (node is CsDocument || node is CsNamespace { IsFileScoped: true } ? "" : IndentText(origin) + options.Indent);
            string items = ListText(node, list.ToArray(), originalNodes, options, version, destinationIndent, newline);
            patches.Add((origin.ListStart, origin.ListEnd, prefix + items + suffix));
        }
        else
        {
            foreach (var child in node.Children.Where(c => c.Origin != null && c is not CsComment))
            {
                var o = child.Origin!;
                if (!ReferenceEquals(o.Snapshot, origin.Snapshot) || o.Start < origin.Start || o.End > origin.End) continue;
                patches.Add((o.Start, o.End, Node(child, options, version, node is CsSwitchSection && child is CsBody)));
            }
        }
        if (patches.Count == 0) return Fresh(node, options, version, origin, statementList);
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

    static string Fresh(CsNode node, RenderOptions options, CsLanguageVersion version, SourceOrigin? origin, bool statementList)
    {
        string rendered = Renderer.Node(node, options, version, Indent(origin, options), statementList);
        if (origin == null || !origin.Snapshot.Text[origin.Start..origin.End].Contains("\r\n", StringComparison.Ordinal)) return rendered;
        var output = new System.Text.StringBuilder();
        int position = 0;
        foreach (var token in Lexer.Scan(rendered))
        {
            // Only whitespace changes newline style; multiline literal/comment
            // contents are data and must remain byte-for-byte unchanged.
            output.Append(rendered[position..token.Start].Replace("\r\n", "\n").Replace("\n", "\r\n"));
            output.Append(token.Text.StartsWith('#') ? token.Text.Replace("\r\n", "\n").Replace("\n", "\r\n") : token.Text);
            position = token.End;
        }
        return output.ToString();
    }

    static string ListText(CsNode owner, CsNode[] nodes, CsNode[] original, RenderOptions options,
        CsLanguageVersion version, string indent, string newline)
    {
        var output = new System.Text.StringBuilder();
        var comments = owner.Comments.Where(c => c.Placement == CommentPlacement.Container)
            .Select(c => (Node: c, Index: c.Origin == null ? 0 : original.Count(n =>
                n.Origin != null && n.Origin.CoreStart < c.Origin.CoreStart))).ToArray();
        for (int i = 0; i <= nodes.Length; i++)
        {
            foreach (var comment in comments.Where(c => Math.Min(c.Index, nodes.Length) == i))
                output.Append(Relocate(Node(comment.Node, options, version), comment.Node.Origin, indent, newline));
            if (i < nodes.Length)
            {
                string text = Node(nodes[i], options, version);
                bool retained = nodes[i].Origin != null && original.Any(old => ReferenceEquals(old.Origin, nodes[i].Origin));
                output.Append(retained ? text : Relocate(text, nodes[i].Origin, indent, newline));
            }
        }
        return output.ToString();
    }

    static bool HasFormatting(CsNode node) => node.Formatting != null || node.Children.Any(HasFormatting);

    static bool UnboundChange(CsNode node, CsNode original)
    {
        var before = original.Children;
        var after = node.Children;
        if (before.Count != after.Count) return true;
        for (int i = 0; i < after.Count; i++)
            if (!ReferenceEquals(after[i].Origin, before[i].Origin) ||
                after[i].Origin == null && !after[i].Equals(before[i])) return true;
        int index = 0;
        var shallow = NodeRewrite.Children(node, _ => before[index++]);
        bool nameBound = node.Origin!.Bindings.Any(b => b.Property == "Name");
        bool textBound = node.Origin.Bindings.Any(b => b.Property == "Text");
        shallow = (shallow, original) switch
        {
            (CsMember a, CsMember b) when nameBound => a with { Name = b.Name },
            (CsType a, CsType b) when nameBound => a with { Name = b.Name },
            (CsExpression a, CsExpression b) when textBound => a with { Text = b.Text },
            (CsStatement a, CsStatement b) when textBound => a with { Text = b.Text },
            (CsLambdaParameter a, CsLambdaParameter b) when nameBound => a with { Name = b.Name },
            (CsVariableDeclarator a, CsVariableDeclarator b) when nameBound => a with { Name = b.Name },
            (CsSymbol a, CsSymbol b) when nameBound => a with { Name = b.Name },
            (CsPattern a, CsPattern b) when nameBound => a with { Name = b.Name },
            (CsParameter a, CsParameter b) when nameBound => a with { Name = b.Name },
            (TypeParameter a, TypeParameter b) when nameBound => a with { Name = b.Name },
            (CsNamespace a, CsNamespace b) when nameBound => a with { Name = b.Name },
            (CsUsing a, CsUsing b) when nameBound => a with { Name = b.Name },
            _ => shallow
        };
        return !shallow.Equals(original);
    }

    static string? Scalar(CsNode node, string property) => (node, property) switch
    {
        (CsMember m, "Name") => m.Name,
        (CsType t, "Name") => t.Name,
        (CsParameter p, "Name") => p.Name,
        (TypeParameter p, "Name") => p.Name,
        (CsNamespace n, "Name") => n.Name,
        (CsUsing u, "Name") => u.Name,
        (CsLambdaParameter p, "Name") => p.Name,
        (CsVariableDeclarator v, "Name") => v.Name,
        (CsSymbol s, "Name") => s.Name == null ? null : Names.Id(s.Name),
        (CsPattern p, "Name") => p.Name,
        (CsExpression e, "Text") => e.Text,
        (CsStatement s, "Text") => s.Text,
        _ => null
    };

    static IEnumerable<CsNode> List(CsNode node) => node switch
    {
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Members,
        CsBody b => b.Statements,
        CsStatement s => s.Sections,
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

    static string IndentText(SourceOrigin? origin)
    {
        if (origin == null) return "";
        int start = origin.CoreStart;
        while (start > 0 && origin.Snapshot.Text[start - 1] is not ('\r' or '\n')) start--;
        string prefix = origin.Snapshot.Text[start..origin.CoreStart];
        return prefix.All(c => c is ' ' or '\t') ? prefix : "";
    }

    static string Relocate(string text, SourceOrigin? origin, string target, string newline)
    {
        string old = IndentText(origin);
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
