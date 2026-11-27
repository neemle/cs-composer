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
        // A node with an embedded origin writes no text of its own: reformatting it reformats the node it wraps (its
        // only child).
        if (origin is { Embedded: true } && node.Formatting is { } formatting)
            node = NodeRewrite.Children(node with { Formatting = null }, child => child with { Formatting = formatting });
        if (origin == null || node.Formatting != null) return new(Scoped(node, options, version, scope, statementList));
        if ((same ?? node.Equals(origin.Original)) && !(formatted ?? HasFormatting(node, -1)))
            return new(origin.Snapshot.Text[origin.Start..origin.End]);
        var patches = BindingPatches(node, origin);
        bool constrained = ConstraintPatches(node, origin, patches);
        var list = SourceLayout.List(node).ToArray();
        bool listChanged = origin.ListStart >= 0 && !list.Select(n => n.Origin)
            .SequenceEqual(SourceLayout.List(origin.Original).Select(n => n.Origin));
        // GENE-27 R1: a changed list is patched as a region of its own, so what else changed shows on the node with
        // its list (and the list's own comments) as in source; null when a comment outside the list changed.
        var subject = listChanged ? Unlisted(node, origin) : node;
        var spliced = new Dictionary<int, CsNode>();
        bool header = false;
        if (!constrained || subject == null || UnboundChange(subject, origin.Original))
        {
            // GENE-27 D1/D6: newly built children replace only their own source text, and so do the edited holes of an
            // interpolated string (O2c); a changed chain link renders fresh around its receiver, which keeps its source;
            // a type renders just its header again (R1); anything else renders the node fresh.
            if (constrained && subject != null && Splices(subject, origin) is { } found) spliced = found;
            else if (Holes(node, origin) is { } holes) patches.AddRange(holes);
            else if (hole >= 0 && AroundReceiver((CsExpression)node, origin, options, version, scope) is { } around) return around;
            else if (Header(node, subject, origin, options, version, scope, patches)) header = true;
            else return new(Fresh(node, origin, options, version, scope, statementList));
        }
        var inner = new LayoutScope(origin, scope);
        int holeAt = -1;
        if (listChanged)
            patches.Add((origin.ListStart, origin.ListEnd, new ListRegion(node, list, inner,
                child => Node(child, options, version, inner), options.Indent).Text()));
        var children = subject!.Children;
        var before = origin.Original.Children;
        for (int index = 0; index < children.Count; index++)
        {
            var child = children[index];
            bool splice = spliced.ContainsKey(index);
            if ((splice ? before[index].Origin : child.Origin) is not { } o) continue;
            if (!ReferenceEquals(o.Snapshot, origin.Snapshot) || o.Start < origin.Start || o.End > origin.End)
                continue;
            // The list region and a header rendered again hold their children's text already.
            if (listChanged && Within(o.Start, origin.ListStart, origin.ListEnd) || header && Within(o.CoreStart, origin.CoreStart, origin.ListStart)) continue;
            bool statements = node is CsSwitchSection && child is CsBody || node is CsGlobalStatement;
            if (splice) { patches.Add((o.Start, o.End, Fresh(spliced[index], o, options, version, inner, statements))); continue; }
            if (index == hole) { holeAt = patches.Count; patches.Add((o.Start, o.End, "")); continue; }
            patches.Add((o.Start, o.End, Node(child, options, version, inner, statements)));
        }
        if (patches.Count == 0) return new(Fresh(node, origin, options, version, scope, statementList));
        if (holeAt >= 0)
        {
            var (head, tail) = Split(origin, patches, holeAt);
            return new(null, head, tail, inner);
        }
        return new(Apply(origin, patches));
    }

    // The children that differ from the original only by being newly built (no origin) in place of a parsed child
    // of this node's own source, when everything else about the node is unchanged; null when there are none or when
    // the node changed in any other way. A replacement renders on its own exactly as the fresh node would render it
    // in place: an expression that binds at least as tightly as the one it replaces needs no parentheses there, and
    // an embedded statement stands alone.
    static Dictionary<int, CsNode>? Splices(CsNode node, SourceOrigin origin)
    {
        var before = origin.Original.Children;
        var after = node.Children;
        if (before.Count != after.Count) return null;
        var found = new Dictionary<int, CsNode>();
        for (int i = 0; i < after.Count; i++)
        {
            if (ReferenceEquals(after[i].Origin, before[i].Origin)) continue;
            // A parsed child lies in its parent's source, and a child without an origin there had none in the
            // parse either (that compares equal above), so the replaced child has an origin in the node's span.
            if (after[i].Origin != null || Splice(origin.Original, before[i], after[i]) is not { } replacement) return null;
            found.Add(i, replacement);
        }
        int index = 0;
        var restored = NodeRewrite.Children(node, child => found.ContainsKey(index) ? before[index++] : after[index++]);
        return found.Count == 0 || UnboundChange(restored, origin.Original) ? null : found;
    }

    // What a newly built `after` renders as in the place of `before`, a child of `parent`, so that the text is the one
    // the fresh parent would write there; null when that cannot be told. A parsed expression sits in a slot typed as
    // an expression (a parsed top-level statement holds symbols), so its replacement is one too: in an operand or
    // argument slot of a known precedence (O2a/O2d) it goes in parentheses when the parent would put it in them, in a
    // body (an expression body, a lambda's) it needs none (O2b), and elsewhere it must bind at least as tightly as the
    // expression it replaces. A statement slot renders whatever it holds as a statement of its own, except a
    // declaration or a label, which no embedded statement can be (R2).
    static CsNode? Splice(CsNode parent, CsNode before, CsNode after)
    {
        if (before is CsExpression old)
        {
            var replacement = (CsExpression)after;
            int? slot = parent is CsBody ? 0 : parent is CsExpression owner ?
                ExpressionWriter.Slot(owner, IndexOf(owner.Arguments, old), replacement) : null;
            if (ExpressionWriter.Precedence(replacement) >= (slot ?? ExpressionWriter.Precedence(old))) return replacement;
            return slot == null ? null : new CsExpression(ExpressionKind.Parenthesized, "", new[] { replacement });
        }
        return before is CsStatement && Array.IndexOf(Declarations, ((CsStatement)after).Kind) < 0 ? after : null;
    }

    // Whether `position` lies in [start, end): parsed spans never straddle a list's or a header's bounds, so where a
    // node or comment starts tells where it lies.
    static bool Within(int position, int start, int end) => (uint)(position - start) < (uint)(end - start);

    static int IndexOf(Values<CsExpression> items, CsNode item)
    {
        for (int i = 0; i < items.Count; i++) if (ReferenceEquals(items[i], item)) return i;
        return -1;
    }

    static readonly StatementKind[] Declarations = [StatementKind.Local, StatementKind.LocalGroup, StatementKind.LocalFunction,
        StatementKind.UsingDeclaration, StatementKind.Label, StatementKind.Directive, StatementKind.DisabledText];

    // GENE-27 R1: `node` with its list and the list's own comments as in source, or null when a comment outside the
    // list changed. A container comment in the list region, or one the list region places (a new comment, or one
    // moved in from elsewhere), is the list's.
    static CsNode? Unlisted(CsNode node, SourceOrigin origin)
    {
        var original = origin.Original;
        bool Outside(CsComment c) => c.Placement != CommentPlacement.Container || c.Origin is { } o &&
            !Within(o.CoreStart, origin.ListStart, origin.ListEnd) && original.Comments.Any(k => ReferenceEquals(k.Origin, o));
        if (!node.Comments.Where(Outside).SequenceEqual(original.Comments.Where(Outside))) return null;
        var items = SourceLayout.List(original);
        CsNode restored = node switch
        {
            CsDocument d => d with { Items = new(items) },
            CsNamespace n => n with { Items = new(items) },
            CsTypeDeclaration t => t with { Members = new(items.Cast<CsMember>()) },
            CsBody b => b with { Statements = new(items.Cast<CsStatement>()) },
            // The list owners left are switch statements (SourceLayout.List).
            _ => ((CsStatement)node) with { Sections = new(items.Cast<CsSwitchSection>()) }
        };
        return restored with { Comments = original.Comments };
    }

    // GENE-27 R1: a type whose header changed (modifiers, attributes, name, type and primary parameters, base list,
    // constraints) gets that header, from its first token to its opening brace, rendered again; its comments, members
    // and closing brace keep their source. Not when the header holds comments or directives of its own, or when a
    // comment outside the member list changed: false, and the type renders fresh.
    static bool Header(CsNode node, CsNode? subject, SourceOrigin origin, RenderOptions options, CsLanguageVersion version,
        LayoutScope? scope, List<(int Start, int End, string Text)> patches)
    {
        if (node is not CsTypeDeclaration type || origin.ListStart < 0 || type.SyntaxTrivia.Count != 0 ||
            subject == null || !subject.Comments.SequenceEqual(origin.Original.Comments) ||
            origin.Original.Comments.Any(c => Within(c.Origin!.CoreStart, origin.CoreStart, origin.ListStart))) return false;
        // One member makes the writer open the body; the header is the text before that member's line, and the opening
        // brace keeps the whitespace before it in source (its own line, or the header's).
        string placeholder = "__kept" + Guid.NewGuid().ToString("N") + "_";
        CsMember member = type.Kind == TypeKind.Enum ? new CsEnumMember(placeholder) : new CsField(Cs.Int, placeholder);
        string indent = SourceLayout.LineIndent(origin);
        string rendered = Renderer.Node(type with { Members = new[] { member }, Comments = Values<CsComment>.Empty },
            Format(options, origin, scope), version, indent);
        string text = rendered[indent.Length..SourceLayout.LineStart(rendered, rendered.IndexOf(placeholder, StringComparison.Ordinal))].TrimEnd();
        string source = origin.Snapshot.Text;
        int brace = origin.ListStart - 1, gap = brace;
        while (char.IsWhiteSpace(source[gap - 1])) gap--;
        text = text[..^1].TrimEnd() + source[gap..brace] + "{";
        // A type's bound tokens (its name, its where clauses) all lie in its header, which the new text replaces.
        patches.Clear();
        patches.Add((origin.CoreStart, origin.ListStart, SourceNewline.Convert(text, SourceNewline.Destination(origin))));
        return true;
    }

    // GENE-27 O2c: an interpolated string whose only changes are the expressions in its holes (a hole's value or its
    // alignment) gets just those expressions written again, over the spans the parser recorded ("Hole<i>",
    // "Alignment<i>", i being the part's index); its text, formats and delimiters keep their source. Null otherwise.
    static List<(int Start, int End, string Text)>? Holes(CsNode node, SourceOrigin origin)
    {
        if (node is not CsExpression { Kind: ExpressionKind.InterpolatedString } changed) return null;
        var original = (CsExpression)origin.Original;
        if (changed.Arguments.Count != original.Arguments.Count || !(changed with { Arguments = original.Arguments }).Equals(original)) return null;
        var patches = new List<(int Start, int End, string Text)>();
        string newline = SourceNewline.Destination(origin);
        for (int i = 0; i < changed.Arguments.Count; i++)
        {
            CsExpression part = changed.Arguments[i], old = original.Arguments[i];
            if (part.Equals(old)) continue;
            // A part that is text, or that changed kind, format or comments, takes the whole string to render again.
            if (old.Kind != ExpressionKind.Interpolation || part.Alignment == null != (old.Alignment == null) ||
                !(part with { Arguments = old.Arguments, Alignment = old.Alignment }).Equals(old)) return null;
            if (!part.Arguments[0].Equals(old.Arguments[0])) patches.Add(Hole(origin, "Hole" + i, StringWriter.Hole(part.Arguments[0]), newline));
            if (!Equals(part.Alignment, old.Alignment)) patches.Add(Hole(origin, "Alignment" + i, ExpressionWriter.Write(part.Alignment!), newline));
        }
        return patches;
    }

    static (int Start, int End, string Text) Hole(SourceOrigin origin, string property, string text, string newline)
    {
        var binding = origin.Bindings.First(b => b.Property == property);
        return (binding.Start, binding.End, SourceNewline.Convert(text, newline));
    }

    // GENE-27 R2/O2b: a node with an embedded origin (a brace-less body, a lambda's expression body) that cannot keep
    // its source around what changed in it makes its owner render fresh, which writes it the way it must be written
    // now (a brace-less body that holds two statements gets its braces there).
    static bool Unsettled(CsNode node) =>
        node.Origin is { Embedded: true } origin && UnboundChange(node, origin.Original) && Splices(node, origin) == null;

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
            SourceLayout.LineIndent(origin), sourceDirectives: true), SourceNewline.Destination(origin));
        if (!rendered.StartsWith(placeholder + "0_", StringComparison.Ordinal)) return null;
        var inner = new LayoutScope(origin, scope);
        foreach (int i in kept.Skip(1))
            rendered = rendered.Replace(placeholder + i + "_", Node(link.Arguments[i], options, version, inner), StringComparison.Ordinal);
        string after = rendered[(placeholder + "0_").Length..];
        int gap = receiver!.End;
        while (gap < origin.End && char.IsWhiteSpace(text[gap])) gap++;
        if (gap > receiver.End) after = text[receiver.End..gap] + after.TrimStart(' ', '\t');
        // The writer ends a trailing block comment with a space; the source after the link brings its own (QA O1).
        after = after.TrimEnd();
        if (text[origin.End - 1] is '\r' or '\n') after += LineEnd(origin);
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
        Fit(Renderer.Node(node, Format(options, origin, scope), version, SourceLayout.LineIndent(origin), statementList, sourceDirectives: true), origin, scope, node);

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
                after[i].Origin == null && !after[i].Equals(before[i]) || Unsettled(after[i])) return true;
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
