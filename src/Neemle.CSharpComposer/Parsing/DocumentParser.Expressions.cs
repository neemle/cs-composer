using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
namespace Neemle.CSharpComposer.Parsing;
internal sealed partial class DocumentParser
{
    CsExpression Expression(int minimum = 0, int depth = 0)
    {
        Guard(depth);
        int start = p;
        var left = PrefixExpression(depth + 1);
        while (true)
        {
            if (Current.Text is "." or "(" && minimum <= 90) { left = Postfix(left, start); continue; }
            bool assignment = Current.Text == "=";
            var op = BinaryOperator(Current.Text);
            int precedence = assignment ? 10 : op == CsOperator.None ? -1 : Operators.Precedence(op);
            if (precedence < minimum || precedence < 0) break;
            p++;
            var right = Expression(assignment ? precedence : precedence + 1, depth + 1);
            left = assignment ? Cs.Assign(left, right) : Cs.Binary(op, left, right);
            left = Bind(left, start);
        }
        return left;
    }
    CsExpression PrefixExpression(int depth)
    {
        Guard(depth);
        int start = p;
        string token = Current.Text;
        if (Take("await")) return Bind(Cs.Await(Expression(80, depth + 1)), start);
        if (token is "+" or "-" or "!")
        {
            p++;
            var op = token == "+" ? CsOperator.UnaryPlus : token == "-" ? CsOperator.Negate : CsOperator.Not;
            return Bind(Cs.Unary(op, Expression(80, depth + 1)), start);
        }
        if (Take("("))
        {
            var inner = Expression(0, depth + 1);
            Expect(")");
            return Bind(inner with { Origin = null }, start);
        }
        if (token.Length == 0 || token is ";" or "}" or ")" or ",")
            throw new ParseFailure("Expression expected.");
        bool literal = char.IsDigit(token[0]) || token[0] is '\'' or '"' || token.StartsWith("@\"", StringComparison.Ordinal) ||
            token.StartsWith('$') || token is "true" or "false" or "null";
        if (!literal && token is "new" or "typeof" or "default" or "this" or "base")
            throw new ParseFailure("Expression form is outside the structured expression model.");
        p++;
        return Bind(new CsExpression(literal ? ExpressionKind.Literal : ExpressionKind.Identifier,
            token, Values<CsExpression>.Empty), start, "Text", tokens[start].Start, token.Length);
    }
    CsExpression Postfix(CsExpression left, int start)
    {
        if (Take("."))
        {
            var name = Current;
            Identifier();
            return Bind(left.Member(name.Text), start, "Text", name.Start, name.Text.Length);
        }
        Expect("(");
        var arguments = new List<CsExpression>();
        if (Current.Text != ")") do { arguments.Add(Expression()); } while (Take(","));
        Expect(")");
        return Bind(left.Call(arguments.ToArray()), start);
    }
    static CsOperator BinaryOperator(string token) => token switch
    {
        "+" => CsOperator.Add, "-" => CsOperator.Subtract, "*" => CsOperator.Multiply,
        "/" => CsOperator.Divide, "%" => CsOperator.Modulo, "==" => CsOperator.Equal,
        "!=" => CsOperator.NotEqual, "<" => CsOperator.LessThan, "<=" => CsOperator.LessThanOrEqual,
        ">" => CsOperator.GreaterThan, ">=" => CsOperator.GreaterThanOrEqual,
        "&&" => CsOperator.AndAlso, "||" => CsOperator.OrElse, _ => CsOperator.None
    };
}
