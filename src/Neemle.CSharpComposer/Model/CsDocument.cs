using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsDocument(string FileName)
{
    public string? Header { get; init; }
    public NullableMode NullableContext { get; init; } = NullableMode.Omit;
    public string? Namespace { get; init; }
    public Values<CsUsing> Usings { get; init; } = Values<CsUsing>.Empty;
    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;
    public Values<CsTypeDeclaration> Types { get; init; } = Values<CsTypeDeclaration>.Empty;

    public CsTypeDeclaration? FindType(string name) => Types.FirstOrDefault(x => x.Name == name);
    public IReadOnlyList<GenDiagnostic> Validate() => Validator.Validate(this);
    public string Render(RenderOptions? options = null) => Renderer.Render(this, options ?? new RenderOptions());
}
