using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;

namespace Neemle.CSharpComposer.Model;

public sealed partial record CsTree(Values<CsNode> Items) : CsNode
{
    public CsLanguageVersion LanguageVersion { get; init; } = CsLanguageVersion.CSharp15;
    public IReadOnlyList<GenDiagnostic> Validate() => SymbolModel.Validate(this);
    public string Render(RenderOptions? options = null) => SymbolModel.Render(this, options ?? new());
    public new CsTree Reformat(RenderOptions? options = null) => this with { Formatting = options ?? new() };
}
