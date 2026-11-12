using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer;

public static partial class Cs
{
    public static CsStatement Switch(CsExpression value, params CsSwitchSection[] sections) =>
        new(StatementKind.Switch, value) { Sections = sections };

    public static CsSwitchSection Section(CsBody body, params CsSwitchLabel[] labels) => new(labels, body);
    public static CsSwitchLabel Case(CsExpression value, CsExpression? when = null) => new(ConstantPattern(value), when);
    public static CsSwitchLabel Case(CsPattern pattern, CsExpression? when = null) => new(pattern, when);
    public static CsSwitchLabel Default() => new();
    public static CsStatement Break() => new(StatementKind.Break);
    public static CsStatement GotoCase(CsExpression value) => new(StatementKind.GotoCase, value);
    public static CsStatement GotoDefault() => new(StatementKind.GotoDefault);

    public static CsExpression SwitchExpression(CsExpression value, params CsSwitchArm[] arms) =>
        new(ExpressionKind.Switch, "", new[] { value }) { Arms = arms };

    public static CsSwitchArm Arm(CsPattern pattern, CsExpression value, CsExpression? when = null) => new(pattern, value, when);
    public static CsExpression ThrowExpression(CsExpression value) => new(ExpressionKind.Throw, "", new[] { value });

    public static CsPattern ConstantPattern(CsExpression value) => new(PatternKind.Constant) { Value = value };
    public static CsPattern DiscardPattern() => new(PatternKind.Discard);
    public static CsPattern DeclarationPattern(CsType type, string name) => new(PatternKind.Declaration) { Type = type, Name = name };
    public static CsPattern VarPattern(string name) => new(PatternKind.Var) { Name = name };
    public static CsPattern TypePattern(CsType type) => new(PatternKind.Type) { Type = type };
    public static CsPattern RelationalPattern(CsOperator op, CsExpression value) => new(PatternKind.Relational) { Operator = op, Value = value };
    public static CsPattern NotPattern(CsPattern pattern) => new(PatternKind.Not) { Patterns = new[] { pattern } };
    public static CsPattern AndPattern(CsPattern left, CsPattern right) => new(PatternKind.And) { Patterns = new[] { left, right } };
    public static CsPattern OrPattern(CsPattern left, CsPattern right) => new(PatternKind.Or) { Patterns = new[] { left, right } };
    public static CsPattern ParenthesizedPattern(CsPattern pattern) => new(PatternKind.Parenthesized) { Patterns = new[] { pattern } };
}
