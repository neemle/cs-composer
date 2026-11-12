namespace Neemle.CSharpComposer.Model;

public sealed record CsCatch(CsBody Body, CsType? Type = null, string? Name = null) : CsNode
{
    public CsExpression? Filter { get; init; }
}
