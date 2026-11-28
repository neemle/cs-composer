using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Validation;

internal static class GraphValidator
{
    // Pre-order with an explicit stack (GENE-19 D10): a chain spine edge adds no level, and paths are built
    // lazily, so a chain of any length is validated in linear time and memory.
    internal static IReadOnlyList<GenDiagnostic> Validate(CsNode root)
    {
        var errors = new List<GenDiagnostic>();
        var active = new HashSet<CsNode>(ReferenceEqualityComparer.Instance);
        var occurrences = new Occurrences();
        var stack = new Stack<Frame>();
        if (Enter(root, NodePathText.Root("Root"), 0, active, occurrences, errors) is { } first) stack.Push(first);
        while (stack.Count != 0)
        {
            var frame = stack.Peek();
            if (frame.Next == frame.Children.Count) { active.Remove(frame.Node); occurrences.Leave(frame.Node); stack.Pop(); continue; }
            int index = frame.Next++;
            var path = frame.Path.Step(".Children[" + index + "]", index == frame.Spine);
            if (Enter(frame.Children[index], path, index == frame.Spine ? frame.Depth : frame.Depth + 1, active, occurrences, errors) is { } child)
                stack.Push(child);
        }
        return errors;
    }

    // GENE-29 F5 (AD B5): the parsed occurrences met so far, by source span. A node shares its span only with the nodes
    // around it that wrap it (a statement and its expression, a lowered symbol and its statement); any other node with
    // the same span is the same parsed occurrence written twice, which would render its comments twice (GEN074).
    sealed class Occurrences
    {
        readonly HashSet<SyntaxPosition> seen = new();
        readonly Dictionary<SyntaxPosition, int> open = new();

        internal bool Enter(CsNode node)
        {
            if (node.SyntaxPosition is not { } position) return true;
            bool fresh = seen.Add(position) || open.ContainsKey(position);
            open[position] = open.GetValueOrDefault(position) + 1;
            return fresh;
        }

        internal void Leave(CsNode node)
        {
            if (node.SyntaxPosition is not { } position) return;
            if (--open[position] == 0) open.Remove(position);
        }
    }

    sealed class Frame(CsNode node, NodePathText path, int depth, IReadOnlyList<CsNode> children)
    {
        internal readonly CsNode Node = node;
        internal readonly NodePathText Path = path;
        internal readonly int Depth = depth;
        internal readonly IReadOnlyList<CsNode> Children = children;
        internal readonly int Spine = Model.Spine.Index(node);
        internal int Next;
    }

    // Checks one node; returns the frame whose children are still to visit, or null when there are none.
    static Frame? Enter(CsNode? node, NodePathText path, int depth, HashSet<CsNode> active,
        Occurrences occurrences, List<GenDiagnostic> errors)
    {
        if (node == null) { errors.Add(new("GEN002", path.ToString(), "Required node is null.")); return null; }
        if (depth > Limits.MaxNestingDepth || !active.Add(node)) { errors.Add(new("GEN001", path.ToString(), "Node graph exceeds128 or contains a cycle.")); return null; }
        if (!occurrences.Enter(node))
            errors.Add(new("GEN074", path.ToString(), "A parsed occurrence is owned twice; use Move or Detach before copying."));
        try
        {
            if (node is CsSyntaxTrivia trivia && (trivia.Content is not (CsDirective or CsDisabledText) || !Enum.IsDefined(trivia.Placement)))
                errors.Add(new("GEN075", path.ToString(), "Syntax trivia requires a directive or inactive text and a valid placement."));
            if (node is CsComment comment && (comment.Comments.Count != 0 || comment.Documentation != null && comment.Kind != CommentKind.Documentation)) errors.Add(new("GEN075", path.ToString(), "Comment nodes are leaves."));
            if (node is CsDocument d && d.Items.Any(n => n is not (CsUsing or CsAttribute or CsTypeDeclaration or CsDelegate or CsDisabledText or CsDirective or CsNamespace or CsExternAlias or CsGlobalStatement or CsRaw)))
                errors.Add(new("GEN075", path.ToString(), "Invalid document child kind."));
            if (node is CsNamespace n && n.Items.Any(x => x is not (CsUsing or CsTypeDeclaration or CsDelegate or CsDisabledText or CsDirective or CsNamespace or CsExternAlias or CsRaw)))
                errors.Add(new("GEN075", path.ToString(), "Invalid namespace child kind."));
            return new Frame(node, path, depth, node.Children);
        }
        catch (Exception exception) when (exception is NullReferenceException or ArgumentNullException)
        {
            errors.Add(new("GEN002", path.ToString(), "Required child collection is null."));
            active.Remove(node);
            occurrences.Leave(node);
            return null;
        }
    }
}
