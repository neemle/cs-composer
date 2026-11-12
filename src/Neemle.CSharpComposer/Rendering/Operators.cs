using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class Operators
{
    internal static string Token(CsOperator op) => op switch
    {
        CsOperator.Add or CsOperator.UnaryPlus => "+",
        CsOperator.Subtract or CsOperator.Negate => "-",
        CsOperator.Multiply or CsOperator.Dereference => "*",
        CsOperator.Divide => "/",
        CsOperator.Modulo => "%",
        CsOperator.Equal => "==",
        CsOperator.NotEqual => "!=",
        CsOperator.LessThan => "<",
        CsOperator.LessThanOrEqual => "<=",
        CsOperator.GreaterThan => ">",
        CsOperator.GreaterThanOrEqual => ">=",
        CsOperator.AndAlso => "&&",
        CsOperator.OrElse => "||",
        CsOperator.Coalesce => "??",
        CsOperator.Not => "!",
        CsOperator.Complement => "~",
        CsOperator.BitwiseAnd or CsOperator.AddressOf => "&",
        CsOperator.BitwiseOr => "|",
        CsOperator.ExclusiveOr or CsOperator.FromEnd => "^",
        CsOperator.LeftShift => "<<",
        CsOperator.RightShift => ">>",
        CsOperator.UnsignedRightShift => ">>>",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    internal static int Precedence(CsOperator op) => op switch
    {
        CsOperator.Multiply or CsOperator.Divide or CsOperator.Modulo => 70,
        CsOperator.Add or CsOperator.Subtract => 60,
        CsOperator.LeftShift or CsOperator.RightShift or CsOperator.UnsignedRightShift => 55,
        CsOperator.LessThan or CsOperator.LessThanOrEqual or CsOperator.GreaterThan or CsOperator.GreaterThanOrEqual => 50,
        CsOperator.Equal or CsOperator.NotEqual => 40,
        CsOperator.BitwiseAnd => 37,
        CsOperator.ExclusiveOr => 35,
        CsOperator.BitwiseOr => 33,
        CsOperator.AndAlso => 30,
        CsOperator.OrElse => 20,
        CsOperator.Coalesce => 18,
        _ => 80
    };

    internal static bool Assignment(CsOperator op) => op == CsOperator.None || op >= CsOperator.Add && op <= CsOperator.Modulo ||
        op is CsOperator.Coalesce or CsOperator.BitwiseAnd or CsOperator.BitwiseOr or CsOperator.ExclusiveOr or
        CsOperator.LeftShift or CsOperator.RightShift or CsOperator.UnsignedRightShift;

    internal static string AssignmentToken(CsOperator op) => Assignment(op) ? op == CsOperator.None ? "=" : Token(op) + "=" : throw new ArgumentOutOfRangeException(nameof(op));

    internal static bool Binary(CsOperator op) => op >= CsOperator.Add && op <= CsOperator.OrElse ||
        op >= CsOperator.Coalesce && op <= CsOperator.UnsignedRightShift;

    internal static bool Unary(CsOperator op) => op >= CsOperator.UnaryPlus && op <= CsOperator.Not || op >= CsOperator.Complement && op <= CsOperator.FromEnd;
}
