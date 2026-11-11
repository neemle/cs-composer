using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument : CsNode
{
    Values<CsNode> items = Values<CsNode>.Empty;
    public CsDocument() { }
    public CsDocument(CsLanguageVersion languageVersion)
    {
        LanguageFeatures.EnsureSupported(languageVersion);
        LanguageVersion = languageVersion;
    }
    public CsLanguageVersion LanguageVersion { get; init; } = CsLanguageVersion.CSharp14;
    public string? Header { get; init; }
    public NullableMode NullableContext { get; init; } = NullableMode.Omit;
    public string? Namespace { get; init; }
    public Values<CsNode> Items { get => items; init => items = value; }
    public Values<CsUsing> Usings
    {
        get => new(items.OfType<CsUsing>());
        init => items = NodeItems.Replace(items, value);
    }
    public Values<CsAttribute> Attributes
    {
        get => new(items.OfType<CsAttribute>());
        init => items = NodeItems.Replace(items, value);
    }
    public Values<CsTypeDeclaration> Types
    {
        get => new(items.OfType<CsTypeDeclaration>());
        init => items = NodeItems.Replace(items, value);
    }
    public CsTypeDeclaration? FindType(string name) => Types.FirstOrDefault(x => x.Name == name);
    public IReadOnlyList<GenDiagnostic> Validate() => Validator.Validate(this);
    public string Render(RenderOptions? options = null) => PreservedRenderer.Render(this, options ?? new());
}
