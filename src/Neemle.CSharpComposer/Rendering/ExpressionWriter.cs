using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Rendering;

internal static class ExpressionWriter
{
    internal static string Write(CsExpression expression) => expression.Kind switch
    {
        ExpressionKind.Identifier => Names.Id(expression.Text),
        ExpressionKind.Literal => expression.Text,
        ExpressionKind.Raw => Names.NormalizeLines(expression.Text),
        ExpressionKind.Member => Child(expression.Arguments[0], 90) + "." + Names.Id(expression.Text),
        ExpressionKind.Invocation => Invocation(expression),
        ExpressionKind.Await => "await " + Child(expression.Arguments[0], 80),
        ExpressionKind.Assignment => Child(expression.Arguments[0], 11) + " = " + Child(expression.Arguments[1], 10),
        ExpressionKind.Binary => Binary(expression),
        ExpressionKind.Unary => Operators.Token(expression.Operator) + Child(expression.Arguments[0], 81),
        _ => throw new ArgumentOutOfRangeException("e")
    };

    static string Binary(CsExpression expression)
    {
        int precedence = Operators.Precedence(expression.Operator);
        return Child(expression.Arguments[0], precedence) + " " + Operators.Token(expression.Operator) + " " +
            Child(expression.Arguments[1], precedence + 1);
    }

    static string Invocation(CsExpression expression) => Child(expression.Arguments[0], 90) + "(" +
        string.Join(", ", expression.Arguments.Skip(1).Select(Write)) + ")";

    static string Child(CsExpression expression, int minimum)
    {
        string text = Write(expression);
        return Precedence(expression) < minimum ? "(" + text + ")" : text;
    }

    static int Precedence(CsExpression expression) => expression.Kind switch
    {
        ExpressionKind.Assignment => 10, ExpressionKind.Binary => Operators.Precedence(expression.Operator),
        ExpressionKind.Await or ExpressionKind.Unary => 80,
        _ => 100
    };
}
