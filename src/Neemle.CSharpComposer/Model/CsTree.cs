using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;

namespace Neemle.CSharpComposer.Model;

public sealed partial record CsTree(Values<CsNode> Items) : CsNode
{
    public CsLanguageVersion LanguageVersion { get; init; } = CsLanguageVersion.CSharp15;
    public IReadOnlyList<GenDiagnostic> Validate() => SymbolModel.Validate(this);
    public string Render(RenderOptions? options = null) => SymbolModel.Render(this, options ?? new());
    [Obsolete("Every render formats the whole document (GENE-29); Reformat returns its input unchanged and is removed in 0.8.0.")]
    public new CsTree Reformat(RenderOptions? options = null) => this;
}
