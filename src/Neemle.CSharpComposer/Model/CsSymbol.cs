namespace Neemle.CSharpComposer.Model;

public sealed record CsSymbol(string? Name, CsSymbolOptions Options) : CsNode;

public sealed record CsSymbolOptions : CsNode
{
    public Values<CsQueryClause> QueryClauses { get; init; } = Values<CsQueryClause>.Empty;
    public Values<CsLambdaParameter> Parameters { get; init; } = Values<CsLambdaParameter>.Empty;
    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;
    public CsTree? Body { get; init; }
    public string? ReturnModifier { get; init; }
    public bool Async { get; init; }
    public bool Static { get; init; }
    public bool HasParameterList { get; init; } = true;
    public CsVariableModifiers VariableModifiers { get; init; }

    public string? Text { get; init; }
    public string? Format { get; init; }
    public CsNode? Alignment { get; init; }
    public CsStringStyle StringStyle { get; init; }
    public bool Utf8 { get; init; }
    public CsSymbol? ParameterSymbols { get; init; }
    public string? ParameterModifier { get; init; }
    public QueryClauseKind QueryKind { get; init; }
    public CsSymbol? Clauses { get; init; }
    public CsNode? EqualityKey { get; init; }
    public string? Into { get; init; }
    public bool Descending { get; init; }
    public SymbolOperation Operation { get; init; }
    public ArgumentModifier ArgumentModifier { get; init; }
    public Values<CsType> TypeArguments { get; init; } = Values<CsType>.Empty;
    public CsType? Type { get; init; }
    public bool AsIndex { get; init; }
    public bool AsCast { get; init; }
    public bool AsTypeOf { get; init; }
    public bool AsDefault { get; init; }
    public bool AsIs { get; init; }
    public bool AsConditional { get; init; }
    public bool AsArray { get; init; }
    public bool AsInitializer { get; init; }
    public bool AsParenthesized { get; init; }
    public bool PointerAccess { get; init; }
    public bool AliasAccess { get; init; }
    public bool ConditionalAccess { get; init; }
    public int ArrayRank { get; init; } = 1;
    public CsPattern? Pattern { get; init; }
    public CsSymbol? Initializer { get; init; }
    public bool AsVar { get; init; }
    public bool AsNew { get; init; }
    public bool AsCall { get; init; }
    public bool AsCompound { get; init; }
    public bool AsAssignment { get; init; }
    public bool Using { get; init; }
    public bool Await { get; init; }
    public CsOperator Operator { get; init; }
    public CsNode? Value { get; init; }
    public CsNode? Target { get; init; }
    public CsSymbol? Arguments { get; init; }
    public Values<CsNode> Parts { get; init; } = Values<CsNode>.Empty;
}
