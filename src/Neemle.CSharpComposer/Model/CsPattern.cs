namespace Neemle.CSharpComposer.Model;

public enum PatternKind { Constant, Discard, Declaration, Var, Type, Relational, Not, And, Or, Parenthesized, Recursive, Property, List, Slice }

public sealed record CsPattern(PatternKind Kind) : CsNode
{
    public bool HasPositional { get; init; }
    public bool HasProperties { get; init; }
    public Values<CsPattern> Properties { get; init; } = Values<CsPattern>.Empty;
    public CsSymbol? Designation { get; init; }
    public CsExpression? Value { get; init; }
    public CsType? Type { get; init; }
    public string? Name { get; init; }
    public CsOperator Operator { get; init; }
    public Values<CsPattern> Patterns { get; init; } = Values<CsPattern>.Empty;
}
