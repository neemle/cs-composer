using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsStatement ForStatement(int start, int depth)
    {
        Guard(depth);
        Expect("(");
        var initializer = Current.Text == ";" ? null : ForInitializer(depth + 1);
        Expect(";");
        var condition = Current.Text == ";" ? null : Expression(0, depth + 1);
        Expect(";");
        var iterators = ExpressionList(")", depth + 1);
        Expect(")");
        return Bind(Cs.For(initializer, condition, new(iterators), Embedded(depth + 1)), start);
    }

    CsForInitializer ForInitializer(int depth)
    {
        Guard(depth);
        int start = p;
        try
        {
            var modifiers = VariableModifiers();
            bool isVar = Take("var");
            var type = isVar ? null : TypeName(depth + 1);
            var variables = new List<CsVariableDeclarator>();
            do
            {
                int variableStart = p;
                var name = Current; Identifier();
                var value = Take("=") ? Expression(0, depth + 2) : null;
                variables.Add(Bind(Cs.Variable(name.Text, value), variableStart));
            } while (Take(","));
            if (Current.Text != ";") throw new ParseFailure("For declaration expected.");
            return Bind(new CsForInitializer { Type = type, IsVar = isVar, VariableModifiers = modifiers, Variables = new(variables) }, start);
        }
        catch (ParseFailure) { p = start; }
        return Bind(Cs.ForExpressions(ExpressionList(";", depth + 1).ToArray()), start);
    }

    List<CsExpression> ExpressionList(string close, int depth)
    {
        var result = new List<CsExpression>();
        if (Current.Text != close)
            do { result.Add(Expression(0, depth)); } while (Take(","));
        return result;
    }

    CsStatement ForEachStatement(int start, int depth)
    {
        Guard(depth);
        Expect("(");
        if (Current.Text == "(" || Current.Text == "var" && Peek(1) == "(")
        {
            var binding = Current.Text == "(" ? PrefixExpression(depth + 1) : TupleElementExpression(depth + 1);
            Expect("in"); var value = Expression(0, depth + 1); Expect(")");
            return Bind(new CsStatement(StatementKind.ForEach, value) { Binding = binding, Body = Embedded(depth + 1) }, start);
        }
        var modifiers = VariableModifiers();
        bool isVar = Take("var");
        var type = isVar ? null : TypeName(depth + 1);
        var name = Current; Identifier();
        Expect("in");
        var collection = Expression(0, depth + 1); Expect(")");
        var body = Embedded(depth + 1);
        return Bind(isVar ? Cs.ForEach(name.Text, collection, body) : Cs.ForEach(type!, name.Text, collection, body),
            start) with
        { VariableModifiers = modifiers };
    }

    CsStatement WhileStatement(int start, int depth)
    {
        Guard(depth);
        Expect("("); var condition = Expression(0, depth + 1); Expect(")");
        return Bind(Cs.While(condition, Embedded(depth + 1)), start);
    }

    CsStatement DoStatement(int start, int depth)
    {
        Guard(depth);
        var body = Embedded(depth + 1);
        Expect("while"); Expect("("); var condition = Expression(0, depth + 1); Expect(")"); Expect(";");
        return Bind(Cs.DoWhile(body, condition), start);
    }
}
