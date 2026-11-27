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
        bool statementList = false) =>
        node is CsExpression expression && Spine.Next(expression) != null ? Chain(expression, options, version, scope) :
            Single(node, options, version, scope, statementList, -1, null, null).Text!;

    // One node: its finished text, or, when the child at `hole` is patched in, the text before and after that
    // child and the layout scope the child renders in. `same` is a precomputed "equals its original" and
    // `formatted` a precomputed "holds formatting anywhere in its subtree".
    static Step Single(CsNode node, RenderOptions options, CsLanguageVersion version, LayoutScope? scope,
        bool statementList, int hole, bool? same, bool? formatted)
    {
        var origin = node.Origin;
        if (origin == null || node.Formatting != null) return new(Scoped(node, options, version, scope, statementList));
        if ((same ?? node.Equals(origin.Original)) && !(formatted ?? HasFormatting(node, -1)))
            return new(origin.Snapshot.Text[origin.Start..origin.End]);
        var patches = BindingPatches(node, origin);
        var list = SourceLayout.List(node).ToArray();
        bool listChanged = origin.ListStart >= 0 && !list.Select(n => n.Origin)
            .SequenceEqual(SourceLayout.List(origin.Original).Select(n => n.Origin));
        if (!listChanged && UnboundChange(node, origin.Original))
            return new(Fresh(node, options, version, scope, statementList));
        var inner = new LayoutScope(origin, scope);
        int holeAt = -1;
        if (listChanged)
            patches.Add((origin.ListStart, origin.ListEnd, new ListRegion(node, list, inner,
                child => Node(child, options, version, inner), options.Indent).Text()));
        else
        {
            var children = node.Children;
            for (int index = 0; index < children.Count; index++)
            {
                var child = children[index];
                if (child.Origin is not { } o) continue;
                if (!ReferenceEquals(o.Snapshot, origin.Snapshot) || o.Start < origin.Start || o.End > origin.End)
                    continue;
                if (index == hole) { holeAt = patches.Count; patches.Add((o.Start, o.End, "")); continue; }
                bool statements = node is CsSwitchSection && child is CsBody || node is CsGlobalStatement;
                patches.Add((o.Start, o.End, Node(child, options, version, inner, statements)));
            }
        }
        if (patches.Count == 0) return new(Fresh(node, options, version, scope, statementList));
        if (holeAt >= 0 && Split(origin, patches, holeAt) is { } split) return new(null, split.Before, split.After, inner);
        return new(Apply(origin, patches));
    }

    sealed record Step(string? Text, string Before = "", string After = "", LayoutScope? Scope = null);

    // A chain spine (GENE-19 D10) renders in a loop: each link that patches its receiver into its own source
    // contributes the text before and after the receiver, and the first link that renders whole (unchanged,
    // fresh or formatted) ends the loop. Whether each link equals its original, and whether its subtree holds
    // formatting, are decided bottom-up once, so an edit or a Reformat deep in a long chain is not compared or
    // searched for again at every link above it.
    static string Chain(CsExpression top, RenderOptions options, CsLanguageVersion version, LayoutScope? scope)
    {
        var links = new List<CsExpression>();
        for (var link = top; link != null; link = Spine.Next(link)) links.Add(link);
        var same = new bool[links.Count];
        for (int i = links.Count - 1; i >= 0; i--)
            same[i] = SameAsOriginal(links[i], i + 1 < links.Count ? links[i + 1] : null, i + 1 < links.Count && same[i + 1]);
        // A link's subtree holds formatting when its receiver's does or when the link itself, or one of its
        // other children's subtrees, does; the receiver is skipped, so each node is visited once.
        var formatted = new bool[links.Count];
        for (int i = links.Count - 1; i >= 0; i--)
            formatted[i] = i + 1 < links.Count && formatted[i + 1] || HasFormatting(links[i], i + 1 < links.Count ? Spine.Index(links[i]) : -1);
        var output = new System.Text.StringBuilder();
        var suffixes = new Stack<string>();
        for (int i = 0; i < links.Count; i++)
        {
            var step = Single(links[i], options, version, scope, false, i + 1 < links.Count ? Spine.Index(links[i]) : -1, same[i], formatted[i]);
            if (step.Text != null) { output.Append(step.Text); break; }
            output.Append(step.Before);
            suffixes.Push(step.After);
            scope = step.Scope;
        }
        while (suffixes.Count != 0) output.Append(suffixes.Pop());
        return output.ToString();
    }

    // Whether a link equals its original, given whether the next link (its receiver) equals its own. A parsed
    // original's receiver carries the origin whose original it equals, so the receiver comparison reuses the
    // next link's answer when both share that origin.
    static bool SameAsOriginal(CsExpression link, CsExpression? next, bool nextSame)
    {
        if (link.Origin is not { } origin) return false;
        // An expression's origin always records an expression (the parsed one, or the lowering of the symbol it was
        // parsed as), and a link that equals it has its argument count, which for a parsed link is at least one.
        var original = (CsExpression)origin.Original;
        if (!link.LinkEquals(original)) return false;
        var receiver = link.Arguments[0];
        var before = original.Arguments[0];
        if (ReferenceEquals(receiver, before)) return true;
        if (next != null && ReferenceEquals(receiver, next) && next.Origin != null && ReferenceEquals(before.Origin, next.Origin)) return nextSame;
        return EqualityComparer<CsExpression>.Default.Equals(receiver, before);
    }

    // The text Apply produces around the patch at `hole`, or null when Apply would skip that patch.
    static (string Before, string After)? Split(SourceOrigin origin, List<(int Start, int End, string Text)> patches, int hole)
    {
        var applied = new List<int>();
        int boundary = origin.End;
        foreach (int index in Enumerable.Range(0, patches.Count).OrderByDescending(i => patches[i].Start))
        {
            if (patches[index].End > boundary) continue;
            applied.Add(index);
            boundary = patches[index].Start;
        }
        if (!applied.Contains(hole)) return null;
        string text = origin.Snapshot.Text;
        var before = new System.Text.StringBuilder();
        var after = new System.Text.StringBuilder();
        var current = before;
        int position = origin.Start;
        for (int k = applied.Count - 1; k >= 0; k--)
        {
            var patch = patches[applied[k]];
            current.Append(text, position, patch.Start - position);
            if (applied[k] == hole) current = after;
            else current.Append(patch.Text);
            position = patch.End;
        }
        current.Append(text, position, origin.End - position);
        return (before.ToString(), after.ToString());
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
        return Fit(Renderer.Node(node, format, version, SourceLayout.LineIndent(origin), statementList), origin, scope, node);
    }

    static string Fresh(CsNode node, RenderOptions options, CsLanguageVersion version, LayoutScope? scope,
        bool statementList)
    {
        var origin = node.Origin!;
        var format = options with { Indent = SourceLayout.InferUnit(origin, scope) ?? options.Indent };
        return Fit(Renderer.Node(node, format, version, SourceLayout.LineIndent(origin), statementList), origin, scope, node);
    }

    static string Fit(string rendered, SourceOrigin origin, LayoutScope? scope, CsNode node)
    {
        string text = origin.Snapshot.Text;
        string newline = scope == null ? SourceNewline.Dominant(text) : SourceNewline.Destination(origin);
        // The line breaks inside a re-rendered documentation comment are the writer's own (escaped XML has no raw
        // CR), so they take the source newline too; other comment text keeps its own line breaks.
        rendered = node is CsComment { Documentation: not null } ? SourceNewline.ConvertAll(rendered, newline) :
            SourceNewline.Convert(rendered, newline);
        if (scope == null) return rendered;
        string indent = SourceLayout.LineIndent(origin);
        if (!SourceLayout.StartsLine(origin) && rendered.StartsWith(indent, StringComparison.Ordinal))
            rendered = rendered[indent.Length..];
        string envelope = origin.Snapshot.Text[origin.Start..origin.End];
        return envelope.Length != 0 && envelope[^1] is not ('\r' or '\n') ? rendered.TrimEnd('\r', '\n') : rendered;
    }

    // Whether `node` or any node below it holds formatting, leaving out the subtree of its child at `skip`.
    static bool HasFormatting(CsNode node, int skip)
    {
        if (node.Formatting != null) return true;
        var stack = new Stack<CsNode>();
        var children = node.Children;
        for (int i = 0; i < children.Count; i++) if (i != skip) stack.Push(children[i]);
        while (stack.Count != 0)
        {
            var current = stack.Pop();
            if (current.Formatting != null) return true;
            foreach (var child in current.Children) stack.Push(child);
        }
        return false;
    }

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
