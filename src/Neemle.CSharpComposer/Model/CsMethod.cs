using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsMethod(CsType ReturnType, string Name) : CsMember(Name)
{
    public Values<CsParameter> Parameters { get; init; } = Values<CsParameter>.Empty;
    public Values<TypeParameter> TypeParameters { get; init; } = Values<TypeParameter>.Empty;
    public CsBody? Body { get; init; }
}
