using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Rendering;

internal static class ExpressionWriter
{
    internal static string Write(CsExpression expression)
    {
        string text = Core(expression);
        if (expression.Kind == ExpressionKind.Lambda) return text;
        bool interior = expression.Kind is not (ExpressionKind.Identifier or ExpressionKind.Literal or ExpressionKind.Raw or ExpressionKind.StringLiteral or ExpressionKind.This or ExpressionKind.Base or ExpressionKind.Field or ExpressionKind.TypeReference or ExpressionKind.InterpolatedString or ExpressionKind.AnonymousMethod) || expression.TypeArguments.Count > 0;
        var owner = interior
            ? expression with { Comments = new(expression.Comments.Where(c => c.Placement != CommentPlacement.Container)) } : expression;
        return InlineComments.Apply(owner, text);
    }

    static string Inside(CsExpression e) => string.Concat(e.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text));

    static string TypeArguments(CsExpression e) => e.TypeArguments.Count == 0 ? "" : "<" + (e.Kind == ExpressionKind.Identifier ? Inside(e) : "") + string.Join(", ", e.TypeArguments.Select(Names.Type)) + ">";

    static string Core(CsExpression expression) => expression.Kind switch
    {
        ExpressionKind.Query => QueryWriter.Write(expression.QueryClauses, Inside(expression)),
        ExpressionKind.StringLiteral => StringWriter.Literal(expression.Text, expression.StringStyle) + (expression.Utf8 ? "u8" : ""),
        ExpressionKind.InterpolatedString => StringWriter.Interpolated(expression),
        ExpressionKind.TypeReference => Names.Type(expression.Type!),
        ExpressionKind.Field => "field",
        ExpressionKind.MakeRef or ExpressionKind.RefType or ExpressionKind.RefValue =>
            (expression.Kind == ExpressionKind.MakeRef ? "__makeref" : expression.Kind == ExpressionKind.RefType ? "__reftype" : "__refvalue") + "(" + Inside(expression) + Write(expression.Arguments[0]) +
            (expression.Type == null ? "" : ", " + Names.Type(expression.Type)) + ")",
        ExpressionKind.This => "this",
        ExpressionKind.Base => "base",
        ExpressionKind.Omitted => "",
        ExpressionKind.As => Child(expression.Arguments[0], 50) + " as " + Inside(expression) + Names.Type(expression.Type!),
        ExpressionKind.SizeOf => "sizeof(" + Inside(expression) + Names.Type(expression.Type!) + ")",
        ExpressionKind.NameOf => "nameof(" + Inside(expression) + Write(expression.Arguments[0]) + ")",
        ExpressionKind.Checked or ExpressionKind.Unchecked or ExpressionKind.Unsafe => expression.Kind.ToString().ToLowerInvariant() + "(" + Inside(expression) + Write(expression.Arguments[0]) + ")",
        ExpressionKind.Ref => "ref " + Inside(expression) + Child(expression.Arguments[0], 10),
        ExpressionKind.NullForgiving => Child(expression.Arguments[0], 90) + Inside(expression) + "!",
        ExpressionKind.Argument => (expression.Text == "" ? "" : Names.Id(expression.Text) + ": ") +
            (expression.ArgumentModifier == ArgumentModifier.None ? "" : expression.ArgumentModifier.ToString().ToLowerInvariant() + " ") + Inside(expression) + Write(expression.Arguments[0]),
        ExpressionKind.Tuple => "(" + Inside(expression) + string.Join(", ", expression.Arguments.Select(Write)) + ")",
        ExpressionKind.Range => Child(expression.Arguments[0], 77) + ".." + Inside(expression) + Child(expression.Arguments[1], 77),
        ExpressionKind.CollectionArguments => "with(" + Inside(expression) + string.Join(", ", expression.Arguments.Select(Write)) + ")",
        ExpressionKind.Collection => "[" + Inside(expression) + string.Join(", ", expression.Arguments.Select(Write)) + "]",
        ExpressionKind.Spread => ".." + Inside(expression) + Write(expression.Arguments[0]),
        ExpressionKind.AnonymousObject => "new " + Initializer(expression),
        ExpressionKind.StackAlloc => "stackalloc " + (expression.Type == null ? "" : Names.Type(expression.Type)) + "[" + Inside(expression) +
            string.Join(", ", expression.Arguments.Select(Write)) + "]" + (expression.Initializer == null ? "" : " " + Write(expression.Initializer)),
        ExpressionKind.With => Child(expression.Arguments[0], 75) + " with " + Inside(expression) + Write(expression.Initializer!),
        ExpressionKind.ImplicitIndex => "[" + Inside(expression) + string.Join(", ", expression.Arguments.Select(Write)) + "]",
        ExpressionKind.Declaration => (expression.Type == null ? "var" : Names.Type(expression.Type)) + " " + Inside(expression) + (expression.Text == "" ? "(" + string.Join(", ", expression.Arguments.Select(Write)) + ")" : Names.Id(expression.Text)),
        ExpressionKind.New => (expression.Type == null ? "new" : "new " + Names.Type(expression.Type)) + "(" + Inside(expression) + string.Join(", ", expression.Arguments.Select(Write)) + ")" +
            (expression.Initializer == null ? "" : " " + Write(expression.Initializer)),
        ExpressionKind.Index => Child(expression.Arguments[0], 90) + (expression.ConditionalAccess ? "?[" : "[") + Inside(expression) +
            string.Join(", ", expression.Arguments.Skip(1).Select(Write)) + "]",
        ExpressionKind.Cast => "(" + Inside(expression) + Names.Type(expression.Type!) + ")" + Child(expression.Arguments[0], 80),
        ExpressionKind.TypeOf => "typeof(" + Inside(expression) + Names.Type(expression.Type!) + ")",
        ExpressionKind.Default => expression.Type == null ? "default" : "default(" + Inside(expression) + Names.Type(expression.Type) + ")",
        ExpressionKind.Is => Child(expression.Arguments[0], 50) + " is " + Inside(expression) + (expression.Type == null ? PatternWriter.Write(expression.Pattern!) : Names.Type(expression.Type)),
        ExpressionKind.Conditional => Child(expression.Arguments[0], 17) + " ? " + Inside(expression) + Write(expression.Arguments[1]) + " : " + Write(expression.Arguments[2]),
        ExpressionKind.Array => Array(expression),
        ExpressionKind.Initializer => Initializer(expression),
        ExpressionKind.Parenthesized => "(" + Inside(expression) + Write(expression.Arguments[0]) + ")",
        ExpressionKind.Lambda or ExpressionKind.AnonymousMethod => LambdaWriter.Write(expression),
        ExpressionKind.Identifier => Names.Id(expression.Text) + TypeArguments(expression),
        ExpressionKind.Literal => expression.Text,
        ExpressionKind.Raw => Names.NormalizeLines(expression.Text),
        ExpressionKind.Member => (expression.AliasAccess && expression.Arguments[0] is { Kind: ExpressionKind.Identifier, Text: "global" } ? "global" : Child(expression.Arguments[0], 90)) + (expression.ConditionalAccess ? "?." : expression.PointerAccess ? "->" : expression.AliasAccess ? "::" : ".") + Inside(expression) + Names.Id(expression.Text) + TypeArguments(expression),
        ExpressionKind.Invocation => Invocation(expression),
        ExpressionKind.Await => "await " + Inside(expression) + Child(expression.Arguments[0], 80),
        ExpressionKind.Assignment => Child(expression.Arguments[0], 11) + " " + Operators.AssignmentToken(expression.Operator) + " " + Inside(expression) + Child(expression.Arguments[1], 10),
        ExpressionKind.PreIncrement => "++" + Inside(expression) + Child(expression.Arguments[0], 81),
        ExpressionKind.PreDecrement => "--" + Inside(expression) + Child(expression.Arguments[0], 81),
        ExpressionKind.PostIncrement => Child(expression.Arguments[0], 90) + Inside(expression) + "++",
        ExpressionKind.PostDecrement => Child(expression.Arguments[0], 90) + Inside(expression) + "--",
        ExpressionKind.Binary => Binary(expression),
        ExpressionKind.Switch => Switch(expression),
        ExpressionKind.Throw => "throw " + Inside(expression) + Write(expression.Arguments[0]),
        ExpressionKind.Unary => Operators.Token(expression.Operator) + Inside(expression) + Child(expression.Arguments[0], 81),
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
        bool right = expression.Operator == CsOperator.Coalesce;
        return Child(expression.Arguments[0], precedence + (right ? 1 : 0)) + " " + Operators.Token(expression.Operator) + " " + Inside(expression) +
            (right && expression.Arguments[1].Kind == ExpressionKind.Throw ? Write(expression.Arguments[1]) : Child(expression.Arguments[1], precedence + (right ? 0 : 1)));
    }

    static string Initializer(CsExpression e)
    {
        var original = e.Origin?.Original is CsExpression old ? old.Arguments : e.Arguments;
        var comments = e.Comments.Where(c => c.Placement == CommentPlacement.Container)
            .Select(c => (Comment: c, Index: c.Origin == null ? 0 : original.Count(a => a.Origin != null && a.Origin.CoreStart < c.Origin.CoreStart))).ToArray();
        var output = new System.Text.StringBuilder("{ ");
        for (int i = 0; i <= e.Arguments.Count; i++)
        {
            foreach (var c in comments.Where(c => Math.Min(c.Index, e.Arguments.Count) == i)) output.Append(InlineComments.Text(c.Comment));
            if (i < e.Arguments.Count) output.Append(Write(e.Arguments[i]) + (i + 1 < e.Arguments.Count ? ", " : ""));
        }
        return output.Append(" }").ToString();
    }

    static string Array(CsExpression expression)
    {
        var element = expression.Type;
        var suffix = new System.Text.StringBuilder();
        while (element is { Form: TypeForm.Array })
        {
            suffix.Insert(0, "[" + new string(',', element.Rank - 1) + "]");
            element = element.Element;
        }
        string dimensions = expression.Arguments.Count == 0 ? new string(',', expression.ArrayRank - 1) :
            string.Join(", ", expression.Arguments.Select(Write));
        return "new " + (element == null ? "" : Names.Type(element)) + "[" + Inside(expression) + dimensions + "]" + suffix +
            (expression.Initializer == null ? "" : " " + Write(expression.Initializer));
    }

    static string Invocation(CsExpression expression) => Child(expression.Arguments[0], 90) + "(" + Inside(expression) +
        string.Join(", ", expression.Arguments.Skip(1).Select(Write)) + ")";

    static string Child(CsExpression expression, int minimum)
    {
        string text = Write(expression);
        return Precedence(expression) < minimum ? "(" + text + ")" : text;
    }

    static int Precedence(CsExpression expression) => expression.Kind switch
    {
        ExpressionKind.Literal when expression.Text.StartsWith('-') || expression.Text.StartsWith('+') => 80,
        ExpressionKind.Lambda or ExpressionKind.AnonymousMethod or ExpressionKind.Assignment or ExpressionKind.Query => 10,
        ExpressionKind.Conditional => 16,
        ExpressionKind.Is or ExpressionKind.As => 50,
        ExpressionKind.Range => 76,
        ExpressionKind.With => 75,
        ExpressionKind.Ref => 10,
        ExpressionKind.Binary => Operators.Precedence(expression.Operator),
        ExpressionKind.Await or ExpressionKind.Unary or ExpressionKind.PreIncrement or ExpressionKind.PreDecrement or ExpressionKind.Cast => 80,
        ExpressionKind.PostIncrement or ExpressionKind.PostDecrement => 89,
        ExpressionKind.Switch => 75,
        ExpressionKind.Throw => 0,
        _ => 100
    };
}
