namespace Neemle.CSharpComposer.Model;

public sealed record CsDelegate(CsType ReturnType, string Name) : CsMember(Name)
{
    public Values<CsParameter> Parameters { get; init; } = Values<CsParameter>.Empty;
    public Values<TypeParameter> TypeParameters { get; init; } = Values<TypeParameter>.Empty;
}
