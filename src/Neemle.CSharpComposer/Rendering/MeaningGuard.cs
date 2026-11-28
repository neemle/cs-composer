using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;

namespace Neemle.CSharpComposer.Rendering;

// GENE-29 AD 84939297 v2 section D, stage F3: the meaning guard. A checked render (RenderOptions.Validate) parses its own
// output once and compares the result with the model it rendered under guard equivalence (D1). Where the first
// divergence lies in an expression or a pattern, the render is repeated with parentheses forced around the operands
// there (D2); a divergence nothing can repair, or a comment that would read back on another owner, is EMIT001 (D3).
internal static class MeaningGuard
{
    internal const string Code = "EMIT001";

    // Fault injection for tests (AD H4): rewrites the output before it is read back, so a divergence the renderer never
    // produces reaches the repair loop and EMIT001. Set through Inject, by reflection only; null in every product render.
    [ThreadStatic] static Func<string, string>? fault;

    static Func<string, string>? Inject(Func<string, string>? change)
    {
        var previous = fault;
        fault = change;
        return previous;
    }

    // The nodes the render in progress writes in parentheses whatever their precedence (reference identity).
    [ThreadStatic] static HashSet<CsNode>? forced;

    internal static bool Forced(CsNode node) => forced != null && forced.Contains(node);

    internal static string Parenthesize(CsNode node, string text) => Forced(node) ? "(" + text + ")" : text;

    // A frame of the lockstep walk: the intended node, the node read back in its place and the intended parent frame.
    internal sealed record Frame(CsNode Intended, CsNode Actual, Frame? Parent);

    internal sealed record Divergence(CsNode Intended, CsNode Actual, Frame? Parent, bool Comment, string What)
    {
        internal IReadOnlyList<CsNode> Path()
        {
            var path = new List<CsNode> { Intended };
            for (var frame = Parent; frame != null; frame = frame.Parent) path.Add(frame.Intended);
            return path;
        }
    }

    // Renders `model`, parses the output back and repairs until the re-parse is guard-equivalent to the model as written
    // (`intended`). GENE-29 F6 (AD B2): a comment the model holds on a node other than the one the parser gives it in the
    // output (its canonical owner: the outermost node that starts, or ends, where its owner does) is then moved to that
    // owner, with the placement the parser reads, and the model is written once more, so that what a render returns is
    // what the render of its own re-parse returns.
    internal static string Run<T>(T model, Func<T, CsNode> intended, Func<T, string> render, Func<string, CsNode> read) where T : CsNode
    {
        var previous = forced;
        forced = new HashSet<CsNode>(ReferenceEqualityComparer.Instance);
        bool normalized = false;
        try
        {
            while (true)
            {
                string text = render(model);
                string written = fault == null ? text : fault(text);
                var root = intended(model);
                var walk = new Walk();
                var divergence = walk.Compare(root, read(written));
                // A node the model holds in two places and the walk compared part by part has one counterpart per place:
                // the model is copied apart (once: the copy holds no node twice), and written and compared again.
                if (walk.Shared)
                {
                    model = (T)Unshare(model);
                    continue;
                }
                if (divergence == null)
                {
                    if (normalized || walk.Moves.Count == 0) return text;
                    normalized = true;
                    model = (T)Normalize(model, root, walk.Moves, walk.Offsets);
                    continue;
                }
                if (divergence.Comment || !Repair(divergence))
                    throw new GenValidationException(new[] { new GenDiagnostic(Code, Describe(divergence),
                        "Rendered output does not read back as the model: " + divergence.What + ".") });
            }
        }
        finally { forced = previous; }
    }

    // A comment to give another owner or placement: the node that holds it, the node that is to hold it, and the placement
    // it reads back with.
    internal sealed record Move(CsNode From, CsComment Comment, CsNode To, CommentPlacement Placement);

    // GENE-29 F6: the model with every node it holds in more than one place (a node a builder reused) copied for each
    // place, so that the walk compares, and a move changes, each place on its own.
    static CsNode Unshare(CsNode model)
    {
        var seen = new HashSet<CsNode>(ReferenceEqualityComparer.Instance);
        var shared = new HashSet<CsNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<CsNode>();
        stack.Push(model);
        while (stack.Count != 0)
            foreach (var child in stack.Pop().Children)
            {
                if (!seen.Add(child)) shared.Add(child);
                stack.Push(child);
            }
        return Spine.Rewrite(model, _ => true, (original, rebuilt) => shared.Contains(original) ? rebuilt with { } : rebuilt);
    }

    // The model with each moved comment on its new owner. `root` is the model as written: a document's own comments are
    // the model's. A move is made only between nodes of the model (not from or to a node the render adds: a namespace
    // around a document's items, a document header's items) and only of a comment the node it leaves holds (not of one the
    // render writes, a document header's, or one a canonical form merged from a part of the node). Every node that gains or loses a comment holds its comments in the
    // order they read back in (`offsets`), and its ancestors are rebuilt around it.
    static CsNode Normalize(CsNode model, CsNode root, IReadOnlyList<Move> moves, Dictionary<CsComment, int> offsets)
    {
        var parents = new Dictionary<CsNode, CsNode>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<CsNode>();
        stack.Push(model);
        while (stack.Count != 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children) { parents[child] = node; stack.Push(child); }
        }
        CsNode Own(CsNode node) => ReferenceEquals(node, root) ? model : node;
        bool Held(CsNode node) => ReferenceEquals(Own(node), model) || parents.ContainsKey(node);
        var applied = moves.Where(move => (Held(move.From) & Held(move.To)) && Own(move.From).Comments.Any(c => ReferenceEquals(c, move.Comment))).ToArray();
        var comments = new Dictionary<CsNode, List<CsComment>>(ReferenceEqualityComparer.Instance);
        foreach (var move in applied)
        {
            if (!comments.TryGetValue(Own(move.From), out var from)) comments[Own(move.From)] = from = Own(move.From).Comments.ToList();
            from.RemoveAll(c => ReferenceEquals(c, move.Comment));
        }
        foreach (var move in applied)
        {
            if (!comments.TryGetValue(Own(move.To), out var to)) comments[Own(move.To)] = to = Own(move.To).Comments.ToList();
            to.Add(move.Comment with { Placement = move.Placement });
            offsets[to[^1]] = offsets[move.Comment];
        }
        // The nodes on the way from the model's root to each node whose comments change.
        var path = new HashSet<CsNode>(ReferenceEqualityComparer.Instance);
        foreach (var node in comments.Keys)
            for (CsNode? at = node; at != null && path.Add(at); at = parents.GetValueOrDefault(at)) { }
        return Spine.Rewrite(model, path.Contains, (original, rebuilt) => comments.TryGetValue(original, out var found)
            ? rebuilt with { Comments = new(found.OrderBy(c => offsets[c]).ToArray()) } : rebuilt);
    }

    static string Describe(Divergence divergence) => divergence.Intended.GetType().Name;

    // D2: forces parentheses around the operands of the lowest expression or pattern on the divergence path that has an
    // operand not yet forced; false when there is none.
    static bool Repair(Divergence divergence)
    {
        foreach (var node in divergence.Path().TakeWhile(node => node is not (CsStatement or CsBody or CsMember or CsDocument or CsNamespace)))
        {
            var operands = Operands(node).Where(operand => !forced!.Contains(operand)).ToArray();
            if (operands.Length == 0) continue;
            forced!.UnionWith(operands);
            return true;
        }
        return false;
    }

    // The children of a node that parentheses can regroup: its compound expressions and patterns, and the compound value of
    // a constant or relational pattern. Parentheses that would not be valid there (around a throw expression, say) make
    // the next read-back fail, which ends in EMIT001, so nothing wrong is ever returned.
    static IEnumerable<CsNode> Operands(CsNode owner) => owner.Children
        .SelectMany(child => child is CsPattern { Kind: PatternKind.Constant or PatternKind.Relational } pattern ? pattern.Children : [child])
        .Where(child => child is CsExpression e && ExpressionWriter.Precedence(e) < 100 || child is CsPattern { Kind: PatternKind.Or or PatternKind.And or PatternKind.Not });

    // The document as the renderer writes it: its items as Renderer.Items orders them (its namespace as a namespace item
    // around its content, usings sorted when RenderOptions.SortUsings asks for it), its header as the items and leading
    // comments it reads as, and its nullable context as a directive item.
    internal static CsDocument Intended(CsDocument document, RenderOptions options)
    {
        var nullable = options.Nullable ?? document.NullableContext;
        IEnumerable<CsNode> items = Renderer.Items(document, options);
        if (nullable != NullableMode.Omit) items = Read("#nullable " + nullable.ToString().ToLowerInvariant() + "\n", document).Items.Take(1).Concat(items);
        // GENE-29 F6: the header's own items (a directive) come first, with their comments; its other comments lead the file.
        var header = document.Header == null ? new CsDocument() : Read(document.Header, document);
        return document with
        {
            Namespace = null, Header = null, NullableContext = NullableMode.Omit, Items = new(header.Items.Concat(items).ToArray()),
            Comments = new(header.Comments.Select(c => c with { Placement = CommentPlacement.Leading }).Concat(document.Comments))
        };
    }

    static CsDocument Read(string text, CsDocument document) => Parsing.DocumentParser.Parse(new Parsing.SourceSnapshot(text, System.Text.Encoding.UTF8, []),
        new Parsing.ParseOptions { LanguageVersion = document.LanguageVersion }, CancellationToken.None).Root;

    // D1 guard equivalence: structural equality ignoring hidden metadata, with parentheses the model does not hold
    // transparent; then every comment on the same canonical owner (AD B2: the outermost node that starts, or for a
    // trailing comment ends, where its owner does) with the same placement, kind and dedented text; then the same
    // directives. Null when equivalent, else the first divergence in a depth-first lockstep walk (Walk.Compare).
    sealed class Walk
    {
        internal Divergence? Compare(CsNode intended, CsNode actual) => Structure(intended, actual) ?? Comments(intended, actual) ?? Directives(intended, actual);

        // After an equivalent read-back: each comment of the model that reads back on another node or with another placement
        // than the model gives it (AD B2: on its canonical owner), between the nodes of the model as it is written.
        internal readonly List<Move> Moves = new();

        // The offset in the output of each comment of the model that reads back.
        internal readonly Dictionary<CsComment, int> Offsets = new(ReferenceEqualityComparer.Instance);

        // Each node read back (as compared) and each node of the model as the walk compared it, with the node of the model as
        // it is written in its place (for a node compared in a canonical form: the node that form stands for).
        readonly Dictionary<CsNode, CsNode> model = new(ReferenceEqualityComparer.Instance);

        // Each compared intended node (as compared) and the node read back in its place; the parent of each node read back.
        readonly Dictionary<CsNode, CsNode> counterpart = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<CsNode, CsNode> parent = new(ReferenceEqualityComparer.Instance);
        // Nodes compared as a whole (equal, opaque or the same text): their comments and directives are not compared again.
        readonly HashSet<CsNode> whole = new(ReferenceEqualityComparer.Instance);
        // Whether a node of the model was compared part by part in two places (a node the model holds twice).
        internal bool Shared;
        readonly HashSet<CsNode> parted = new(ReferenceEqualityComparer.Instance);

        // Nodes compared as the same text whose comments differ: their comments are compared, their parts are not.
        readonly HashSet<CsNode> shallow = new(ReferenceEqualityComparer.Instance);
        // Each node, intended or read back, and the canonical form the walk compared in its place.
        readonly Dictionary<CsNode, CsNode> view = new(ReferenceEqualityComparer.Instance);

        internal Divergence? Structure(CsNode intended, CsNode actual)
        {
            var stack = new Stack<Frame>();
            stack.Push(new(intended, actual, null));
            while (stack.Count != 0)
            {
                var frame = stack.Pop();
                var i = View(frame.Intended);
                var a = Transparent(i, View(Transparent(i, frame.Actual)));
                view[frame.Actual] = a;
                counterpart[i] = a;
                model[a] = model[i] = frame.Intended;
                if (frame.Parent != null) parent[a] = view[frame.Parent.Actual];
                // A chain link is compared link by link: comparing it whole at every link would cost the square of the
                // chain's length (every other nesting is bounded by Limits.MaxNestingDepth).
                if (!(i is CsExpression link && Spine.Next(link) != null) && i.Equals(a) || Opaque(i))
                { whole.Add(i); whole.Add(a); continue; }
                // GENE-29 F6: a node that reads back as the same text with its comments placed otherwise is compared by
                // its own comments, so that they can move to the owner they read back on.
                if (!parted.Add(frame.Intended)) Shared = true;
                if (SameText(i, a))
                {
                    var set = i.Comments.Equals(a.Comments) ? whole : shallow;
                    set.Add(i); set.Add(a);
                    continue;
                }
                if (i.GetType() != a.GetType()) return new(frame.Intended, a, frame.Parent, false, Show(i) + " reads back as " + Show(a));
                var left = Structural(i);
                var right = Structural(a);
                if (left.Count != right.Count)
                {
                    if (left.Any(Opaque)) { whole.Add(i); whole.Add(a); continue; }
                    return new(frame.Intended, a, frame.Parent, false, Show(i) + " reads back with " + right.Count + " parts instead of " + left.Count);
                }
                if (!SameOwnPart(i, a, right)) return new(frame.Intended, a, frame.Parent, false, Show(i) + " reads back as " + Show(a));
                for (int k = left.Count - 1; k >= 0; k--) stack.Push(new(left[k], right[k], frame));
            }
            return null;
        }

        // The canonical form of a node, as the walk compares it.
        CsNode View(CsNode node) => view[node] = Canonical(node);

        // AD D1, B2: every comment of the model reads back with the same kind and text on the same canonical owner, on the
        // same side of it. A container comment, which the formatter writes inside its owner, may read back on any node
        // there, or on none: it reads back inside its owner's span, so it moves and goes with its owner all the same.
        internal Divergence? Comments(CsNode intended, CsNode actual)
        {
            var mine = Owned(intended).ToArray();
            var theirs = Owned(actual).ToArray();
            if (mine.Length + theirs.Length == 0) return null;
            Index(actual);
            var expected = new Dictionary<(CsNode, bool, CommentKind, string), List<int>>();
            var entries = new List<(CsNode Owner, CommentPlacement Placement, CommentKind Kind, string Text, bool Used, CsNode Mine, CsComment Comment)>();
            var read = new List<(int Entry, CsNode Owner, CsComment Comment)>();
            foreach (var (owner, comment) in mine)
            {
                var key = Key(counterpart[owner], comment);
                if (!expected.TryGetValue(key, out var at)) expected[key] = at = new();
                at.Add(entries.Count);
                entries.Add((counterpart[owner], comment.Placement, comment.Kind, key.Item4, false, owner, comment));
            }
            foreach (var (owner, comment) in theirs)
            {
                var key = Key(owner, comment);
                int entry = expected.TryGetValue(key, out var at) ? at.FindIndex(e => !entries[e].Used) : -1;
                entry = entry >= 0 ? at![entry] : entries.FindIndex(e => !e.Used && e.Placement == CommentPlacement.Container &&
                    e.Kind == comment.Kind && e.Text == key.Item4 && Within(comment, e.Owner));
                if (entry < 0) return new(owner, owner, null, true, "comment " + (comment.Text) + " reads back on another owner");
                entries[entry] = entries[entry] with { Used = true };
                read.Add((entry, owner, comment));
            }
            var lost = entries.FindIndex(e => !e.Used);
            if (lost >= 0) return new(entries[lost].Owner, entries[lost].Owner, null, true, "comment " + entries[lost].Text + " reads back on another owner");
            // AD B2: the owner and placement each comment reads back with, where it differs from the model's.
            foreach (var (entry, owner, comment) in read)
            {
                var (_, placement, _, _, _, from, original) = entries[entry];
                Offsets[original] = comment.SyntaxPosition.GetValueOrDefault().Offset;
                if (!ReferenceEquals(model[owner], model[from]) || comment.Placement != placement)
                    Moves.Add(new(model[from], original, model[owner], comment.Placement));
            }
            return null;
        }

        // The span of every node read back (a node the parser binds no span to spans its parts).
        readonly Dictionary<CsNode, (int Start, int End)> span = new(ReferenceEqualityComparer.Instance);

        void Index(CsNode root)
        {
            var order = new List<CsNode>();
            var stack = new Stack<CsNode>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                var node = stack.Pop();
                order.Add(node);
                foreach (var child in Structural(node)) stack.Push(child);
            }
            for (int k = order.Count - 1; k >= 0; k--)
            {
                var node = order[k];
                var parts = Structural(node).Select(child => span[child]).ToArray();
                span[node] = node.SyntaxPosition is { } at ? (at.Offset, at.End) :
                    (parts.Select(p => p.Start).DefaultIfEmpty(-1).Min(), parts.Select(p => p.End).DefaultIfEmpty(-1).Max());
            }
            // The root holds every comment; each node the walk compared in place of one read back has its span.
            span[root] = (0, int.MaxValue);
            foreach (var (node, seen) in view.ToArray()) span.TryAdd(seen, span.GetValueOrDefault(node, (-1, -1)));
        }

        // Whether a comment read back lies inside a node read back.
        bool Within(CsComment comment, CsNode owner)
        {
            var at = comment.SyntaxPosition.GetValueOrDefault();
            var around = span[owner];
            return around.Start <= at.Offset && at.End <= around.End;
        }

        internal Divergence? Directives(CsNode intended, CsNode actual)
        {
            var expected = new Dictionary<string, int>();
            foreach (var trivia in Trivia(intended)) expected[trivia] = expected.GetValueOrDefault(trivia) + 1;
            foreach (var trivia in Trivia(actual))
            {
                if (expected.GetValueOrDefault(trivia) == 0) return new(intended, actual, null, false, "directive " + (trivia) + " reads back where the model holds none");
                expected[trivia]--;
            }
            var lost = expected.FirstOrDefault(entry => entry.Value != 0).Key;
            return lost == null ? null : new(intended, actual, null, false, "directive " + (lost) + " does not read back");
        }

        // The comments of a tree outside the nodes compared as a whole, with their owners.
        IEnumerable<(CsNode Owner, CsComment Comment)> Owned(CsNode root)
        {
            foreach (var node in Nodes(root))
                foreach (var comment in node.Comments) yield return (node, comment);
        }

        // Every directive and inactive text of a tree outside the nodes compared as a whole: syntax trivia, directive and
        // inactive items and statements, as text (a condition by its structure).
        IEnumerable<string> Trivia(CsNode root) => Nodes(root).Where(node => !shallow.Contains(node)).SelectMany(node => node.Children).Select(Directive).OfType<string>();

        // The nodes of a tree the structural walk compared part by part, as it compared them.
        IEnumerable<CsNode> Nodes(CsNode root)
        {
            var stack = new Stack<CsNode>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                var node = stack.Pop();
                node = view.GetValueOrDefault(node, node);
                if (whole.Contains(node)) continue;
                yield return node;
                if (shallow.Contains(node)) continue;
                foreach (var child in Structural(node)) stack.Push(child);
            }
        }

        // A comment's canonical owner and side, with its text as written. A trailing comment of the whole document is
        // written after its last item and belongs to it, or, with no item, is the document's own; a leading or trailing comment belongs to the outermost node that
        // starts or ends where its owner does; a container comment the renderer writes before its owner, and a leading
        // one, are both before the owner, which is all a re-parse can tell apart.
        (CsNode, bool, CommentKind, string) Key(CsNode owner, CsComment comment)
        {
            bool trailing = comment.Placement == CommentPlacement.Trailing;
            if (trailing && owner is CsDocument)
            {
                if (Structural(owner) is [.., var last]) owner = view.GetValueOrDefault(last, last);
                else trailing = false;
            }
            // A type's and a lambda's container comments are written before them, and an expression body's before its
            // expression (GENE-29 F4: a parsed lambda's header comments), so each reads back before the node that starts there.
            if (comment.Placement == CommentPlacement.Container && owner is CsBody { Expression: { } expression }) owner = view.GetValueOrDefault(expression, expression);
            else if (comment.Placement == CommentPlacement.Container && owner is not (CsType or CsExpression { Kind: ExpressionKind.Lambda or ExpressionKind.AnonymousMethod }))
                return (owner, trailing, comment.Kind, Dedent(DocumentationWriter.Text(comment)));
            while (parent.TryGetValue(owner, out var outer) && Boundary(outer, trailing) == Boundary(owner, trailing))
                owner = outer;
            return (owner, trailing, comment.Kind, Dedent(DocumentationWriter.Text(comment)));
        }

        int Boundary(CsNode node, bool trailing) => trailing ? span[node].End : span[node].Start;
    }

    // A directive condition by its structure, parentheses aside.
    static string Condition(CsExpression condition) => condition switch
    {
        { Kind: ExpressionKind.Parenthesized, Arguments: [var inner] } => Condition(inner),
        _ => condition.Kind + " " + condition.Operator + " " + condition.Text + "(" + string.Join(",", condition.Arguments.Select(Condition)) + ")"
    };

    // A node's children other than its comments, directives and inactive text.
    static IReadOnlyList<CsNode> Structural(CsNode node) => node.Children.Where(child => child is not CsComment && Directive(child) == null).ToArray();

    // A directive or inactive text, wherever it stands (syntax trivia, an item, a member or a statement), as text; null for
    // any other node.
    static string? Directive(CsNode node) => node switch
    {
        CsSyntaxTrivia trivia => Directive(trivia.Content),
        CsStatement { Kind: StatementKind.Directive } statement => Directive(statement.Directive!),
        CsStatement { Kind: StatementKind.DisabledText } statement => Directive(statement.Disabled!),
        CsDirective { Condition: { } condition } directive => directive.Kind + " " + Condition(condition),
        CsDirective directive => DirectiveWriter.Write(directive),
        CsDisabledText disabled => Dedent(disabled.Text).Trim(),
        _ => null
    };

    // Opaque text (AD C4) is written verbatim and reads back as whatever it holds.
    static bool Opaque(CsNode node) => node is CsRaw or CsRawMember or CsDisabledText || node is CsBody { Raw: not null } ||
        node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw or StatementKind.DisabledText } ||
        node is CsType { Form: TypeForm.Raw };

    // A type, a using, and an expression or pattern with no operand of its own (a name, a literal, a type
    // reference, a designation) claim no grouping: such a node means its text and reads back as that text however the
    // parser shapes it.
    static bool SameText(CsNode intended, CsNode actual) => (intended, actual) switch
    {
        (CsType i, CsType a) => Names.Type(i) == Names.Type(a),
        // A using's text holds none of its comments, so a using with comments is compared part by part (GENE-29 F4).
        (CsUsing i, CsUsing a) => i.Comments.Count + a.Comments.Count == 0 && Renderer.Using(i) == Renderer.Using(a),
        (CsExpression { Kind: not (ExpressionKind.StringText or ExpressionKind.Interpolation) } i, CsExpression { Kind: not (ExpressionKind.StringText or ExpressionKind.Interpolation) } a) when Atomic(i) =>
            Layout.Flat(ExpressionWriter.Write(i)) == Layout.Flat(ExpressionWriter.Write(a)),
        (CsPattern i, CsPattern a) when Atomic(i) => Layout.Flat(PatternWriter.Write(i)) == Layout.Flat(PatternWriter.Write(a)),
        _ => false
    };

    static bool Atomic(CsNode node) => node.Children.All(child => child is CsType or CsComment or CsSymbol);

    // Whether two nodes are equal apart from their children, comments and directives: the intended node with the actual
    // node's children, comments and directives in their places equals the actual node.
    static bool SameOwnPart(CsNode intended, CsNode actual, IReadOnlyList<CsNode> children)
    {
        int index = 0;
        CsNode swapped;
        try { swapped = NodeRewrite.Children(Strip(intended), _ => children[index++]); }
        catch (ArgumentException) { return false; }
        return Named(swapped).Equals(Named(Strip(actual)));
    }

    // A node without its comments, directives and inactive text.
    static CsNode Strip(CsNode node) => NodeRewrite.Children(node, child => child is CsComment || Directive(child) != null ? null : child) with
    {
        Comments = Values<CsComment>.Empty,
        SyntaxTrivia = Values<CsSyntaxTrivia>.Empty
    };

    // The canonical form of a node, for the equivalences the renderer writes one way: a symbol body as the statements it
    // lowers to; a plain argument as its value; a lambda parameter of a ref type as a ref parameter; a local declaration group of one declarator as a local declaration; an `is` type pattern as an `is` type;
    // a type with primary constructor parameters as a type with a primary constructor, and `record class` as `record`; a
    // type parameter's constraints in the order the renderer writes them; an else branch braced around one statement as
    // that statement; a document's items after a file-scoped namespace inside it; a constant pattern over a parenthesized
    // value as a parenthesized pattern.
    static CsNode Canonical(CsNode node) => node switch
    {
        CsBody { Tree: { } tree } body => SymbolModel.ToBody(tree) with { Comments = new(body.Comments.Concat(tree.Comments)) },
        CsStatement { Kind: StatementKind.LocalGroup, ForInitializer: { Variables: [{ Comments.Count: 0, SyntaxTrivia.Count: 0, FixedSize: null } single] } group } s
            when group.Comments.Count == 0 && group.SyntaxTrivia.Count == 0 => new CsStatement(StatementKind.Local, single.Initializer, single.Name)
            {
                IsVar = group.IsVar, Type = group.Type, VariableModifiers = group.VariableModifiers, Comments = s.Comments, SyntaxTrivia = s.SyntaxTrivia, SyntaxPosition = s.SyntaxPosition
            },
        CsExpression { Kind: ExpressionKind.Argument, Text: "", ArgumentModifier: ArgumentModifier.None, Arguments: [var inner] } argument =>
            inner with { Comments = new(argument.Comments.Concat(inner.Comments)), SyntaxTrivia = new(argument.SyntaxTrivia.Concat(inner.SyntaxTrivia)) },
        CsLambdaParameter { Modifier: null, Type: { Form: TypeForm.Ref, ReadOnly: false, Element: { } element } } parameter => parameter with { Modifier = "ref", Type = element },
        CsExpression { Kind: ExpressionKind.Is, Pattern: { Kind: PatternKind.Type, Type: { } type, Comments.Count: 0, SyntaxTrivia.Count: 0 } } e => e with { Pattern = null, Type = type },
        CsTypeDeclaration t => t with
        {
            HasPrimaryConstructor = t.HasPrimaryConstructor || t.PrimaryParameters.Count > 0,
            Kind = t.Kind == TypeKind.RecordClass ? TypeKind.Record : t.Kind
        },
        TypeParameter t => t with { Constraints = new(Names.Constraints(t)) },
        CsStatement { Kind: StatementKind.If, Else: { Kind: StatementKind.Block, Comments.Count: 0, Body: { Comments.Count: 0, Raw: null, Tree: null } body } block } s
            when body.Statements.Where(n => Directive(n) == null).ToArray() is [CsStatement single] => s with
            {
                Else = single with
                {
                    SyntaxTrivia = new(block.SyntaxTrivia.Concat(body.SyntaxTrivia).Concat(body.Statements.Where(n => Directive(n) != null)
                        .Select(n => new CsSyntaxTrivia(n))).Concat(single.SyntaxTrivia))
                }
            },
        CsDocument d when d.Items.Select((item, index) => (item, index)).FirstOrDefault(entry => entry.item is CsNamespace { IsFileScoped: true }) is ({ } found, var at) && at < d.Items.Count - 1 =>
            d with { Items = new(d.Items.Take(at).Append(((CsNamespace)found) with { Items = new(((CsNamespace)found).Items.Concat(d.Items.Skip(at + 1))) }).ToArray()) },
        CsPattern { Kind: PatternKind.Constant, Value: { Kind: ExpressionKind.Parenthesized, Arguments: [var inner] } value } p =>
            new CsPattern(PatternKind.Parenthesized) { Patterns = new[] { p with { Value = inner, Comments = new(value.Comments.Concat(inner.Comments)) } }, Comments = p.Comments, SyntaxTrivia = p.SyntaxTrivia, SyntaxPosition = p.SyntaxPosition },
        _ => node
    };

    // Names as they read back: without a verbatim `@` or unicode escapes, and modifiers in the order the renderer writes them.
    static CsNode Named(CsNode node) => node switch
    {
        CsExpression e => e with { Text = Name(e.Text) },
        CsType t => t with { Name = Name(t.Name) },
        CsMember m => m with { Name = Name(m.Name), Modifiers = new(Names.OrderModifiers(m.Modifiers)) },
        CsParameter p => p with { Name = Name(p.Name) },
        CsStatement statement => statement with { Text = Name(statement.Text) },
        TypeParameter t => t with { Name = Name(t.Name) },
        CsNamespace n => n with { Name = Name(n.Name) },
        _ => node
    };

    static string Name(string text) => string.Join(".", text.Split('.').Select(part => IdentifierText.Decode(part.TrimStart('@'))));

    // Parentheses read back where the model holds none are transparent, with any comments they own.
    static CsNode Transparent(CsNode intended, CsNode actual)
    {
        while (true)
        {
            if (actual is CsExpression { Kind: ExpressionKind.Parenthesized } e && intended is CsExpression { Kind: not ExpressionKind.Parenthesized })
                actual = e.Arguments[0];
            else if (actual is CsPattern { Kind: PatternKind.Parenthesized } p && intended is CsPattern { Kind: not PatternKind.Parenthesized })
                actual = p.Patterns[0];
            else return actual;
        }
    }

    // AD B4: comment text without the indentation of its continuation lines.
    static string Dedent(string text) => string.Join("\n", text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => line.Trim())).TrimEnd('\n');

    static string Show(CsNode node) => node switch
    {
        CsExpression e => "expression " + e.Kind,
        CsStatement s => "statement " + s.Kind,
        _ => node.GetType().Name
    };
}
