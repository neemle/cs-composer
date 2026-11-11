using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsField(CsType Type, string Name) : CsMember(Name)
{
    public CsExpression? Initializer { get; init; }
}
