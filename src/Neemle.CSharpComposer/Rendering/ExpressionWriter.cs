using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Rendering;

internal static class ExpressionWriter
{
    internal static string Write(CsExpression expression) => expression.Kind switch
    {
        ExpressionKind.New => "new " + Names.Type(expression.Type!) + "(" + string.Join(", ", expression.Arguments.Select(Write)) + ")",
        ExpressionKind.Lambda => LambdaWriter.Write(expression),
        ExpressionKind.Identifier => Names.Id(expression.Text),
        ExpressionKind.Literal => expression.Text,
        ExpressionKind.Raw => Names.NormalizeLines(expression.Text),
        ExpressionKind.Member => Child(expression.Arguments[0], 90) + "." + Names.Id(expression.Text),
        ExpressionKind.Invocation => Invocation(expression),
        ExpressionKind.Await => "await " + Child(expression.Arguments[0], 80),
        ExpressionKind.Assignment => Child(expression.Arguments[0], 11) + " " + Operators.AssignmentToken(expression.Operator) + " " + Child(expression.Arguments[1], 10),
        ExpressionKind.PreIncrement => "++" + Child(expression.Arguments[0], 81),
        ExpressionKind.PreDecrement => "--" + Child(expression.Arguments[0], 81),
        ExpressionKind.PostIncrement => Child(expression.Arguments[0], 90) + "++",
        ExpressionKind.PostDecrement => Child(expression.Arguments[0], 90) + "--",
        ExpressionKind.Binary => Binary(expression),
        ExpressionKind.Switch => Switch(expression),
        ExpressionKind.Throw => "throw " + Write(expression.Arguments[0]),
        ExpressionKind.Unary => Operators.Token(expression.Operator) + Child(expression.Arguments[0], 81),
        _ => throw new ArgumentOutOfRangeException("e")
    };

    internal static string Arm(CsSwitchArm arm) => InlineComments.Apply(arm, ArmCore(arm));

    static string ArmCore(CsSwitchArm arm) => PatternWriter.Write(arm.Pattern) +
        (arm.When == null ? "" : " when " + Write(arm.When)) + " => " + Write(arm.Value);

    static string Switch(CsExpression expression)
    {
        var arms = expression.Arms;
        var original = expression.Origin?.Original is CsExpression old ? old.Arms : arms;
        var comments = expression.Comments.Where(c => c.Placement == CommentPlacement.Container)
            .Select(c => (Comment: c, Index: c.Origin == null ? 0 : original.Count(a =>
                a.Origin != null && a.Origin.CoreStart < c.Origin.CoreStart))).ToArray();
        var output = new System.Text.StringBuilder(Child(expression.Arguments[0], 75) + " switch { ");
        for (int i = 0; i <= arms.Count; i++)
        {
            foreach (var comment in comments.Where(c => Math.Min(c.Index, arms.Count) == i))
                output.Append(InlineComments.Text(comment.Comment));
            if (i < arms.Count)
                output.Append(InlineComments.Apply(arms[i], ArmCore(arms[i]) + (i < arms.Count - 1 ? ", " : "")));
        }
        return output.Append(" }").ToString();
    }

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
        ExpressionKind.Lambda or ExpressionKind.Assignment => 10,
        ExpressionKind.Binary => Operators.Precedence(expression.Operator),
        ExpressionKind.Await or ExpressionKind.Unary or ExpressionKind.PreIncrement or ExpressionKind.PreDecrement => 80,
        ExpressionKind.PostIncrement or ExpressionKind.PostDecrement => 89,
        ExpressionKind.Switch => 75,
        ExpressionKind.Throw => 0,
        _ => 100
    };
}
