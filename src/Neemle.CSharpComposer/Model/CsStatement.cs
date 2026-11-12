using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsStatement(StatementKind Kind, CsExpression? Expression = null, string Text = "") : CsNode
{
    public CsDisabledText? Disabled { get; init; }

    public CsDirective? Directive { get; init; }

    public CsExpression? Binding { get; init; }

    public CsVariableModifiers VariableModifiers { get; init; }

    public CsMethod? Function { get; init; }

    public bool IsAwait { get; init; }

    public CsForInitializer? ForInitializer { get; init; }

    public Values<CsExpression> Iterators { get; init; } = Values<CsExpression>.Empty;

    public CsType? Type { get; init; }

    public bool IsVar { get; init; }

    public CsBody? Body { get; init; }

    public CsStatement? Else { get; init; }

    public Values<CsCatch> Catches { get; init; } = Values<CsCatch>.Empty;

    public CsBody? Finally { get; init; }

    public CsStatement? Resource { get; init; }

    public Values<CsSwitchSection> Sections { get; init; } = Values<CsSwitchSection>.Empty;
}
