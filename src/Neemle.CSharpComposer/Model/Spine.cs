using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Model;

// GENE-19 D10: a chain spine is a run of left-deep links (binary operator, member access, invocation and
// element access) through Arguments[0]. In the symbol model the same link is Symbol -> Options -> receiver.
// A spine edge adds no nesting level, and every pass walks a spine with a loop or an explicit stack,
// never one stack frame per link.
internal static class Spine
{
    internal static bool IsLink(CsExpression? e) =>
        e is { Kind: ExpressionKind.Binary or ExpressionKind.Member or ExpressionKind.Invocation or ExpressionKind.Index };

    // The receiver a link continues along: Arguments[0], when it is itself a link.
    internal static CsExpression? Next(CsExpression e) =>
        IsLink(e) && e.Arguments is { Count: > 0 } arguments && arguments[0] is { } receiver && IsLink(receiver) ? receiver : null;

    // The child a node continues its spine along, or null; the edge to it adds no level.
    internal static CsNode? Next(CsNode node) => node switch
    {
        CsExpression e => Next(e),
        CsSymbol { Options: { } options } => Next(options) != null ? options : null,
        CsSymbolOptions options => Receiver(options) is { } receiver && IsSpineReceiver(receiver) ? receiver : null,
        _ => null
    };

    // The receiver of a symbol link as the lowering reads it (SymbolModel.Lowerer.Expression), or null.
    static CsNode? Receiver(CsSymbolOptions o)
    {
        if (o.Operation != SymbolOperation.None || o.Await || o.AsCompound || o.Using || o.AsVar || o.Type != null) return null;
        if (o.AsIndex) return o.Target;
        if (o.AsCast || o.AsTypeOf || o.AsDefault || o.AsIs || o.AsConditional || o.AsArray || o.AsInitializer || o.AsParenthesized || o.AsNew || o.AsAssignment) return null;
        if (o.AsCall) return o.Target;
        if (o.Operator != CsOperator.None)
            return Operators.Unary(o.Operator) || o.Parts is not { Count: 2 } ? null : o.Parts[0];
        return o.Value == null ? o.Target : null;
    }

    static bool IsSpineReceiver(CsNode receiver) => receiver switch
    {
        CsExpression e => IsLink(e),
        CsSymbol { Options: { } options } => Receiver(options) != null,
        _ => false
    };

    // Index in Children of the child Next returns (NodeChildren order), or -1.
    internal static int Index(CsNode node)
    {
        var next = Next(node);
        if (next == null) return -1;
        int index = 0;
        // Children, which every caller reads first, already rejects a null comment.
        foreach (var comment in node.Comments) if (comment.Placement != CommentPlacement.Trailing) index++;
        switch (node)
        {
            case CsExpression e:
                return index + e.LambdaAttributes.Count + e.QueryClauses.Count + (e.Alignment == null ? 0 : 1) + e.TypeArguments.Count +
                    (e.Type == null ? 0 : 1) + (e.Pattern == null ? 0 : 1) + (e.Initializer == null ? 0 : 1);
            case CsSymbol:
                return index;
            default:
                // Next is non-null only for an expression, a symbol or symbol options.
                var o = (CsSymbolOptions)node;
                index += o.Parameters.Count + (o.ParameterSymbols == null ? 0 : 1) + (o.Clauses == null ? 0 : 1) + (o.EqualityKey == null ? 0 : 1) +
                    o.Attributes.Count + (o.Body == null ? 0 : 1) + (o.Alignment == null ? 0 : 1) + o.QueryClauses.Count + o.TypeArguments.Count +
                    (o.Type == null ? 0 : 1) + (o.Pattern == null ? 0 : 1) + (o.Initializer == null ? 0 : 1) + (o.Value == null ? 0 : 1);
                // Binary operands are Parts, after Target and Arguments; every other link continues along Target.
                bool operands = !o.AsIndex && !o.AsCall && o.Operator != CsOperator.None;
                return operands ? index + (o.Target == null ? 0 : 1) + (o.Arguments == null ? 0 : 1) : index;
        }
    }

    // Children-edge height of `node` by the D10 metric, iteratively; stops as soon as it exceeds `limit`.
    internal static int Height(CsNode node, int limit)
    {
        int height = 0;
        var stack = new Stack<(CsNode Node, int Depth)>();
        stack.Push((node, 0));
        while (stack.Count != 0)
        {
            var (current, depth) = stack.Pop();
            if (depth > height && (height = depth) > limit) break;
            var children = current.Children;
            int spine = Index(current);
            for (int i = 0; i < children.Count; i++) stack.Push((children[i], i == spine ? depth : depth + 1));
        }
        return height;
    }

    // Rewrites a subtree bottom-up with an explicit stack. `descend` decides whether a node's children are
    // rewritten at all (false keeps the node as it is); `finish` receives the original node and the node with
    // rewritten children. Children are visited in the order NodeRewrite.Children calls its function, so a
    // stateful `finish` sees exactly the order the recursive rewrite would.
    internal static CsNode Rewrite(CsNode root, Func<CsNode, bool> descend, Func<CsNode, CsNode, CsNode> finish)
    {
        if (!descend(root)) return root;
        var frames = new Stack<Frame>();
        frames.Push(new Frame(root));
        CsNode? result = null;
        while (frames.Count != 0)
        {
            var frame = frames.Peek();
            if (frame.Next < frame.Children.Count)
            {
                var child = frame.Children[frame.Next];
                if (!descend(child)) { frame.Results.Add(child); frame.Next++; continue; }
                frames.Push(new Frame(child));
                continue;
            }
            frames.Pop();
            int index = 0;
            var rebuilt = NodeRewrite.Children(frame.Node, _ => frame.Results[index++]);
            var done = finish(frame.Node, rebuilt);
            if (frames.Count == 0) result = done;
            else { var parent = frames.Peek(); parent.Results.Add(done); parent.Next++; }
        }
        return result!;
    }

    sealed class Frame
    {
        internal readonly CsNode Node;
        internal readonly List<CsNode> Children = new();
        internal readonly List<CsNode?> Results = new();
        internal int Next;

        internal Frame(CsNode node)
        {
            Node = node;
            NodeRewrite.Children(node, child => { Children.Add(child); return child; });
        }
    }
}

// A validator path built lazily, one link per step, so a long chain costs linear memory. A run of more than
// Limits.MaxNestingDepth consecutive spine steps is written once as ".Arguments[0]×k" (GENE-19 D10); shorter
// runs are written step by step exactly as before.
internal sealed class NodePathText
{
    readonly NodePathText? parent;
    readonly string segment;
    readonly NodePathText? runBase;
    readonly int run;

    NodePathText(NodePathText? parent, string segment, NodePathText? runBase, int run)
    {
        this.parent = parent;
        this.segment = segment;
        this.runBase = runBase;
        this.run = run;
    }

    internal static NodePathText Root(string text) => new(null, text, null, 0);

    internal NodePathText Step(string segment, bool spine) => spine
        ? new(this, segment, run > 0 ? runBase : this, run + 1)
        : new(this, segment, null, 0);

    internal static string Run(int count) => Compressed + count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal const string Compressed = ".Arguments[0]\u00D7";

    public override string ToString()
    {
        var pieces = new List<string>();
        for (var current = this; current != null;)
        {
            if (current.run > Limits.MaxNestingDepth)
            {
                pieces.Add(Run(current.run));
                current = current.runBase;
                continue;
            }
            pieces.Add(current.segment);
            current = current.parent;
        }
        pieces.Reverse();
        return string.Concat(pieces);
    }
}
