namespace Neemle.CSharpComposer.Model;

public enum SyntaxTriviaPlacement { Leading, Trailing }

/// <summary>A semantic directive or inactive conditional text attached to a syntax node.</summary>
public sealed record CsSyntaxTrivia(CsNode Content, SyntaxTriviaPlacement Placement = SyntaxTriviaPlacement.Leading) : CsNode
{
    // Parser-owned alignment metadata survives Detach. Callers attach trivia to
    // semantic nodes; they never assemble punctuation or calculate token offsets.
    internal TriviaAnchor? Anchor { get; init; }
}

internal sealed record TriviaAnchor(Values<string> Tokens, int Boundary);
