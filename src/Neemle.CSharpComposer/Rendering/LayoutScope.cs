using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

// The parsed containers that enclose the node being rendered, innermost first. Indentation for
// re-rendered or inserted code is inferred from these before the rest of the document is searched.
internal sealed record LayoutScope(SourceOrigin Origin, LayoutScope? Parent);
