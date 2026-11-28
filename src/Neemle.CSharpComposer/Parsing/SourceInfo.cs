using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

// GENE-29 F5 (AD B6): what a parsed root keeps of its source. `Parsed` is the root as the parse returned it, so a checked
// render whose output is the source text reads it back without parsing it again; `Identity` is the SyntaxPosition.Source
// of the nodes a document parse bound.
internal sealed record SourceInfo(SourceSnapshot Snapshot, CsNode Parsed)
{
    internal Guid Identity { get; init; }
}
