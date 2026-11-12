using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsExpression Creation(int start, int depth)
    {
        Guard(depth);
        if (Current.Text == "{")
        {
            var anonymous = Initializer(depth + 1);
            return Bind(anonymous with { Kind = ExpressionKind.AnonymousObject, Origin = null }, start);
        }
        CsType? type = Current.Text is "[" or "(" ? null : TypeName(depth + 1, stopBeforeArray: true);
        if (Current.Text == "[") return ArrayCreation(type, start, depth + 1);
        var arguments = new List<CsExpression>();
        if (Take("(")) { arguments = ArgumentList(depth + 1); Expect(")"); }
        else if (Current.Text != "{") throw new ParseFailure("Constructor arguments or initializer expected.");
        var initializer = Current.Text == "{" ? Initializer(depth + 1) : null;
        return Bind(new CsExpression(ExpressionKind.New, "", new(arguments)) { Type = type, Initializer = initializer }, start);
    }

    CsExpression ArrayCreation(CsType? type, int start, int depth)
    {
        Guard(depth);
        Expect("[");
        int rank = 1;
        var dimensions = new List<CsExpression>();
        if (Current.Text == ",") while (Take(",")) rank++;
        else if (Current.Text != "]") { dimensions = ExpressionList("]", depth + 1); rank = dimensions.Count; }
        Expect("]");
        while (Take("["))
        {
            int innerRank = 1;
            while (Take(",")) innerRank++;
            Expect("]");
            if (type == null) throw new ParseFailure("Inferred arrays cannot specify additional ranks.");
            type = type.Array(innerRank);
        }
        return Bind(new CsExpression(ExpressionKind.Array, "", new(dimensions))
        {
            Type = type,
            ArrayRank = rank,
            Initializer = Current.Text == "{" ? Initializer(depth + 1) : null
        }, start);
    }

    CsExpression Initializer(int depth)
    {
        Guard(depth);
        int start = p;
        int open = Expect("{").End;
        var values = new List<CsExpression>();
        while (Current.Text is not ("}" or ""))
        {
            if (Take("["))
            {
                int indexStart = p - 1;
                var indexes = ExpressionList("]", depth + 1); Expect("]"); Expect("=");
                var target = Bind(new CsExpression(ExpressionKind.ImplicitIndex, "", new(indexes)), indexStart);
                values.Add(Bind(Cs.Assign(target, Expression(0, depth + 1)), indexStart));
            }
            else values.Add(Current.Text == "{" ? Initializer(depth + 1) : Expression(0, depth + 1));
            if (!Take(",")) break;
        }
        int close = Expect("}").Start;
        var expression = new CsExpression(ExpressionKind.Initializer, "", new(values));
        return Bind(expression with { Comments = ContainerComments(open, close, expression.Arguments) }, start);
    }

    CsExpression? Cast(int start, int depth)
    {
        int before = p;
        try
        {
            Expect("("); var type = TypeName(depth + 1); Expect(")");
            string next = Current.Text;
            bool definite = Cs.PrimitiveNames.Values.Contains(type.Name) || type.Form != TypeForm.Named;
            bool operand = next.Length != 0 && next is not ("as" or "is") && (Rendering.Names.IsIdentifier(next) || char.IsDigit(next[0]) || next[0] is '_' or '@' or '"' or '\'' or '$' || next.Length > 1 && next[0] == '.' && char.IsDigit(next[1]) ||
                next is "!" or "~" or "(" || definite && next is "+" or "-" or "++" or "--" or "&" or "*" or "[");
            if (!operand) { p = before; return null; }
            return Bind(new CsExpression(ExpressionKind.Cast, "", new[] { Expression(80, depth + 1) }) { Type = type }, start);
        }
        catch (ParseFailure) { p = before; return null; }
    }
}
