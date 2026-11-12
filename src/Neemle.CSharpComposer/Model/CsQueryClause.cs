namespace Neemle.CSharpComposer.Model;

public enum QueryClauseKind { From, Let, Where, Join, OrderBy, Order, Select, Group, Into }

/// <summary>A query clause. Source, keys and values remain traversable expressions.</summary>
public sealed record CsQueryClause(QueryClauseKind Kind) : CsNode
{
    public string? Name { get; init; }
    public string? Into { get; init; }
    public CsType? Type { get; init; }
    public CsExpression? Value { get; init; }
    public CsExpression? Key { get; init; }
    public CsExpression? EqualsKey { get; init; }
    public bool Descending { get; init; }
    public Values<CsQueryClause> Clauses { get; init; } = Values<CsQueryClause>.Empty;
}
