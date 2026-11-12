namespace Neemle.CSharpComposer.Model;

public enum OverloadableOperator
{
    Plus, Minus, Multiply, Divide, Modulo, Not, Complement, Increment, Decrement, True, False,
    BitwiseAnd, BitwiseOr, ExclusiveOr, LeftShift, RightShift, UnsignedRightShift,
    Equal, NotEqual, LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual,
    AddAssign, SubtractAssign, MultiplyAssign, DivideAssign, ModuloAssign, AndAssign, OrAssign, XorAssign,
    LeftShiftAssign, RightShiftAssign, UnsignedRightShiftAssign, Implicit, Explicit
}

public sealed record CsOperatorDeclaration(OverloadableOperator Operator, CsType ReturnType) : CsMember("")
{
    public bool IsChecked { get; init; }
    public Values<CsParameter> Parameters { get; init; } = Values<CsParameter>.Empty;
    public CsBody? Body { get; init; }
}
