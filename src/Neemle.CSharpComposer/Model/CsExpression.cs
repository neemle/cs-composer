using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsExpression(ExpressionKind Kind, string Text, Values<CsExpression> Arguments) : CsNode
{
    public CsOperator Operator { get; init; }

    public CsExpression Member(string name) => new CsExpression(ExpressionKind.Member, name, new[] { this });

    public CsExpression Call(params CsExpression[] arguments) =>
        new CsExpression(ExpressionKind.Invocation, "", new Values<CsExpression>(new[] { this }.Concat(arguments)));

    public CsExpression Await() => new CsExpression(ExpressionKind.Await, "", new[] { this });
}
