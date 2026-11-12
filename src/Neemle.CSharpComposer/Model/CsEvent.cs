using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsEvent(CsType Type, string Name) : CsMember(Name)
{
    public Values<CsAccessor> Accessors { get; init; } = Values<CsAccessor>.Empty;

    public Values<CsVariableDeclarator> AdditionalVariables { get; init; } = Values<CsVariableDeclarator>.Empty;

    public CsExpression? Initializer { get; init; }
}
