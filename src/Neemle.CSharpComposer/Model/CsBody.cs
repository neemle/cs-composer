using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsBody : CsNode
{
    public CsTree? Tree { get; init; }

    public string? Raw { get; init; }

    public CsExpression? Expression { get; init; }

    public Values<CsStatement> Statements { get; init; } = Values<CsStatement>.Empty;

    public CsBody(params CsStatement[] statements)
    {
        Statements = statements;
    }

    public static CsBody FromRaw(string source) => new CsBody
    {
        Raw = source
    };

    public static CsBody FromExpression(CsExpression expression) => new CsBody
    {
        Expression = expression
    };
}
