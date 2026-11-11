using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument
{
    public CsNode At(NodePath path)
    {
        CheckPath(path);
        CsNode node = this;
        foreach (int index in path.Segments)
        {
            if (index < 0 || index >= node.Children.Count) throw new ArgumentOutOfRangeException(nameof(path));
            node = node.Children[index];
        }
        return node;
    }
    void CheckPath(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Owner != null && !ReferenceEquals(this, path.Owner))
            throw new ArgumentException("This path belongs to a different document snapshot.", nameof(path));
        if (path.Segments.Count > 128) throw new ArgumentException("Path exceeds128.", nameof(path));
    }
    public IEnumerable<(NodePath Path, CsNode Node)> Descendants() => Walk(this, new() { Owner = this }, 0);
    static IEnumerable<(NodePath, CsNode)> Walk(CsNode node, NodePath path, int depth)
    {
        if (depth > 128) throw new ArgumentException("Node graph exceeds128 or contains a cycle.");
        yield return (path, node);
        for (int i = 0; i < node.Children.Count; i++)
            foreach (var result in Walk(node.Children[i], path.Child(i), depth + 1)) yield return result;
    }
    public CsDocument Replace(NodePath path, CsNode replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        At(path);
        return Finish(Edit(this, path.Segments, 0, _ => replacement));
    }
    public CsDocument Remove(NodePath path)
    {
        At(path);
        if (path.Segments.Count == 0) throw new ArgumentException("Cannot remove the document root.");
        return Finish(Edit(this, path.Segments, 0, _ => null));
    }
    public CsDocument Insert(NodePath parent, int childIndex, CsNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        At(parent);
        return Finish(Edit(this, parent.Segments, 0, node => InsertChild(node!, childIndex, child)));
    }
    public CsDocument Move(NodePath source, NodePath destinationParent, int childIndex)
    {
        var moving = At(source);
        At(destinationParent);
        if (source.Segments.Count == 0 || destinationParent.Segments.Take(source.Segments.Count).SequenceEqual(source.Segments))
            throw new ArgumentException("Cannot move a node into its own subtree.");
        var oldParent = new NodePath(new Values<int>(source.Segments.Take(source.Segments.Count - 1)));
        bool same = oldParent.Segments.Equals(destinationParent.Segments);
        var removed = (CsDocument)Edit(this, source.Segments, 0, _ => null)!;
        var destination = destinationParent.Segments.ToArray();
        int level = source.Segments.Count - 1;
        if (!same && destination.Length > level && destination.Take(level).SequenceEqual(source.Segments.Take(level)) &&
            destination[level] > source.Segments[level]) destination[level]--;
        if (same && childIndex > source.Segments[^1]) childIndex--;
        return Finish(Edit(removed, new(destination), 0, n => InsertChild(n!, childIndex, moving)));
    }
    public new CsDocument Reformat(RenderOptions? options = null) => this with { Formatting = options ?? new() };
    public CsDocument Reformat(NodePath path, RenderOptions? options = null) => Replace(path, At(path).Reformat(options));
    static CsNode? Edit(CsNode node, Values<int> path, int offset, Func<CsNode?, CsNode?> change)
    {
        if (offset == path.Count) return change(node);
        int index = 0;
        return NodeRewrite.Children(node, child => index++ == path[offset] ? Edit(child, path, offset + 1, change) : child);
    }
    CsDocument Finish(CsNode? node)
    {
        if (node is not CsDocument document) throw new ArgumentException("Root replacement must be a document.");
        var errors = document.Validate();
        if (errors.Count != 0) throw new GenValidationException(errors);
        return document;
    }
    static CsNode InsertChild(CsNode parent, int index, CsNode child)
    {
        if (index < 0 || index > parent.Children.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return NodeInsertion.Insert(parent, index, child);
    }
}
