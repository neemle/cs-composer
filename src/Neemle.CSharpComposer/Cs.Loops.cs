using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer;

public static partial class Cs
{
    public static CsStatement For(CsForInitializer? initializer, CsExpression? condition,
        Values<CsExpression> iterators, CsBody body) => new(StatementKind.For, condition)
        { ForInitializer = initializer, Iterators = iterators, Body = body };

    public static CsStatement For(CsBody body) => For(null, null, Values<CsExpression>.Empty, body);

    public static CsVariableDeclarator Variable(string name, CsExpression? initializer = null) => new(name, initializer);

    public static CsForInitializer ForDeclaration(CsType type, params CsVariableDeclarator[] variables) =>
        new() { Type = type, Variables = variables };

    public static CsForInitializer ForVar(string name, CsExpression initializer) =>
        new() { IsVar = true, Variables = new[] { Variable(name, initializer) } };

    public static CsForInitializer ForExpressions(params CsExpression[] expressions) => new() { Expressions = expressions };

    public static CsStatement ForEach(CsType type, string name, CsExpression collection, CsBody body) =>
        new(StatementKind.ForEach, collection, name) { Type = type, Body = body };

    public static CsStatement ForEach(string name, CsExpression collection, CsBody body) =>
        new(StatementKind.ForEach, collection, name) { IsVar = true, Body = body };

    public static CsStatement While(CsExpression condition, CsBody body) => new(StatementKind.While, condition) { Body = body };
    public static CsStatement DoWhile(CsBody body, CsExpression condition) => new(StatementKind.DoWhile, condition) { Body = body };
    public static CsStatement Continue() => new(StatementKind.Continue);
    public static CsStatement Empty() => new(StatementKind.Empty);

    public static CsExpression PreIncrement(CsExpression operand) => new(ExpressionKind.PreIncrement, "", new[] { operand });
    public static CsExpression PostIncrement(CsExpression operand) => new(ExpressionKind.PostIncrement, "", new[] { operand });
    public static CsExpression PreDecrement(CsExpression operand) => new(ExpressionKind.PreDecrement, "", new[] { operand });
    public static CsExpression PostDecrement(CsExpression operand) => new(ExpressionKind.PostDecrement, "", new[] { operand });
}
