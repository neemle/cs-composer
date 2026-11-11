using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
internal sealed record SourceOrigin(SourceSnapshot Snapshot, CsNode Original, int Start, int End)
{
    internal int CoreStart { get; init; }
    internal int CoreEnd { get; init; }
    internal int NameStart { get; init; } = -1;
    internal int NameLength { get; init; }
    internal int ListStart { get; init; } = -1;
    internal int ListEnd { get; init; } = -1;
    internal Values<TokenBinding> Bindings { get; init; } = Values<TokenBinding>.Empty;
}
internal readonly record struct TokenBinding(string Property, int Start, int End, string Original);
