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
        // An unchecked render (Validate = false) can still meet a symbol the lowering refuses; that refusal is
        // reported as the public validation exception, never as the internal one (GENE-27 D9).
        try { return document.Origin == null ? Renderer.Render(document, options) : Node(document, options, document.LanguageVersion); }
        catch (SymbolModel.InvalidModel error) { throw new GenValidationException(new[] { error.Diagnostic }); }
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
        // A type parameter's source holds its name, variance and attributes; its constraints are written in the
        // owner's where clause, which the owner patches (ConstraintPatches), so they take no part here, on either
        // side of the comparison. Only a parsed owner's child reaches here as a type parameter, so it has an origin.
        if (node is TypeParameter parameter)
            node = parameter with
            {
                Constraints = Values<CsConstraint>.Empty,
                Origin = parameter.Origin! with { Original = (TypeParameter)parameter.Origin.Original with { Constraints = Values<CsConstraint>.Empty } }
            };
        var origin = node.Origin;
        if (origin == null || node.Formatting != null) return new(Scoped(node, options, version, scope, statementList));
        if ((same ?? node.Equals(origin.Original)) && !(formatted ?? HasFormatting(node, -1)))
            return new(origin.Snapshot.Text[origin.Start..origin.End]);
        var patches = BindingPatches(node, origin);
        if (!ConstraintPatches(node, origin, patches)) return new(Fresh(node, origin, options, version, scope, statementList));
        var list = SourceLayout.List(node).ToArray();
        bool listChanged = origin.ListStart >= 0 && !list.Select(n => n.Origin)
            .SequenceEqual(SourceLayout.List(origin.Original).Select(n => n.Origin));
        var spliced = Array.Empty<int>();
        if (!listChanged && UnboundChange(node, origin.Original))
        {
            // GENE-27 D1/D6: newly built children replace only their own source text; anything else renders the
            // node fresh, and a changed chain link renders fresh around its receiver, which keeps its source.
            if (Splices(node, origin) is { } found) spliced = found;
            else if (hole >= 0 && AroundReceiver((CsExpression)node, origin, options, version, scope) is { } around) return around;
            else return new(Fresh(node, origin, options, version, scope, statementList));
        }
        var inner = new LayoutScope(origin, scope);
        int holeAt = -1;
        if (listChanged)
            patches.Add((origin.ListStart, origin.ListEnd, new ListRegion(node, list, inner,
                child => Node(child, options, version, inner), options.Indent).Text()));
        else
        {
            var children = node.Children;
            var before = origin.Original.Children;
            for (int index = 0; index < children.Count; index++)
            {
                var child = children[index];
                bool splice = Array.IndexOf(spliced, index) >= 0;
                if ((splice ? before[index].Origin : child.Origin) is not { } o) continue;
                if (!ReferenceEquals(o.Snapshot, origin.Snapshot) || o.Start < origin.Start || o.End > origin.End)
                    continue;
                bool statements = node is CsSwitchSection && child is CsBody || node is CsGlobalStatement;
                if (splice) { patches.Add((o.Start, o.End, Fresh(child, o, options, version, inner, statements))); continue; }
                if (index == hole) { holeAt = patches.Count; patches.Add((o.Start, o.End, "")); continue; }
                patches.Add((o.Start, o.End, Node(child, options, version, inner, statements)));
            }
        }
        if (patches.Count == 0) return new(Fresh(node, origin, options, version, scope, statementList));
        if (holeAt >= 0)
        {
            var (before, after) = Split(origin, patches, holeAt);
            return new(null, before, after, inner);
        }
        return new(Apply(origin, patches));
    }

    // The children that differ from the original only by being newly built (no origin) in place of a parsed child
    // of this node's own source, when everything else about the node is unchanged; null when there are none or when
    // the node changed in any other way. A replacement renders on its own exactly as the fresh node would render it
    // in place: an expression that binds at least as tightly as the one it replaces needs no parentheses there, and
    // an embedded statement stands alone.
    static int[]? Splices(CsNode node, SourceOrigin origin)
    {
        var before = origin.Original.Children;
        var after = node.Children;
        if (before.Count != after.Count) return null;
        var found = new List<int>();
        for (int i = 0; i < after.Count; i++)
        {
            if (ReferenceEquals(after[i].Origin, before[i].Origin)) continue;
            // A parsed child lies in its parent's source, and a child without an origin there had none in the
            // parse either (that compares equal above), so the replaced child has an origin in the node's span.
            if (after[i].Origin != null || !Spliceable(before[i], after[i])) return null;
            found.Add(i);
        }
        int index = 0;
        var restored = NodeRewrite.Children(node, child => found.Contains(index) ? before[index++] : after[index++]);
        return found.Count == 0 || UnboundChange(restored, origin.Original) ? null : found.ToArray();
    }

    // A parsed expression sits in a slot typed as an expression (a parsed top-level statement holds symbols), so its
    // replacement is one too. A statement slot renders whatever it holds as a statement of its own.
    static bool Spliceable(CsNode before, CsNode after) => before switch
    {
        CsExpression old => ExpressionWriter.Precedence((CsExpression)after) >= ExpressionWriter.Precedence(old),
        _ => before is CsStatement
    };

    // GENE-27 D6: a chain link whose own text changed renders fresh with a placeholder for its receiver, which the
    // chain loop then renders from source, so the links below keep their lines. The whitespace that followed the
    // receiver in source stays before the link's own text. A link with a new receiver, or one that owns syntax trivia,
    // parenthesizes its receiver or puts anything before it (a leading comment) renders whole instead: null.
    static Step? AroundReceiver(CsExpression link, SourceOrigin origin, RenderOptions options, CsLanguageVersion version,
        LayoutScope? scope)
    {
        // A parsed link's receiver has an origin, so a receiver that kept it has one too.
        var receiver = link.Arguments[0].Origin;
        if (!ReferenceEquals(receiver, ((CsExpression)origin.Original).Arguments[0].Origin) || link.SyntaxTrivia.Count != 0 ||
            ExpressionWriter.ParenthesizesReceiver(link)) return null;
        // The receiver, and the arguments of a call or an element access that kept their source (each is written
        // on its own there), render through placeholders.
        string placeholder = "__kept" + Guid.NewGuid().ToString("N") + "_";
        var original = ((CsExpression)origin.Original).Arguments;
        var arguments = link.Arguments.ToArray();
        var kept = new List<int> { 0 };
        for (int i = 1; i < arguments.Length; i++)
            if (link.Kind != ExpressionKind.Binary && original.Any(a => ReferenceEquals(a.Origin, arguments[i].Origin)))
                kept.Add(i);
        foreach (int i in kept) arguments[i] = Cs.Id(placeholder + i + "_");
        string text = origin.Snapshot.Text;
        string rendered = SourceNewline.Convert(Renderer.Node(link with { Arguments = arguments }, Format(options, origin, scope), version,
            SourceLayout.LineIndent(origin)), SourceNewline.Destination(origin));
        if (!rendered.StartsWith(placeholder + "0_", StringComparison.Ordinal)) return null;
        var inner = new LayoutScope(origin, scope);
        foreach (int i in kept.Skip(1))
            rendered = rendered.Replace(placeholder + i + "_", Node(link.Arguments[i], options, version, inner), StringComparison.Ordinal);
        string after = rendered[(placeholder + "0_").Length..];
        int gap = receiver!.End;
        while (gap < origin.End && char.IsWhiteSpace(text[gap])) gap++;
        if (gap > receiver.End) after = text[receiver.End..gap] + after.TrimStart(' ', '\t');
        if (text[origin.End - 1] is '\r' or '\n') after = after.TrimEnd('\r', '\n') + LineEnd(origin);
        return new(null, "", after, inner);
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

    // The text Apply produces around the patch at `hole`. A link that patches its receiver in has no list
    // patch: its patches are its bound name tokens and the spans of children that keep their parsed origins,
    // which never overlap, so Apply keeps every one of them and so does this split.
    static (string Before, string After) Split(SourceOrigin origin, List<(int Start, int End, string Text)> patches, int hole)
    {
        string text = origin.Snapshot.Text;
        var before = new System.Text.StringBuilder();
        var after = new System.Text.StringBuilder();
        var current = before;
        int position = origin.Start;
        foreach (int index in Enumerable.Range(0, patches.Count).OrderBy(i => patches[i].Start))
        {
            var patch = patches[index];
            current.Append(text, position, patch.Start - position);
            if (index == hole) current = after;
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

    // GENE-27 D7: a where clause lies outside the source of the type parameter it constrains, so the declaration
    // that owns both rewrites it. The parser records each clause's span on the owner's origin as "Constraints<i>",
    // i being the parameter's index in the parsed owner. A parsed parameter whose constraints changed, or whose
    // bound name changed while it has a clause, gets just that clause rewritten. A parameter that has no clause to
    // rewrite (it gained constraints) or whose clause would be empty asks for the owner to render fresh: false.
    static bool ConstraintPatches(CsNode node, SourceOrigin origin, List<(int Start, int End, string Text)> patches)
    {
        if (node is not CsMember) return true;
        var before = origin.Original.Children.OfType<TypeParameter>().ToArray();
        foreach (var parameter in node.Children.OfType<TypeParameter>())
        {
            int index = Array.FindIndex(before, b => ReferenceEquals(b.Origin, parameter.Origin));
            if (index < 0) continue;
            var old = before[index];
            if (parameter.Constraints.Equals(old.Constraints) && (parameter.Name == old.Name || old.Constraints.Count == 0)) continue;
            var clause = origin.Bindings.FirstOrDefault(b => b.Property == "Constraints" + index);
            if (clause.Property == null || parameter.Constraints.Count == 0) return false;
            patches.Add((clause.Start, clause.End, Renderer.ConstraintClause(parameter)));
        }
        return true;
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

    // A node rendered fresh in the place of `origin`: its own (a changed node) or the one it replaces (GENE-27 D1).
    static string Fresh(CsNode node, SourceOrigin origin, RenderOptions options, CsLanguageVersion version, LayoutScope? scope,
        bool statementList) =>
        Fit(Renderer.Node(node, Format(options, origin, scope), version, SourceLayout.LineIndent(origin), statementList), origin, scope, node);

    // Render options whose indent unit is the one the source around `origin` uses.
    static RenderOptions Format(RenderOptions options, SourceOrigin origin, LayoutScope? scope) =>
        options with { Indent = SourceLayout.InferUnit(origin, scope) ?? options.Indent };

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
        // Text written inline (an expression) does not indent itself: in a place that starts its line it takes the
        // line's indentation from source (GENE-27 D1); whole lines placed after other text on their line drop theirs.
        bool startsLine = SourceLayout.StartsLine(origin), indented = rendered.StartsWith(indent, StringComparison.Ordinal);
        if (!startsLine && indented) rendered = rendered[indent.Length..];
        else if (startsLine && !indented) rendered = indent + rendered;
        // Trailing whitespace is the writer's own: a line end, or the indentation of a continuation line after a
        // directive (GENE-27 D2), which the source after the node either ends or continues.
        string envelope = text[origin.Start..origin.End];
        if (envelope.EndsWith('\n') || envelope.EndsWith('\r')) return rendered.TrimEnd() + LineEnd(origin);
        rendered = rendered.TrimEnd('\r', '\n');
        string trimmed = rendered.TrimEnd();
        // A directive owns its whole line: when the text ends in one (an edit emptied what followed it), the line
        // end stays, and the source after the node starts the next line at the node's indentation instead of
        // joining the directive (`#endif;`, GENE-27 D3).
        return EndsInDirective(trimmed) ? trimmed + newline + indent : rendered;
    }

    // The end of a line that a replaced place ended in source: the whitespace after its core, or just the line break
    // when that holds comments of the node it replaced or when the core itself ran to the line end (a directive)
    // (GENE-27 D1/D6).
    static string LineEnd(SourceOrigin origin)
    {
        string text = origin.Snapshot.Text;
        string tail = text[origin.CoreEnd..origin.End];
        string lineBreak = text.AsSpan(0, origin.End).EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : text[(origin.End - 1)..origin.End];
        return tail.All(char.IsWhiteSpace) && tail.EndsWith(lineBreak, StringComparison.Ordinal) ? tail : lineBreak;
    }

    // Text with a '#' has a token before the scanner's closing empty one; a directive token runs to its line end, so
    // nothing (not even a comment) can follow it on that line.
    static bool EndsInDirective(string text) => text.Contains('#') && Lexer.Scan(text)[^2].Text.StartsWith('#');

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
            (CsPattern a, CsPattern b) when nameBound => a with { Name = b.Name },
            (CsParameter a, CsParameter b) when nameBound => a with { Name = b.Name },
            (TypeParameter a, TypeParameter b) when nameBound => a with { Name = b.Name },
            (CsNamespace a, CsNamespace b) when nameBound => a with { Name = b.Name },
            (CsUsing a, CsUsing b) when nameBound => a with { Name = b.Name },
            _ => shallow
        };
        return !shallow.Equals(original);
    }

    // A name-bound symbol is a single designation inside a deconstruction; it sits in the parts of symbol
    // options, which carry no origin, so preserved rendering never reaches it on its own.
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
        (CsPattern p, "Name") => p.Name,
        (CsExpression e, "Text") => e.Text,
        (CsStatement s, "Text") => s.Text,
        _ => null
    };
}
