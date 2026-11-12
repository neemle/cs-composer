namespace Neemle.CSharpComposer.Model;

public sealed record CsExtension(CsType ReceiverType, string? ReceiverName = null) : CsMember("")
{
    public Values<CsAttribute> ReceiverAttributes { get; init; } = Values<CsAttribute>.Empty;
    public string? ReceiverModifier { get; init; }
    public Values<TypeParameter> TypeParameters { get; init; } = Values<TypeParameter>.Empty;
    public Values<CsMember> Members { get; init; } = Values<CsMember>.Empty;
}
