using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsAccessor(string Kind) : CsNode
{
    public Values<string> Modifiers { get; init; } = Values<string>.Empty;

    public string? Accessibility { get; init; }

    public CsBody? Body { get; init; }

    public Values<CsAttribute> Attributes { get; init; } = Values<CsAttribute>.Empty;
}
