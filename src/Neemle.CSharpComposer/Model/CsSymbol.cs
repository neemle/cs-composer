namespace Neemle.CSharpComposer.Model;

public sealed record CsSymbol(string? Name, CsSymbolOptions Options) : CsNode
{
    // Record equality, hashing and text, written out so that a member-access, call, element-access or operator
    // chain is walked along its receivers in a loop instead of one nested call per link (GENE-19 D10). Each link
    // is compared through its options with the receiver replaced by one shared marker.
    public bool Equals(CsSymbol? other)
    {
        CsNode? left = this, right = other;
        while (true)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is not CsSymbol a || right is not CsSymbol b)
                return left is not CsSymbol && right is not CsSymbol && EqualityComparer<CsNode?>.Default.Equals(left, right);
            if (!a.NodeEquals(b) || !string.Equals(a.Name, b.Name, StringComparison.Ordinal)) return false;
            var (aLink, aNext) = Link(a.Options);
            var (bLink, bNext) = Link(b.Options);
            if (aNext == null || bNext == null) return EqualityComparer<CsSymbolOptions>.Default.Equals(a.Options, b.Options);
            if (!aLink!.Equals(bLink)) return false;
            left = aNext;
            right = bNext;
        }
    }

    public override int GetHashCode()
    {
        int hash = 0;
        CsNode? current = this;
        while (current is CsSymbol symbol)
        {
            var (link, next) = Link(symbol.Options);
            hash = unchecked(hash * -1521134295 + HashCode.Combine(symbol.NodeHash(), symbol.Name, next == null ? symbol.Options : link));
            current = next;
        }
        return current == null ? hash : unchecked(hash * -1521134295 + current.GetHashCode());
    }

    public override string ToString()
    {
        var output = new System.Text.StringBuilder();
        var suffixes = new Stack<string>();
        CsNode? current = this;
        while (current is CsSymbol symbol)
        {
            var options = symbol.Options;
            if (options?.Target is not CsSymbol target || !ReferenceEquals(Spine.Next(options), target))
            {
                output.Append(symbol.Text());
                break;
            }
            string text = (symbol with { Options = options with { Target = SpineMarker.Instance } }).Text();
            int at = text.IndexOf(SpineMarker.Token, StringComparison.Ordinal);
            output.Append(text, 0, at);
            suffixes.Push(text[(at + SpineMarker.Token.Length)..]);
            current = target;
        }
        while (suffixes.Count != 0) output.Append(suffixes.Pop());
        return output.ToString();
    }

    // The text the compiler would generate for this symbol alone.
    string Text()
    {
        var builder = new System.Text.StringBuilder("CsSymbol { ");
        if (PrintMembers(builder)) builder.Append(' ');
        return builder.Append('}').ToString();
    }

    // The link's options with its receiver replaced by the marker, and the receiver; (null, null) when the
    // options do not continue a chain.
    static (CsSymbolOptions? Link, CsNode? Next) Link(CsSymbolOptions? options)
    {
        if (options == null || Spine.Next(options) is not { } next) return (null, null);
        bool operands = !options.AsIndex && !options.AsCall && options.Operator != CsOperator.None;
        return operands
            ? (options with { Parts = new(new CsNode[] { SpineMarker.Instance }.Concat(options.Parts.Skip(1))) }, next)
            : (options with { Target = SpineMarker.Instance }, next);
    }
}

// Stands in for a chain receiver while one link is compared, hashed or printed on its own.
internal sealed record SpineMarker : CsNode
{
    internal static readonly SpineMarker Instance = new();

    internal static readonly string Token = "\u0001" + Guid.NewGuid().ToString("N") + "\u0001";

    public override string ToString() => Token;
}

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
