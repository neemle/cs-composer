using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsField(CsType Type, string Name) : CsMember(Name)
{
    public CsExpression? FixedSize { get; init; }

    public Values<CsVariableDeclarator> AdditionalVariables { get; init; } = Values<CsVariableDeclarator>.Empty;

    public CsExpression? Initializer { get; init; }
}
