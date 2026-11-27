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

    public CsNode Detach() => NodeRewrite.Detach(this);

    public ParseResult<CsNode> Parse(ParseContext context) => FragmentParser.Parse(this, context, CancellationToken.None);

    public ParseResult<CsNode> Parse(ParseContext context, CancellationToken cancellationToken) => FragmentParser.Parse(this, context, cancellationToken);

    // GENE-29 F4 (AD E): every render formats the whole document; this returns the node unchanged.
    [Obsolete("Every render formats the whole document (GENE-29); Reformat returns its input unchanged and is removed in 0.8.0.")]
    public CsNode Reformat(RenderOptions? options = null) => this;

    public virtual bool Equals(CsNode? other) => other != null && NodeEquals(other);

    public override int GetHashCode() => NodeHash();

    // The CsNode part of equality and hashing, callable on any instance by records that compare a chain
    // iteratively instead of through base calls (GENE-19 D10).
    internal bool NodeEquals(CsNode other) => EqualityContract == other.EqualityContract &&
        Comments.Equals(other.Comments) && SyntaxTrivia.Equals(other.SyntaxTrivia);

    internal int NodeHash() => HashCode.Combine(EqualityContract, Comments, SyntaxTrivia);
}

internal readonly record struct SyntaxPosition(Guid Source, int Offset, int End);
