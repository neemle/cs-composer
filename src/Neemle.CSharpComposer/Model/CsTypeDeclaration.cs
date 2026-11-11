using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsTypeDeclaration(TypeKind Kind, string Name) : CsMember(Name)
{
    public Values<CsMember> Members { get; init; } = Values<CsMember>.Empty;
    public Values<CsType> BaseTypes { get; init; } = Values<CsType>.Empty;
    public Values<TypeParameter> TypeParameters { get; init; } = Values<TypeParameter>.Empty;
    public Values<CsParameter> PrimaryParameters { get; init; } = Values<CsParameter>.Empty;
    public IEnumerable<CsProperty> Properties => Members.OfType<CsProperty>();
    public IEnumerable<CsTypeDeclaration> Types => Members.OfType<CsTypeDeclaration>();

    public CsProperty? FindProperty(string name) => Properties.FirstOrDefault(x => x.Name == name);
    public CsTypeDeclaration? FindType(string name) => Types.FirstOrDefault(x => x.Name == name);
}
