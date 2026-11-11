using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsAttribute(CsType Type)
{
    public string? Target { get; init; }
    public Values<AttributeArgument> Arguments { get; init; } = Values<AttributeArgument>.Empty;
}
