using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument
{
    public CsNode At(NodePath path)
    {
        CheckPath(path);
        return NodeEdits.At(this, path);
    }

    void CheckPath(NodePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Owner != null && !ReferenceEquals(this, path.Owner))
            throw new ArgumentException("This path belongs to a different document snapshot.", nameof(path));
    }

    public IEnumerable<(NodePath Path, CsNode Node)> Descendants() => NodeEdits.Descendants(this, new() { Owner = this });

    public CsDocument Replace(NodePath path, CsNode replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        At(path);
        return Finish(NodeEdits.Edit(this, path.Segments, _ => replacement));
    }

    public CsDocument Remove(NodePath path)
    {
        At(path);
        if (path.Segments.Count == 0) throw new ArgumentException("Cannot remove the document root.");
        return Finish(NodeEdits.Edit(this, path.Segments, _ => null));
    }

    public CsDocument Insert(NodePath parent, int childIndex, CsNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        At(parent);
        return Finish(NodeEdits.Edit(this, parent.Segments, node => InsertChild(node!, childIndex, child)));
    }

    public CsDocument Move(NodePath source, NodePath destinationParent, int childIndex)
    {
        var moving = At(source);
        At(destinationParent);
        if (source.Segments.Count == 0 || destinationParent.Segments.Take(source.Segments.Count).SequenceEqual(source.Segments))
            throw new ArgumentException("Cannot move a node into its own subtree.");
        var oldParent = new NodePath(new Values<int>(source.Segments.Take(source.Segments.Count - 1)));
        bool same = oldParent.Segments.Equals(destinationParent.Segments);
        var removed = (CsDocument)NodeEdits.Edit(this, source.Segments, _ => null)!;
        var destination = destinationParent.Segments.ToArray();
        int level = source.Segments.Count - 1;
        if (!same && destination.Length > level && destination.Take(level).SequenceEqual(source.Segments.Take(level)) &&
            destination[level] > source.Segments[level]) destination[level]--;
        if (same && childIndex > source.Segments[^1]) childIndex--;
        return Finish(NodeEdits.Edit(removed, new(destination), n => InsertChild(n!, childIndex, moving)));
    }

    // GENE-29 F4 (AD E): every render formats the whole document, so there is nothing left to mark for reformatting.
    [Obsolete("Every render formats the whole document (GENE-29); Reformat returns its input unchanged and is removed in 0.8.0.")]
    public new CsDocument Reformat(RenderOptions? options = null) => this;

    // The path is still checked: a path that names no node is refused as before.
    [Obsolete("Every render formats the whole document (GENE-29); Reformat returns its input unchanged and is removed in 0.8.0.")]
    public CsDocument Reformat(NodePath path, RenderOptions? options = null)
    {
        At(path);
        return this;
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
