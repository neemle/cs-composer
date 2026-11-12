using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class Operators
{
    internal static string Token(CsOperator op)
    {
        if (Unary(op)) return UnaryToken(op);
        if (op >= CsOperator.Add && op <= CsOperator.Modulo) return Arithmetic(op);
        if (op >= CsOperator.Equal && op <= CsOperator.GreaterThanOrEqual) return Comparison(op);
        if (op == CsOperator.AndAlso) return "&&";
        if (op == CsOperator.OrElse) return "||";
        throw new ArgumentOutOfRangeException(nameof(op));
    }

    static string UnaryToken(CsOperator op) => op switch
    {
        CsOperator.UnaryPlus => "+",
        CsOperator.Negate => "-",
        _ => "!"
    };

    static string Arithmetic(CsOperator op) => op switch
    {
        CsOperator.Add => "+",
        CsOperator.Subtract => "-",
        CsOperator.Multiply => "*",
        CsOperator.Divide => "/",
        _ => "%"
    };

    static string Comparison(CsOperator op) => op switch
    {
        CsOperator.Equal => "==",
        CsOperator.NotEqual => "!=",
        CsOperator.LessThan => "<",
        CsOperator.LessThanOrEqual => "<=",
        CsOperator.GreaterThan => ">",
        _ => ">="
    };

    internal static int Precedence(CsOperator op) => op switch
    {
        CsOperator.Multiply or CsOperator.Divide or CsOperator.Modulo => 70,
        CsOperator.Add or CsOperator.Subtract => 60,
        CsOperator.LessThan or CsOperator.LessThanOrEqual or
            CsOperator.GreaterThan or CsOperator.GreaterThanOrEqual => 50,
        CsOperator.Equal or CsOperator.NotEqual => 40,
        CsOperator.AndAlso => 30,
        CsOperator.OrElse => 20,
        _ => 80
    };

    internal static bool Assignment(CsOperator op) => op == CsOperator.None || op >= CsOperator.Add && op <= CsOperator.Modulo;

    internal static string AssignmentToken(CsOperator op) => Assignment(op) ? op == CsOperator.None ? "=" : Token(op) + "=" : throw new ArgumentOutOfRangeException(nameof(op));

    internal static bool Binary(CsOperator op) => op >= CsOperator.Add && op <= CsOperator.OrElse;

    internal static bool Unary(CsOperator op) => op >= CsOperator.UnaryPlus && op <= CsOperator.Not;
}
