using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class OperatorWriter
{
    internal static string Token(OverloadableOperator op) => op switch
    {
        OverloadableOperator.Plus => "+",
        OverloadableOperator.Minus => "-",
        OverloadableOperator.Multiply => "*",
        OverloadableOperator.Divide => "/",
        OverloadableOperator.Modulo => "%",
        OverloadableOperator.Not => "!",
        OverloadableOperator.Complement => "~",
        OverloadableOperator.Increment => "++",
        OverloadableOperator.Decrement => "--",
        OverloadableOperator.True => "true",
        OverloadableOperator.False => "false",
        OverloadableOperator.BitwiseAnd => "&",
        OverloadableOperator.BitwiseOr => "|",
        OverloadableOperator.ExclusiveOr => "^",
        OverloadableOperator.LeftShift => "<<",
        OverloadableOperator.RightShift => ">>",
        OverloadableOperator.UnsignedRightShift => ">>>",
        OverloadableOperator.Equal => "==",
        OverloadableOperator.NotEqual => "!=",
        OverloadableOperator.LessThan => "<",
        OverloadableOperator.GreaterThan => ">",
        OverloadableOperator.LessThanOrEqual => "<=",
        OverloadableOperator.GreaterThanOrEqual => ">=",
        OverloadableOperator.AddAssign => "+=",
        OverloadableOperator.SubtractAssign => "-=",
        OverloadableOperator.MultiplyAssign => "*=",
        OverloadableOperator.DivideAssign => "/=",
        OverloadableOperator.ModuloAssign => "%=",
        OverloadableOperator.AndAssign => "&=",
        OverloadableOperator.OrAssign => "|=",
        OverloadableOperator.XorAssign => "^=",
        OverloadableOperator.LeftShiftAssign => "<<=",
        OverloadableOperator.RightShiftAssign => ">>=",
        OverloadableOperator.UnsignedRightShiftAssign => ">>>=",
        OverloadableOperator.Implicit => "implicit",
        OverloadableOperator.Explicit => "explicit",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };
}
