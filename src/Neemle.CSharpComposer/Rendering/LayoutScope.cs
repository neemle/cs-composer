using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal sealed record LayoutScope(SourceOrigin Origin, LayoutScope? Parent);
