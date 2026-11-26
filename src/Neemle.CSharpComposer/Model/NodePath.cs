namespace Neemle.CSharpComposer.Model;

public sealed record NodePath
{
    // A child path built by Child or Descendants keeps a link to its parent and materializes its segments on
    // first read, so walking a long chain costs constant time per node (GENE-19 D10).
    Values<int>? segments;

    readonly NodePath? parent;

    readonly int index;

    public NodePath(Values<int> Segments)
    {
        segments = Segments;
    }

    public NodePath(params int[] segments) : this(new Values<int>(segments)) { }

    NodePath(NodePath parent, int index)
    {
        this.parent = parent;
        this.index = index;
        Owner = parent.Owner;
    }

    public Values<int> Segments
    {
        get => parent == null ? segments! : segments ??= Build();
        init => segments = value;
    }

    internal CsNode? Owner { get; init; }

    public static NodePath Root => new();

    public NodePath Child(int index) => new(this, index);

    public void Deconstruct(out Values<int> Segments) => Segments = this.Segments;

    public bool Equals(NodePath? other) => ReferenceEquals(this, other) || other is not null &&
        EqualityComparer<Values<int>>.Default.Equals(Segments, other.Segments) && EqualityComparer<CsNode?>.Default.Equals(Owner, other.Owner);

    public override int GetHashCode() => HashCode.Combine(Segments, Owner);

    Values<int> Build()
    {
        var reversed = new List<int>();
        var current = this;
        for (; current.segments == null && current.parent != null; current = current.parent) reversed.Add(current.index);
        var prefix = current.segments ?? Values<int>.Empty;
        reversed.Reverse();
        return new(prefix.Concat(reversed));
    }
}
