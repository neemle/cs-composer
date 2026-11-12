namespace Neemle.CSharpComposer.Model;

public sealed record CsVariableDeclarator(string Name, CsExpression? Initializer = null) : CsNode;

// A for initializer is either a declaration with a shared type, or a list of
// statement expressions. The containing for statement owns the semicolons.
public sealed record CsForInitializer : CsNode
{
    public CsType? Type { get; init; }
    public bool IsVar { get; init; }
    public Values<CsVariableDeclarator> Variables { get; init; } = Values<CsVariableDeclarator>.Empty;
    public Values<CsExpression> Expressions { get; init; } = Values<CsExpression>.Empty;
}
