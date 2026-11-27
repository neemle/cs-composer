namespace Neemle.CSharpComposer.Model;

// Path resolution, the descendant walk and the path rewrite shared by CsDocument and CsTree. All three run in
// loops, so nodes deep inside a long chain stay addressable and editable (GENE-19 D10).
internal static class NodeEdits
{
    // The node at `path`. The 128 cap applies to compressed segments: a run of consecutive steps that each
    // follow a chain spine edge counts as one, and a step that cannot be resolved counts as one.
    internal static CsNode At(CsNode root, NodePath path)
    {
        var segments = path.Segments;
        CsNode node = root;
        int compressed = 0;
        bool previousSpine = false;
        for (int i = 0; i < segments.Count; i++)
        {
            int index = segments[i];
            var children = node.Children;
            if (index < 0 || index >= children.Count)
            {
                if (compressed + segments.Count - i > Limits.MaxNestingDepth) throw PathTooLong();
                throw new ArgumentOutOfRangeException(nameof(path));
            }
            bool spine = index == Spine.Index(node);
            if (!(spine && previousSpine)) compressed++;
            if (compressed > Limits.MaxNestingDepth) throw PathTooLong();
            previousSpine = spine;
            node = children[index];
        }
        return node;
    }

    static ArgumentException PathTooLong() => new("Path exceeds128.", "path");

    internal static IEnumerable<(NodePath Path, CsNode Node)> Descendants(CsNode root, NodePath rootPath)
    {
        var stack = new Stack<(CsNode Node, NodePath Path, int Depth)>();
        stack.Push((root, rootPath, 0));
        while (stack.Count != 0)
        {
            var (node, path, depth) = stack.Pop();
            if (depth > Limits.MaxNestingDepth) throw new ArgumentException("Node graph exceeds128 or contains a cycle.");
            yield return (path, node);
            var children = node.Children;
            int spine = Spine.Index(node);
            for (int i = children.Count - 1; i >= 0; i--) stack.Push((children[i], path.Child(i), i == spine ? depth : depth + 1));
        }
    }

    // Rewrites the node at `path` with `change` and rebuilds every ancestor bottom-up. Each level maps the child
    // NodeRewrite.Children passes at the path's index, exactly as the recursive rewrite did.
    internal static CsNode? Edit(CsNode root, Values<int> path, Func<CsNode?, CsNode?> change)
    {
        var nodes = new List<CsNode> { root };
        for (int level = 0; level < path.Count; level++)
        {
            int index = 0;
            CsNode? found = null;
            NodeRewrite.Children(nodes[level], child => { if (index++ == path[level]) found = child; return child; });
            nodes.Add(found ?? throw new ArgumentOutOfRangeException(nameof(path)));
        }
        var replacement = change(nodes[^1]);
        for (int level = path.Count - 1; level >= 0; level--)
        {
            int index = 0;
            var current = replacement;
            replacement = NodeRewrite.Children(nodes[level], child => index++ == path[level] ? current : child);
        }
        return replacement;
    }
}
