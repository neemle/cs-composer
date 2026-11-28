using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Rendering;

internal static class ExpressionWriter
{
    // GENE-29 F3: an expression the meaning guard forces is written in parentheses (AD D2).
    internal static string Write(CsExpression expression) => MeaningGuard.Parenthesize(expression, Unforced(expression));

    static string Unforced(CsExpression expression)
    {
        if (Spine.Next(expression) != null) return Chain(expression);
        string text = Core(expression);
        if (expression.Kind == ExpressionKind.Lambda) return text;
        bool interior = expression.Kind is not (ExpressionKind.Identifier or ExpressionKind.Literal or ExpressionKind.Raw or ExpressionKind.StringLiteral or ExpressionKind.This or ExpressionKind.Base or ExpressionKind.Field or ExpressionKind.TypeReference or ExpressionKind.InterpolatedString or ExpressionKind.AnonymousMethod) || expression.TypeArguments.Count > 0;
        var owner = interior
            ? expression with { Comments = new(expression.Comments.Where(c => c.Placement != CommentPlacement.Container)) } : expression;
        return InlineComments.Apply(owner, text);
    }

    // A chain spine (GENE-19 D10) is written in two loops instead of one nested call per link: top-down, each
    // link's leading comments and opening parenthesis; then the head; then bottom-up, each link's closing
    // parenthesis, the text after its receiver and its trailing comments. The result is the text the nested
    // calls produce; a link that owns syntax trivia has it applied to its own finished text.
    // GENE-29 F2 (AD C3): the spine is one group. When it breaks, a member access after the chain's first call breaks
    // before its `.` (in a chain of at least two calls), and a `&&`, `||` or `??` link breaks before its operator, each
    // one indent unit further.
    static string Chain(CsExpression top)
    {
        var links = new List<CsExpression>();
        for (var link = top; link != null; link = Spine.Next(link)) links.Add(link);
        var breaks = Breaks(links);
        bool grouped = breaks.Contains(true), indented = false;
        var output = new System.Text.StringBuilder();
        if (grouped) output.Append(Layout.GroupOpen);
        var starts = new int[links.Count];
        var parenthesized = new bool[links.Count];
        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i];
            starts[i] = output.Length;
            foreach (var comment in link.Comments.Where(c => c.Placement is not (CommentPlacement.Trailing or CommentPlacement.Container))) output.Append(InlineComments.Text(comment));
            parenthesized[i] = ParenthesizesReceiver(link) || i + 1 < links.Count && MeaningGuard.Forced(links[i + 1]);
            if (parenthesized[i]) output.Append('(');
        }
        var last = links[^1];
        output.Append(GlobalAlias(last) ? "global" : Write(last.Arguments[0]));
        for (int i = links.Count - 1; i >= 0; i--)
        {
            var link = links[i];
            if (parenthesized[i]) output.Append(')');
            if (breaks[i] && !indented) { output.Append(Layout.IndentOpen); indented = true; }
            if (breaks[i] && link.Kind == ExpressionKind.Member) output.Append(Layout.SoftEmpty);
            output.Append(breaks[i] && link.Kind == ExpressionKind.Binary ? Logical(link) : AfterReceiver(link));
            foreach (var comment in link.Comments.Where(c => c.Placement == CommentPlacement.Trailing)) output.Append(' ').Append(InlineComments.Text(comment));
            if (link.SyntaxTrivia.Count != 0)
            {
                string text = output.ToString(starts[i], output.Length - starts[i]);
                output.Length = starts[i];
                output.Append(SyntaxTriviaWriter.Apply(link, text));
            }
        }
        if (grouped) output.Append(Layout.IndentClose).Append(Layout.GroupClose);
        return output.ToString();
    }

    // Which links of a spine (top first) break when the spine does: logical operators, and member accesses after the
    // first call of a chain with at least two calls.
    static bool[] Breaks(List<CsExpression> links)
    {
        var breaks = new bool[links.Count];
        bool calls = links.Count(l => l.Kind == ExpressionKind.Invocation) >= 2, called = false;
        for (int i = links.Count - 1; i >= 0; i--)
        {
            var link = links[i];
            breaks[i] = link.Kind == ExpressionKind.Binary ? IsLogical(link) :
                link.Kind == ExpressionKind.Member && called && calls && !link.AliasAccess;
            called |= link.Kind == ExpressionKind.Invocation;
        }
        return breaks;
    }

    static bool IsLogical(CsExpression e) => e.Operator is CsOperator.AndAlso or CsOperator.OrElse or CsOperator.Coalesce;

    // A logical operator and its right operand, with a soft break before the operator. A `??` whose right operand is
    // another `??` continues on that operand's operands, so a right-nested `??` chain breaks before every operator.
    static string Logical(CsExpression e)
    {
        var output = new System.Text.StringBuilder();
        while (true)
        {
            int precedence = Operators.Precedence(e.Operator);
            bool right = e.Operator == CsOperator.Coalesce;
            output.Append(Layout.Soft).Append(Operators.Token(e.Operator)).Append(' ').Append(Inside(e));
            var operand = e.Arguments[1];
            if (right && operand is { Kind: ExpressionKind.Binary, Operator: CsOperator.Coalesce, Comments.Count: 0, SyntaxTrivia.Count: 0 })
            {
                output.Append(Child(operand.Arguments[0], precedence + 1));
                e = operand;
                continue;
            }
            return output.Append(right && operand.Kind == ExpressionKind.Throw ? Write(operand) : Child(operand, precedence + (right ? 0 : 1))).ToString();
        }
    }

    // Whether a link writes its receiver in parentheses (GENE-27 D6: a link rendered around its receiver keeps the
    // receiver's source text, which then must not need them).
    internal static bool ParenthesizesReceiver(CsExpression link) => !GlobalAlias(link) && Precedence(link.Arguments[0]) < ReceiverPrecedence(link);

    static bool GlobalAlias(CsExpression e) => e.Kind == ExpressionKind.Member && e.AliasAccess && e.Arguments[0] is { Kind: ExpressionKind.Identifier, Text: "global" };

    // The precedence a link's receiver needs to be written without parentheses.
    static int ReceiverPrecedence(CsExpression e) => e.Kind == ExpressionKind.Binary
        ? Operators.Precedence(e.Operator) + (e.Operator == CsOperator.Coalesce ? 1 : 0) : 90;

    // A link's text after its receiver, as Core writes it.
    static string AfterReceiver(CsExpression e) => e.Kind switch
    {
        ExpressionKind.Member => (e.ConditionalAccess ? "?." : e.PointerAccess ? "->" : e.AliasAccess ? "::" : ".") + Inside(e) + Names.Member(e.Text) + TypeArguments(e),
        ExpressionKind.Invocation => Arguments("(" + Inside(e), e.Arguments.Skip(1), ")"),
        ExpressionKind.Index => (e.ConditionalAccess ? "?[" : "[") + Inside(e) + string.Join(", ", e.Arguments.Skip(1).Select(Write)) + "]",
        _ => BinaryRight(e)
    };

    // A link of a binary operator that is not logical (Logical writes those).
    static string BinaryRight(CsExpression expression) => " " + Operators.Token(expression.Operator) + " " + Inside(expression) +
        Child(expression.Arguments[1], Operators.Precedence(expression.Operator) + 1);

    static string Inside(CsExpression e) => string.Concat(e.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text));

    static string TypeArguments(CsExpression e) => e.TypeArguments.Count == 0 ? "" :
        Names.TypeList("<" + (e.Kind == ExpressionKind.Identifier ? Inside(e) : ""), e.TypeArguments, ">");

    // GENE-29 F2 (AD C3): an argument list, breaking one argument per line.
    internal static string Arguments(string open, IEnumerable<CsExpression> arguments, string close) =>
        Layout.List(open, arguments.Select(a => InlineComments.Entry(a, Write)), close);

    // An initializer's or a with expression's braced list after the text before it: on the same line when flat, on
    // a line of its own when broken.
    static string Lead(string text) => text.StartsWith(Layout.GroupOpen) ? Layout.GroupOpen.ToString() + Layout.Soft + text[1..] : " " + text;

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
        ExpressionKind.Collection => Layout.Braced(Layout.SoftEmpty.ToString(), '[', expression.Arguments.Select(a => InlineComments.Entry(a, Write)),
            expression.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(DocumentationWriter.Comment), ']', padded: false),
        ExpressionKind.Spread => ".." + Inside(expression) + Write(expression.Arguments[0]),
        ExpressionKind.AnonymousObject => "new" + Lead(Initializer(expression)),
        ExpressionKind.StackAlloc => "stackalloc " + (expression.Type == null ? "" : Names.Type(expression.Type)) + "[" + Inside(expression) +
            string.Join(", ", expression.Arguments.Select(Write)) + "]" + (expression.Initializer == null ? "" : Lead(Write(expression.Initializer))),
        ExpressionKind.With => Child(expression.Arguments[0], 75) + " with" + (Inside(expression) is { Length: > 0 } inside ? " " + inside + Write(expression.Initializer!) : Lead(Write(expression.Initializer!))),
        ExpressionKind.ImplicitIndex => "[" + Inside(expression) + string.Join(", ", expression.Arguments.Select(Write)) + "]",
        ExpressionKind.Declaration => (expression.Type == null ? "var" : Names.Type(expression.Type)) + " " + Inside(expression) + (expression.Text == "" ? "(" + string.Join(", ", expression.Arguments.Select(Write)) + ")" : Names.Id(expression.Text)),
        ExpressionKind.New => Arguments((expression.Type == null ? "new" : "new " + Names.Type(expression.Type)) + "(" + Inside(expression), expression.Arguments, ")") +
            (expression.Initializer == null ? "" : Lead(Write(expression.Initializer))),
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
        ExpressionKind.Identifier => Names.Simple(expression.Text) + TypeArguments(expression),
        ExpressionKind.Literal => Layout.Verbatim(expression.Text),
        ExpressionKind.Raw => Names.NormalizeLines(expression.Text),
        ExpressionKind.Member => (expression.AliasAccess && expression.Arguments[0] is { Kind: ExpressionKind.Identifier, Text: "global" } ? "global" : Child(expression.Arguments[0], 90)) + (expression.ConditionalAccess ? "?." : expression.PointerAccess ? "->" : expression.AliasAccess ? "::" : ".") + Inside(expression) + Names.Member(expression.Text) + TypeArguments(expression),
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
        ExpressionKind.Unary => Unary(expression),
        _ => throw new ArgumentOutOfRangeException("e")
    };

    static string ArmCore(CsSwitchArm arm) => PatternWriter.Write(arm.Pattern) +
        (arm.When == null ? "" : " when " + Write(arm.When)) + " => " + Write(arm.Value);

    // GENE-29 F2 (AD C3): a switch expression's arms and an initializer's items are braced lists, which end with their own
    // container comments (F5, AD B2: a comment before the closing brace is the list's).
    static string Switch(CsExpression expression) => Child(expression.Arguments[0], 75) + " switch" + Layout.Braced(Layout.Soft.ToString(), '{',
        expression.Arms.Select(arm => InlineComments.Entry(arm, a => InlineComments.Apply(a, ArmCore(a)))), Tail(expression), '}', padded: true);

    static IEnumerable<string> Tail(CsExpression e) =>
        e.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(DocumentationWriter.Comment);

    static string Binary(CsExpression expression)
    {
        int precedence = Operators.Precedence(expression.Operator);
        bool right = expression.Operator == CsOperator.Coalesce;
        if (IsLogical(expression))
            return Layout.Group(Child(expression.Arguments[0], precedence + (right ? 1 : 0)) + Layout.Indent(Logical(expression)));
        return Child(expression.Arguments[0], precedence) + " " + Operators.Token(expression.Operator) + " " + Inside(expression) +
            Child(expression.Arguments[1], precedence + 1);
    }

    static string Initializer(CsExpression e) => Layout.Braced("", '{', e.Arguments.Select(a => InlineComments.Entry(a, Write)), Tail(e), '}', padded: true);

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
        return "new" + (element == null ? "" : " " + Names.Type(element)) + "[" + Inside(expression) + dimensions + "]" + suffix.ToString() +
            (expression.Initializer == null ? "" : Lead(Write(expression.Initializer)));
    }

    // GENE-29 F4: a prefix operator applies to any unary operand without parentheses (`~~a`, `-(int)x`, `!await t`), so a
    // parsed chain of them renders as it reads and stays within the nesting limit. Only an operand that starts with the
    // operator's own character (`- -a`, `+ ++a`, `& &a`) goes in parentheses, which would otherwise lex as one token.
    static string Unary(CsExpression expression)
    {
        string token = Operators.Token(expression.Operator), inside = Inside(expression), operand = Child(expression.Arguments[0], 80);
        bool merges = inside.Length == 0 && token[^1] is '-' or '+' or '&' && Layout.Flat(operand).StartsWith(token[^1]);
        return token + inside + (merges ? "(" + operand + ")" : operand);
    }

    static string Invocation(CsExpression expression) => Arguments(Child(expression.Arguments[0], 90) + "(" + Inside(expression), expression.Arguments.Skip(1), ")");

    static string Child(CsExpression expression, int minimum)
    {
        string text = Write(expression);
        return Precedence(expression) < minimum ? "(" + text + ")" : text;
    }

    internal static int Precedence(CsExpression expression) => expression.Kind switch
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
