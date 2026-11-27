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
    // produces reaches the repair loop and EMIT001. Reached by reflection only; null in every product render.
#pragma warning disable CS0649 // assigned by reflection from the tests only
    [ThreadStatic] static Func<string, string>? fault;
#pragma warning restore CS0649

    // The nodes the render in progress writes in parentheses whatever their precedence (reference identity).
    [ThreadStatic] static HashSet<CsNode>? forced;

    internal static bool Forced(CsNode node) => forced != null && forced.Contains(node);

    internal static string Parenthesize(CsNode node, string text) => Forced(node) ? "(" + text + ")" : text;

    // A frame of the lockstep walk: the intended node, the node read back in its place and the intended parent frame.
    internal sealed record Frame(CsNode Intended, CsNode Actual, Frame? Parent);

    internal sealed record Divergence(CsNode Intended, CsNode Actual, Frame? Parent, bool Comment, string What)
    {
        internal IEnumerable<CsNode> Path()
        {
            yield return Intended;
            for (var frame = Parent; frame != null; frame = frame.Parent) yield return frame.Intended;
        }
    }

    // Renders, parses the output back and repairs until the re-parse is guard-equivalent to `intended`.
    internal static string Run(CsNode intended, Func<string> render, Func<string, CsNode> read)
    {
        var previous = forced;
        forced = new HashSet<CsNode>(ReferenceEqualityComparer.Instance);
        try
        {
            while (true)
            {
                string text = render();
                string written = fault == null ? text : fault(text);
                var divergence = Compare(intended, read(written), written);
                if (divergence == null) return text;
                if (divergence.Comment || !Repair(divergence))
                    throw new GenValidationException(new[] { new GenDiagnostic(Code, Describe(divergence),
                        "Rendered output does not read back as the model: " + divergence.What + ".") });
            }
        }
        finally { forced = previous; }
    }

    static string Describe(Divergence divergence) => divergence.Intended.GetType().Name;

    // D2: forces parentheses around the operands of the lowest expression or pattern on the divergence path that has an
    // operand not yet forced; false when there is none.
    static bool Repair(Divergence divergence)
    {
        foreach (var node in divergence.Path())
        {
            if (node is CsStatement or CsBody or CsMember or CsDocument or CsNamespace or CsTree) return false;
            var operands = Operands(node).Where(operand => !forced!.Contains(operand)).ToArray();
            if (operands.Length == 0) continue;
            forced!.UnionWith(operands);
            return true;
        }
        return false;
    }

    // The children of a node that parentheses keep the meaning of and can regroup: compound expressions in an operand,
    // receiver, argument or condition slot, compound patterns, and the compound value of a constant or relational pattern.
    static IEnumerable<CsNode> Operands(CsNode owner)
    {
        foreach (var child in owner.Children)
        {
            if (child is CsExpression e && Compound(e) && owner is CsExpression
                { Kind: not (ExpressionKind.Initializer or ExpressionKind.Collection or ExpressionKind.AnonymousObject or ExpressionKind.Declaration) })
                yield return e;
            else if (child is CsPattern { Kind: PatternKind.Or or PatternKind.And or PatternKind.Not } p) yield return p;
            else if (child is CsPattern { Kind: PatternKind.Constant or PatternKind.Relational, Value: { } value } && Compound(value)) yield return value;
        }
    }

    static bool Compound(CsExpression e) => ExpressionWriter.Precedence(e) < 100 && e.Kind is not (ExpressionKind.Throw or ExpressionKind.Ref);

    // The document as the renderer writes it: its namespace as a namespace item around its content, its header as
    // leading comments, its nullable context as a directive item and, for a built document, its usings once each and
    // sorted when RenderOptions.SortUsings asks for it.
    internal static CsDocument Intended(CsDocument document, RenderOptions options)
    {
        var nullable = options.Nullable ?? document.NullableContext;
        bool plain = document.Origin == null && !document.Items.Any(n => n is CsDirective or CsDisabledText or CsExternAlias or CsGlobalStatement);
        if (document.Namespace == null && document.Header == null && nullable == NullableMode.Omit && !plain) return document;
        IEnumerable<CsNode> items = document.Items;
        if (plain)
        {
            IEnumerable<CsUsing> usings = document.Usings.Distinct();
            if (options.SortUsings) usings = usings.OrderBy(u => Layout.Flat(Renderer.Using(u)), StringComparer.Ordinal);
            items = usings.Cast<CsNode>().Concat(document.Attributes).Concat(Scoped(document, options, items.Where(n => n is not (CsUsing or CsAttribute))));
        }
        else items = Scoped(document, options, items);
        if (nullable != NullableMode.Omit) items = Read("#nullable " + nullable.ToString().ToLowerInvariant() + "\n", document).Items.Take(1).Concat(items);
        var header = document.Header == null ? Enumerable.Empty<CsComment>() :
            Read(document.Header, document).Descendants().Select(found => found.Node).OfType<CsComment>().Select(c => c with { Placement = CommentPlacement.Leading });
        return document with { Namespace = null, Header = null, NullableContext = NullableMode.Omit, Items = new(items.ToArray()), Comments = new(header.Concat(document.Comments)) };
    }

    static IEnumerable<CsNode> Scoped(CsDocument document, RenderOptions options, IEnumerable<CsNode> items) => document.Namespace == null ? items :
        new[] { new CsNamespace(document.Namespace, new(items.ToArray()), options.NamespaceStyle == NamespaceStyle.FileScoped) };

    static CsDocument Read(string text, CsDocument document) => Parsing.DocumentParser.Parse(new Parsing.SourceSnapshot(text, System.Text.Encoding.UTF8, []),
        new Parsing.ParseOptions { LanguageVersion = document.LanguageVersion }, CancellationToken.None).Root;

    // D1 guard equivalence: structural equality ignoring hidden metadata, with parentheses the model does not hold
    // transparent; then every comment on the same canonical owner (AD B2: the outermost node that starts, or for a
    // trailing comment ends, where its owner does) with the same placement, kind and dedented text; then the same
    // directives. Null when equivalent, else the first divergence in a depth-first lockstep walk.
    internal static Divergence? Compare(CsNode intended, CsNode actual, string text)
    {
        var walk = new Walk();
        return walk.Structure(intended, actual) ?? walk.Comments(intended, actual, text) ?? walk.Directives(intended, actual);
    }

    sealed class Walk
    {
        // Each compared intended node (as compared) and the node read back in its place; the parent of each node read back.
        readonly Dictionary<CsNode, CsNode> counterpart = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<CsNode, CsNode> parent = new(ReferenceEqualityComparer.Instance);
        // Nodes compared as a whole (equal, opaque or the same text): their comments and directives are not compared again.
        readonly HashSet<CsNode> whole = new(ReferenceEqualityComparer.Instance);
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
                if (frame.Parent != null) parent[a] = view[frame.Parent.Actual];
                // A chain link is compared link by link: comparing it whole at every link would cost the square of the
                // chain's length (every other nesting is bounded by Limits.MaxNestingDepth).
                if (ReferenceEquals(i, a) || !(i is CsExpression link && Spine.Next(link) != null) && i.Equals(a) || Opaque(i) || SameText(i, a))
                { whole.Add(i); whole.Add(a); continue; }
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

        // The canonical form of a node, as the walk compares it (computed once per node).
        CsNode View(CsNode node)
        {
            if (view.TryGetValue(node, out var known)) return known;
            var result = Canonical(node);
            view[node] = result;
            return result;
        }

        internal Divergence? Comments(CsNode intended, CsNode actual, string text)
        {
            var mine = Owned(intended).ToArray();
            var theirs = Owned(actual).ToArray();
            if (mine.Length + theirs.Length == 0) return null;
            Index(actual);
            var expected = new Dictionary<(CsNode, bool, CommentKind, string), int>();
            foreach (var (owner, comment) in mine)
            {
                var key = Key(counterpart[owner], comment);
                expected[key] = expected.GetValueOrDefault(key) + 1;
            }
            foreach (var (owner, comment) in theirs)
            {
                // The parser's own reading of the comment, else the reading AD B2 gives its position in the output.
                var key = Key(owner, comment);
                if (expected.GetValueOrDefault(key) == 0 && Attach(comment, text) is { } attached)
                    key = (attached.Owner, attached.Trailing, comment.Kind, key.Item4);
                if (expected.GetValueOrDefault(key) == 0) return new(owner, owner, null, true, "comment " + Clip(comment.Text) + " reads back on another owner");
                expected[key]--;
            }
            var lost = expected.FirstOrDefault(entry => entry.Value != 0).Key;
            return lost.Item1 == null ? null : new(lost.Item1, lost.Item1, null, true, "comment " + Clip(lost.Item4) + " reads back on another owner");
        }

        // The span of every node read back (a node the parser binds no span to spans its parts), each node's parent there,
        // and the spans of its comments.
        readonly Dictionary<CsNode, (int Start, int End)> span = new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<CsNode, CsNode> above = new(ReferenceEqualityComparer.Instance);
        readonly List<(int Start, int End)> commentSpans = [];

        void Index(CsNode root)
        {
            var order = new List<CsNode>();
            var stack = new Stack<CsNode>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                var node = stack.Pop();
                order.Add(node);
                foreach (var comment in node.Comments)
                    if (comment.SyntaxPosition is { } at) commentSpans.Add((at.Offset, at.End));
                foreach (var child in Structural(node)) { above[child] = node; stack.Push(child); }
            }
            for (int k = order.Count - 1; k >= 0; k--)
            {
                var node = order[k];
                if (node.SyntaxPosition is { } at) { span[node] = (at.Offset, at.End); continue; }
                var parts = Structural(node).Where(span.ContainsKey).Select(child => span[child]).ToArray();
                if (parts.Length != 0) span[node] = (parts.Min(p => p.Start), parts.Max(p => p.End));
            }
        }

        // AD B2: the owner and side of a comment by its position in the output, among the nodes read back.
        (CsNode Owner, bool Trailing)? Attach(CsComment comment, string text)
        {
            if (comment.SyntaxPosition is not { } at) return null;
            int start = at.Offset, end = at.End;
            var enclosing = span.Where(entry => entry.Value.Start <= start && entry.Value.End >= end)
                .OrderBy(entry => entry.Value.End - entry.Value.Start).Select(entry => entry.Key).FirstOrDefault();
            if (enclosing == null) return null;
            var inside = span.Where(entry => !ReferenceEquals(entry.Key, enclosing) && Within(entry.Key, enclosing)).ToArray();
            int previous = Skip(text, start, -1), next = Skip(text, end, 1);
            // 1. On the line of the previous token, after a node that ends there (a list separator stands for its item).
            if (previous > 0 && !text.AsSpan(previous, start - previous).Contains('\n'))
            {
                int item = text[previous - 1] == ',' ? Skip(text, previous - 1, -1) : previous;
                if (Outermost(inside, enclosing, node => span[node].End == item) is { } owner) return (Seen(owner), true);
            }
            // 2. Before a node that starts at the next token.
            if (Outermost(inside, enclosing, node => span[node].Start == next) is { } leading) return (Seen(leading), false);
            // 3. Before the closing delimiter or the end: the enclosing node's own; 4. else the node before it.
            if (next >= text.Length || text[next] is '}' or ')' or ']') return (Seen(enclosing), false);
            return Outermost(inside, enclosing, node => span[node].End == previous) is { } last ? (Seen(last), true) : (Seen(enclosing), false);
        }

        CsNode Seen(CsNode node) => view.GetValueOrDefault(node, node);

        bool Within(CsNode node, CsNode enclosing)
        {
            for (var outer = above.GetValueOrDefault(node); outer != null; outer = above.GetValueOrDefault(outer))
                if (ReferenceEquals(outer, enclosing)) return true;
            return false;
        }

        // The node matching `at` whose parent is the enclosing node or does not match.
        CsNode? Outermost(KeyValuePair<CsNode, (int Start, int End)>[] inside, CsNode enclosing, Func<CsNode, bool> at) =>
            inside.Select(entry => entry.Key).FirstOrDefault(node => at(node) && (ReferenceEquals(above[node], enclosing) || !at(above[node])));

        // The position after the previous significant token (direction -1) or of the next one (+1), past whitespace and
        // comments.
        int Skip(string text, int position, int direction)
        {
            while (true)
            {
                if (direction < 0)
                {
                    while (position > 0 && char.IsWhiteSpace(text[position - 1])) position--;
                    var comment = commentSpans.FirstOrDefault(c => c.End == position && c.Start < position);
                    if (comment == default) return position;
                    position = comment.Start;
                }
                else
                {
                    while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                    var comment = commentSpans.FirstOrDefault(c => c.Start == position && c.End > position);
                    if (comment == default) return position;
                    position = comment.End;
                }
            }
        }

        internal Divergence? Directives(CsNode intended, CsNode actual)
        {
            var expected = new Dictionary<string, int>();
            foreach (var trivia in Trivia(intended)) expected[trivia] = expected.GetValueOrDefault(trivia) + 1;
            foreach (var trivia in Trivia(actual))
            {
                if (expected.GetValueOrDefault(trivia) == 0) return new(intended, actual, null, false, "directive " + Clip(trivia) + " reads back where the model holds none");
                expected[trivia]--;
            }
            var lost = expected.FirstOrDefault(entry => entry.Value != 0).Key;
            return lost == null ? null : new(intended, actual, null, false, "directive " + Clip(lost) + " does not read back");
        }

        // The comments of a tree outside the nodes compared as a whole, with their owners.
        IEnumerable<(CsNode Owner, CsComment Comment)> Owned(CsNode root)
        {
            foreach (var node in Nodes(root))
                foreach (var comment in node.Comments) yield return (node, comment);
        }

        // Every directive and inactive text of a tree outside the nodes compared as a whole: syntax trivia, directive and
        // inactive items and statements, as text (a condition by its structure).
        IEnumerable<string> Trivia(CsNode root) => Nodes(root).SelectMany(node => node.Children).Select(Directive).OfType<string>();

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
                foreach (var child in Structural(node)) stack.Push(child);
            }
        }

        // A comment's canonical owner and side, with its text as written. A trailing comment of the whole document is
        // written after its last item and belongs to it; a leading or trailing comment belongs to the outermost node that
        // starts or ends where its owner does; a container comment the renderer writes before its owner, and a leading
        // one, are both before the owner, which is all a re-parse can tell apart.
        (CsNode, bool, CommentKind, string) Key(CsNode owner, CsComment comment)
        {
            bool trailing = comment.Placement == CommentPlacement.Trailing;
            if (trailing && owner is CsDocument && Structural(owner) is [.., var last]) owner = view.GetValueOrDefault(last, last);
            if (comment.Placement != CommentPlacement.Container || owner is CsType)
                while (parent.TryGetValue(owner, out var outer) && Boundary(outer, trailing) is { } edge && edge == Boundary(owner, trailing))
                    owner = outer;
            return (owner, trailing, comment.Kind, Dedent(DocumentationWriter.Text(comment)));
        }

        int? Boundary(CsNode node, bool trailing) => (span.TryGetValue(node, out var at) ? at : node.SyntaxPosition is { } p ? (p.Offset, p.End) : ((int, int)?)null) is { } s ? (trailing ? s.Item2 : s.Item1) : null;
    }

    // A directive condition by its structure, parentheses aside.
    static string Condition(CsExpression? condition) => condition switch
    {
        null => "",
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
        CsStatement { Kind: StatementKind.Directive, Directive: { } directive } => Directive(directive),
        CsStatement { Kind: StatementKind.DisabledText, Disabled: { } disabled } => Directive(disabled),
        CsDirective { Condition: { } condition } directive => directive.Kind + " " + Condition(condition),
        CsDirective directive => DirectiveWriter.Write(directive),
        CsDisabledText disabled => Dedent(disabled.Text).Trim(),
        _ => null
    };

    // Opaque text (AD C4) is written verbatim and reads back as whatever it holds.
    static bool Opaque(CsNode node) => node is CsRaw or CsRawMember or CsDisabledText || node is CsBody { Raw: not null } ||
        node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw or StatementKind.DisabledText } ||
        node is CsType { Form: TypeForm.Raw };

    // A type, a using, a directive, and an expression or pattern with no operand of its own (a name, a literal, a type
    // reference, a designation) claim no grouping: such a node means its text and reads back as that text however the
    // parser shapes it.
    static bool SameText(CsNode intended, CsNode actual) => (intended, actual) switch
    {
        (CsType i, CsType a) => Names.Type(i) == Names.Type(a),
        (CsUsing i, CsUsing a) => Renderer.Using(i) == Renderer.Using(a),
        (CsDirective i, CsDirective a) => DirectiveWriter.Write(i) == DirectiveWriter.Write(a),
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
                        .Select(n => new CsSyntaxTrivia((CsNode?)n.Directive ?? n.Disabled!))).Concat(single.SyntaxTrivia))
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
        CsPattern p => p with { Name = p.Name == null ? null : Name(p.Name) },
        CsMember m => m with { Name = Name(m.Name), Modifiers = new(Names.OrderModifiers(m.Modifiers)) },
        CsParameter p => p with { Name = Name(p.Name) },
        CsLambdaParameter p => p with { Name = Name(p.Name) },
        CsVariableDeclarator v => v with { Name = Name(v.Name) },
        TypeParameter t => t with { Name = Name(t.Name) },
        CsNamespace n => n with { Name = Name(n.Name) },
        _ => node
    };

    static string Name(string text) => string.Join(".", text.Split('.').Select(part => IdentifierText.Decode(part.TrimStart('@')) ?? part));

    // Parentheses read back where the model holds none are transparent, with any comments they own.
    static CsNode Transparent(CsNode intended, CsNode actual)
    {
        while (true)
        {
            if (actual is CsExpression { Kind: ExpressionKind.Parenthesized, Arguments.Count: 1 } e && intended is CsExpression { Kind: not ExpressionKind.Parenthesized })
                actual = Merge(e, e.Arguments[0]);
            else if (actual is CsPattern { Kind: PatternKind.Parenthesized, Patterns.Count: 1 } p && intended is CsPattern { Kind: not PatternKind.Parenthesized })
                actual = Merge(p, p.Patterns[0]);
            else return actual;
        }
    }

    static CsNode Merge(CsNode wrapper, CsNode inner) => wrapper.Comments.Count == 0 ? inner : inner with
    {
        Comments = new(wrapper.Comments.Where(c => c.Placement != CommentPlacement.Trailing).Concat(inner.Comments)
            .Concat(wrapper.Comments.Where(c => c.Placement == CommentPlacement.Trailing)))
    };

    // AD B4: comment text without the indentation of its continuation lines.
    static string Dedent(string text) => string.Join("\n", text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line => line.Trim())).TrimEnd('\n');

    static string Clip(string text) => text.Length <= 40 ? text : text[..37] + "...";

    static string Show(CsNode node) => node switch
    {
        CsExpression e => "expression " + e.Kind,
        CsPattern p => "pattern " + p.Kind,
        CsStatement s => "statement " + s.Kind,
        _ => node.GetType().Name
    };
}
