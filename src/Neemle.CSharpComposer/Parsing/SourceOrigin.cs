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

    // GENE-27 phase 3: the origin of a node the source writes no text of its own for (a brace-less embedded body, the
    // block an `else` statement is wrapped in, a lambda's expression body). It spans the one node it wraps, so an edit
    // below it keeps the source text around that node instead of rendering the owner again (with braces).
    internal bool Embedded { get; init; }

    // An embedded origin for `wrapper`, spanning `inner`'s source.
    internal static SourceOrigin Wrap(CsNode wrapper, SourceOrigin inner) =>
        new(inner.Snapshot, wrapper, inner.Start, inner.End) { CoreStart = inner.CoreStart, CoreEnd = inner.CoreEnd, Embedded = true };

    // The node an embedded origin's owner wraps.
    internal static CsNode Inner(CsNode wrapper) => wrapper switch
    {
        CsBody body => (CsNode?)body.Expression ?? body.Statements[0],
        // A top-level statement's brace-less body, as the symbol tree it is held as (GENE-27 phase 4).
        CsTree tree => tree.Items[0],
        _ => ((CsStatement)wrapper).Body!
    };
}
internal readonly record struct TokenBinding(string Property, int Start, int End, string Original);
