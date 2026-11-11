using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsStatement(StatementKind Kind, CsExpression? Expression = null, string Text = "") : CsNode
{
    public CsType? Type { get; init; }

    public bool IsVar { get; init; }

    public CsBody? Body { get; init; }

    public CsStatement? Else { get; init; }

    public Values<CsCatch> Catches { get; init; } = Values<CsCatch>.Empty;

    public CsBody? Finally { get; init; }

    public CsStatement? Resource { get; init; }
}
