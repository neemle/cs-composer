namespace Neemle.CSharpComposer.Model;
public sealed record NodePath(Values<int> Segments)
{
    public NodePath(params int[] segments) : this(new Values<int>(segments)) { }

    internal CsDocument? Owner { get; init; }

    public static NodePath Root => new();

    public NodePath Child(int index) => new(new Values<int>(Segments.Append(index))) { Owner = Owner };
}
