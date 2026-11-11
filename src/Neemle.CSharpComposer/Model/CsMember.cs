using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public abstract record CsMember(string Name) : CsNode
{
    public Values<string> Modifiers { get; init; } = Values<string>.Empty;
    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;
}
