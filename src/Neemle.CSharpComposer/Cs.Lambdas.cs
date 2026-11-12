using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer;

public static partial class Cs
{
    public static CsLambdaParameter LambdaParameter(string name, CsType? type = null, string? modifier = null) => new(name, type, modifier);

    public static CsExpression Lambda(CsBody body, params CsLambdaParameter[] parameters) =>
        new(ExpressionKind.Lambda, "", Values<CsExpression>.Empty) { LambdaBody = body, LambdaParameters = parameters };

    public static CsExpression Lambda(CsExpression expression, params CsLambdaParameter[] parameters) => Lambda(CsBody.FromExpression(expression), parameters);
    public static CsExpression AsyncLambda(CsBody body, params CsLambdaParameter[] parameters) => Lambda(body, parameters) with { IsAsync = true };
    public static CsExpression AsyncLambda(CsExpression expression, params CsLambdaParameter[] parameters) => Lambda(expression, parameters) with { IsAsync = true };
}
