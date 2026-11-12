using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsStatement SwitchStatement(int start, int depth)
    {
        Guard(depth);
        Expect("("); var value = Expression(0, depth + 1); Expect(")");
        int open = Expect("{").End;
        var sections = new List<CsSwitchSection>();
        while (Current.Text is not ("}" or ""))
        {
            int sectionStart = p;
            var labels = new List<CsSwitchLabel>();
            do
            {
                int labelStart = p;
                CsSwitchLabel label;
                if (Take("default")) label = Cs.Default();
                else
                {
                    Expect("case");
                    var pattern = Pattern(0, depth + 1);
                    // In a case label, a bare underscore is a constant name, not
                    // the switch-expression discard pattern.
                    if (pattern.Kind == PatternKind.Discard)
                        pattern = pattern with { Kind = PatternKind.Constant, Value = Cs.Id("_") };
                    var when = Take("when") ? Expression(0, depth + 1, allowLambda: false) : null;
                    label = Cs.Case(pattern, when);
                }
                Expect(":");
                labels.Add(Bind(label, labelStart));
            } while (Current.Text is "case" or "default");
            int bodyStart = p;
            var statements = new List<CsStatement>();
            while (Current.Text is not ("case" or "default" or "}" or ""))
                statements.Add(Statement(depth + 1));
            // No synthetic braces: a switch section is a statement list, not a block.
            var body = statements.Count == 0 ? Cs.Body() : Bind(Cs.Body(statements.ToArray()), bodyStart);
            sections.Add(Bind(new CsSwitchSection(new(labels), body), sectionStart));
        }
        int close = Expect("}").Start;
        var result = Cs.Switch(value, sections.ToArray());
        result = result with { Comments = ContainerComments(open, close, result.Sections) };
        return Bind(result, start, listStart: open, listEnd: close);
    }

    CsExpression SwitchExpression(CsExpression value, int start, int depth)
    {
        Guard(depth);
        Expect("switch");
        int open = Expect("{").End;
        var arms = new List<CsSwitchArm>();
        while (Current.Text is not ("}" or ""))
        {
            int armStart = p;
            var pattern = Pattern(0, depth + 1);
            var when = Take("when") ? Expression(0, depth + 1, allowLambda: false) : null;
            Expect("=>");
            var expression = Expression(0, depth + 1);
            arms.Add(Bind(Cs.Arm(pattern, expression, when), armStart));
            if (!Take(",")) break;
        }
        int close = Expect("}").Start;
        var result = Cs.SwitchExpression(value, arms.ToArray());
        result = result with { Comments = ContainerComments(open, close, result.Arms) };
        return Bind(result, start);
    }

    CsPattern Pattern(int minimum = 0, int depth = 0)
    {
        Guard(depth);
        int start = p;
        var left = PrimaryPattern(depth + 1);
        while (true)
        {
            int precedence = Current.Text == "or" ? 1 : Current.Text == "and" ? 2 : -1;
            if (precedence < minimum || precedence < 0) break;
            string op = tokens[p++].Text;
            var right = Pattern(precedence + 1, depth + 1);
            left = Bind(op == "and" ? Cs.AndPattern(left, right) : Cs.OrPattern(left, right), start);
        }
        return left;
    }

    CsPattern PrimaryPattern(int depth)
    {
        Guard(depth);
        int start = p;
        if (Take("not")) return Bind(Cs.NotPattern(Pattern(3, depth + 1)), start);
        if (Take("("))
        {
            // Try a parenthesized constant expression before a grouped pattern.
            p = start;
            try { return Bind(Cs.ConstantPattern(Expression(0, depth + 1, allowLambda: false)), start); }
            catch (ParseFailure) { p = start + 1; }
            var nested = Pattern(0, depth + 1); Expect(")");
            return Bind(Cs.ParenthesizedPattern(nested), start);
        }
        if (Take("_")) return Bind(Cs.DiscardPattern(), start);
        if (Take("var"))
        {
            var name = Current; Identifier();
            return Bind(Cs.VarPattern(name.Text), start, "Name", name.Start, name.Text.Length);
        }
        if (Current.Text is "<" or "<=" or ">" or ">=")
        {
            var op = BinaryOperator(tokens[p++].Text);
            return Bind(Cs.RelationalPattern(op, Expression(0, depth + 1, allowLambda: false)), start);
        }
        try
        {
            var type = TypeName(depth + 1);
            if (Current.Text is not ("when" or "and" or "or") && Current.Text.Length != 0 &&
                (char.IsLetter(Current.Text[0]) || Current.Text[0] is '_' or '@'))
            {
                var name = Current; Identifier();
                return Bind(Cs.DeclarationPattern(type, name.Text), start, "Name", name.Start, name.Text.Length);
            }
            // Bare names can denote either a constant or a type; retain name syntax
            // as a constant pattern, leaving symbol binding to the C# compiler.
            if (Cs.PrimitiveNames.Values.Contains(type.Name) || type.Form != TypeForm.Named)
                return Bind(Cs.TypePattern(type), start);
        }
        catch (ParseFailure) { }
        p = start;
        return Bind(Cs.ConstantPattern(Expression(0, depth + 1, allowLambda: false)), start);
    }
}
