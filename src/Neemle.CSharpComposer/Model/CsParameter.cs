using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsParameter(CsType Type, string Name) : CsNode
{
    public string? Modifier { get; init; }

    public CsExpression? Default { get; init; }

    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;
}
