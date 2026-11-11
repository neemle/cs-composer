using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Model;
public sealed partial record CsDocument : CsNode
{
    Values<CsNode> items = Values<CsNode>.Empty;

    bool nullUsings, nullAttributes, nullTypes;

    public CsDocument() { }

    public CsDocument(CsLanguageVersion languageVersion)
    {
        LanguageFeatures.EnsureSupported(languageVersion);
        LanguageVersion = languageVersion;
    }

    public CsLanguageVersion LanguageVersion { get; init; } = CsLanguageVersion.CSharp15;

    public string? Header { get; init; }

    public NullableMode NullableContext { get; init; } = NullableMode.Omit;

    public string? Namespace { get; init; }

    public Values<CsNode> Items { get => items; init => items = value; }

    public Values<CsUsing> Usings
    {
        get => nullUsings ? null! : new(items.OfType<CsUsing>());
        init { nullUsings = value == null; if (value != null) items = NodeItems.Replace(items, value); }
    }

    public Values<CsAttribute> Attributes
    {
        get => nullAttributes ? null! : new(items.OfType<CsAttribute>());
        init { nullAttributes = value == null; if (value != null) items = NodeItems.Replace(items, value); }
    }

    public Values<CsTypeDeclaration> Types
    {
        get => nullTypes ? null! : new(items.OfType<CsTypeDeclaration>());
        init { nullTypes = value == null; if (value != null) items = NodeItems.Replace(items, value); }
    }

    public CsTypeDeclaration? FindType(string name) => Types.FirstOrDefault(x => x.Name == name);

    public IReadOnlyList<GenDiagnostic> Validate() => Validator.Validate(this);

    public string Render(RenderOptions? options = null) => PreservedRenderer.Render(this, options ?? new());
}
