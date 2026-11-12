using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record TypeParameter(string Name) : CsNode
{
    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;

    public string? Variance { get; init; }

    public Values<CsConstraint> Constraints { get; init; } = Values<CsConstraint>.Empty;
}
