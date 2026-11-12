namespace Neemle.CSharpComposer.Model;

public sealed record CsSymbol(string? Name, CsSymbolOptions Options) : CsNode;

public sealed record CsSymbolOptions : CsNode
{
    public CsType? Type { get; init; }
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
