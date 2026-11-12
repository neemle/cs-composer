using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Model;

public abstract record CsNode
{
    public Values<CsComment> Comments { get; init; } = Values<CsComment>.Empty;

    public Values<CsSyntaxTrivia> SyntaxTrivia { get; init; } = Values<CsSyntaxTrivia>.Empty;

    public IReadOnlyList<CsNode> Children => NodeChildren.Get(this);

    internal SyntaxPosition? SyntaxPosition { get; init; }

    internal SourceOrigin? Origin { get; init; }

    internal RenderOptions? Formatting { get; init; }

    public CsNode Detach() => NodeRewrite.Detach(this);

    public ParseResult<CsNode> Parse(ParseContext context) => FragmentParser.Parse(this, context);

    public CsNode Reformat(RenderOptions? options = null) => this with { Formatting = options ?? new() };

    public virtual bool Equals(CsNode? other) => other != null && EqualityContract == other.EqualityContract &&
        Comments.Equals(other.Comments) && SyntaxTrivia.Equals(other.SyntaxTrivia);

    public override int GetHashCode() => HashCode.Combine(EqualityContract, Comments, SyntaxTrivia);
}

internal readonly record struct SyntaxPosition(Guid Source, int Offset, int End);
