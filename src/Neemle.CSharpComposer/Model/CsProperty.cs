using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsProperty(CsType Type, string Name) : CsMember(Name)
{
    public bool IsIndexer { get; init; }

    public Values<CsParameter> Parameters { get; init; } = Values<CsParameter>.Empty;

    public Values<CsAccessor> Accessors { get; init; } = Values<CsAccessor>.Empty;

    public CsExpression? Expression { get; init; }

    public CsExpression? Initializer { get; init; }
}
