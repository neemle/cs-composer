using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class ExpressionKinds
{
    internal static bool Mutation(ExpressionKind kind) => kind is ExpressionKind.PreIncrement or ExpressionKind.PostIncrement or
        ExpressionKind.PreDecrement or ExpressionKind.PostDecrement;

    internal static bool StatementExpression(ExpressionKind kind) => Mutation(kind) ||
        kind is ExpressionKind.Assignment or ExpressionKind.Invocation or ExpressionKind.Await or ExpressionKind.Raw or ExpressionKind.New;

    internal static bool Assignable(CsExpression expression) =>
        expression.Kind == ExpressionKind.Parenthesized && expression.Arguments.Count == 1 && Assignable(expression.Arguments[0]) ||
        expression.Kind == ExpressionKind.Tuple && expression.Arguments.All(Assignable) ||
        expression.Kind == ExpressionKind.Unary && expression.Operator == CsOperator.Dereference || expression.Kind is
        ExpressionKind.This or ExpressionKind.RefValue or ExpressionKind.Field or ExpressionKind.Declaration or ExpressionKind.Identifier or ExpressionKind.Member or ExpressionKind.Index or ExpressionKind.ImplicitIndex or ExpressionKind.Raw;
}
