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

    static string Node(CsNode node, RenderOptions options, CsLanguageVersion version, LayoutScope? scope = null,
        bool statementList = false)
    {
        var origin = node.Origin;
        if (origin == null || node.Formatting != null) return Scoped(node, options, version, scope, statementList);
        if (node.Equals(origin.Original) && !HasFormatting(node))
            return origin.Snapshot.Text[origin.Start..origin.End];
        var patches = BindingPatches(node, origin);
        var list = SourceLayout.List(node).ToArray();
        bool listChanged = origin.ListStart >= 0 && !list.Select(n => n.Origin)
            .SequenceEqual(SourceLayout.List(origin.Original).Select(n => n.Origin));
        if (!listChanged && UnboundChange(node, origin.Original))
            return Fresh(node, options, version, scope, statementList);
        var inner = new LayoutScope(origin, scope);
        if (listChanged)
            patches.Add((origin.ListStart, origin.ListEnd, new ListRegion(node, list, inner,
                child => Node(child, options, version, inner), options.Indent).Text()));
        else
            foreach (var child in node.Children.Where(c => c.Origin != null && c is not CsComment))
            {
                var o = child.Origin!;
                if (!ReferenceEquals(o.Snapshot, origin.Snapshot) || o.Start < origin.Start || o.End > origin.End)
                    continue;
                bool sectionBody = node is CsSwitchSection && child is CsBody;
                patches.Add((o.Start, o.End, Node(child, options, version, inner, sectionBody)));
            }
        return patches.Count == 0 ? Fresh(node, options, version, scope, statementList) : Apply(origin, patches);
    }

    static List<(int Start, int End, string Text)> BindingPatches(CsNode node, SourceOrigin origin)
    {
        var patches = new List<(int Start, int End, string Text)>();
        foreach (var binding in origin.Bindings)
        {
            string? value = Scalar(node, binding.Property);
            if (value != null && value != binding.Original)
                patches.Add((binding.Start, binding.End, binding.Property == "Name" ? Names.Id(value) : value));
        }
        return patches;
    }

    static string Apply(SourceOrigin origin, List<(int Start, int End, string Text)> patches)
    {
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

    static string Scoped(CsNode node, RenderOptions options, CsLanguageVersion version, LayoutScope? scope,
        bool statementList)
    {
        var origin = node.Origin;
        var format = node.Formatting ?? options with { Indent = SourceLayout.InferUnit(null, scope) ?? options.Indent };
        if (origin == null) return Renderer.Node(node, format, version, "", statementList);
        return Fit(Renderer.Node(node, format, version, SourceLayout.LineIndent(origin), statementList), origin, scope);
    }

    static string Fresh(CsNode node, RenderOptions options, CsLanguageVersion version, LayoutScope? scope,
        bool statementList)
    {
        var origin = node.Origin!;
        var format = options with { Indent = SourceLayout.InferUnit(origin, scope) ?? options.Indent };
        return Fit(Renderer.Node(node, format, version, SourceLayout.LineIndent(origin), statementList), origin, scope);
    }

    static string Fit(string rendered, SourceOrigin origin, LayoutScope? scope)
    {
        string text = origin.Snapshot.Text;
        rendered = SourceNewline.Convert(rendered, scope == null ? SourceNewline.Dominant(text) :
            SourceNewline.Destination(origin));
        if (scope == null) return rendered;
        string indent = SourceLayout.LineIndent(origin);
        if (!SourceLayout.StartsLine(origin) && rendered.StartsWith(indent, StringComparison.Ordinal))
            rendered = rendered[indent.Length..];
        string envelope = origin.Snapshot.Text[origin.Start..origin.End];
        return envelope.Length != 0 && envelope[^1] is not ('\r' or '\n') ? rendered.TrimEnd('\r', '\n') : rendered;
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
}
