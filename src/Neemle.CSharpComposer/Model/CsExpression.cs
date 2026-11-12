using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;

public sealed record CsExpression(ExpressionKind Kind, string Text, Values<CsExpression> Arguments) : CsNode
{
    public ArgumentModifier ArgumentModifier { get; init; }
    public Values<CsType> TypeArguments { get; init; } = Values<CsType>.Empty;
    public Values<CsQueryClause> QueryClauses { get; init; } = Values<CsQueryClause>.Empty;

    public string? Format { get; init; }
    public CsExpression? Alignment { get; init; }
    public CsStringStyle StringStyle { get; init; }
    public bool Utf8 { get; init; }

    public CsType? Type { get; init; }

    public CsPattern? Pattern { get; init; }

    public bool PointerAccess { get; init; }
    public bool AliasAccess { get; init; }

    public bool ConditionalAccess { get; init; }

    public int ArrayRank { get; init; } = 1;

    public CsExpression? Initializer { get; init; }

    public CsOperator Operator { get; init; }

    public Values<CsLambdaParameter> LambdaParameters { get; init; } = Values<CsLambdaParameter>.Empty;

    public CsBody? LambdaBody { get; init; }

    public Values<CsAttribute> LambdaAttributes { get; init; } = Values<CsAttribute>.Empty;
    public string? ReturnModifier { get; init; }
    public bool HasParameterList { get; init; } = true;

    public bool IsAsync { get; init; }

    public bool IsStatic { get; init; }

    public Values<CsSwitchArm> Arms { get; init; } = Values<CsSwitchArm>.Empty;

    public CsExpression Member(string name) => new CsExpression(ExpressionKind.Member, name, new[] { this });

    public CsExpression Call(params CsExpression[] arguments) =>
        new CsExpression(ExpressionKind.Invocation, "", new Values<CsExpression>(new[] { this }.Concat(arguments)));

    public CsExpression Await() => new CsExpression(ExpressionKind.Await, "", new[] { this });
}
