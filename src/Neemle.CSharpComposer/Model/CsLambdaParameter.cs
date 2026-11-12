namespace Neemle.CSharpComposer.Model;

public sealed record CsLambdaParameter(string Name, CsType? Type = null, string? Modifier = null) : CsNode
{
    public CsExpression? Default { get; init; }
    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;
}
