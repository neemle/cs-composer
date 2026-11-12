namespace Neemle.CSharpComposer.Model;

public enum PatternKind { Constant, Discard, Declaration, Var, Type, Relational, Not, And, Or, Parenthesized }

public sealed record CsPattern(PatternKind Kind) : CsNode
{
    public CsExpression? Value { get; init; }
    public CsType? Type { get; init; }
    public string? Name { get; init; }
    public CsOperator Operator { get; init; }
    public Values<CsPattern> Patterns { get; init; } = Values<CsPattern>.Empty;
}
