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
        if (Take("[")) return ListPattern(start, depth + 1);
        if (Current.Text == "{") return RecursivePattern(null, start, depth + 1);
        if (Take("not")) return Bind(Cs.NotPattern(Pattern(3, depth + 1)), start);
        if (Take("("))
        {
            if (Peek(1) == ":") { p = start; return RecursivePattern(null, start, depth + 1); }
            // Try a parenthesized constant expression before a grouped pattern, unless the
            // parenthesized region itself holds only a declaration. The constant stops below
            // relational precedence, as an unparenthesized constant does, so `o is (1) && b`
            // leaves `&& b` to the enclosing expression.
            bool tupleArray = false;
            if (!ParenthesizedDeclaration(start, depth))
                try
                {
                    var constant = Expression(51, depth + 1, allowLambda: false);
                    if (constant.Kind == ExpressionKind.Tuple)
                    {
                        p = start;
                        return RecursivePattern(null, start, depth + 1);
                    }
                    // A keyword or array type in parentheses reads as an expression here, but it
                    // can only be a type; the grouped-pattern path below models it as one.
                    if (!TypeOnlyOperand(constant)) return Bind(Cs.ConstantPattern(constant), start);
                    tupleArray = TupleArray(constant);
                }
                catch (ParseFailure) { }
            if (!tupleArray)
            {
                p = start + 1;
                var nested = Pattern(0, depth + 1);
                if (Current.Text == ",") { p = start; return RecursivePattern(null, start, depth + 1); }
                Expect(")");
                return Bind(Cs.ParenthesizedPattern(nested), start);
            }
            // `(int, string)[]` is a tuple array type, which the type-pattern path below parses.
            p = start;
        }
        if (Take("_")) return Bind(Cs.DiscardPattern(), start);
        if (Take("var"))
        {
            if (Current.Text == "(") return Bind(new CsPattern(PatternKind.Var) { Designation = VariableDesignation(depth + 1) }, start);
            var name = Current; Identifier();
            return Bind(Cs.VarPattern(name.Text), start, "Name", name.Start, name.Text.Length);
        }
        if (Current.Text is "<" or "<=" or ">" or ">=")
        {
            var op = BinaryOperator(tokens[p++].Text);
            return Bind(Cs.RelationalPattern(op, Expression(51, depth + 1, allowLambda: false)), start);
        }
        try
        {
            var type = TypeName(depth + 1, stopAtQuestion: true);
            if (Current.Text is "(" or "{") return RecursivePattern(type, start, depth + 1);
            if (Current.Text is not ("when" or "and" or "or") && Current.Text.Length != 0 &&
                Neemle.CSharpComposer.Rendering.Names.IsIdentifier(Current.Text))
            {
                var name = Current; Identifier();
                return Bind(Cs.DeclarationPattern(type, name.Text), start, "Name", name.Start, name.Text.Length);
            }
            // Bare names can denote either a constant or a type; retain name syntax
            // as a constant pattern, leaving symbol binding to the C# compiler.
            if (Cs.PrimitiveNames.Values.Contains(type.Name) || PatternKeywordTypes.Contains(type.Name) || type.Form != TypeForm.Named)
                return Bind(Cs.TypePattern(type), start);
        }
        catch (ParseFailure) { }
        p = start;
        return Bind(Cs.ConstantPattern(Expression(51, depth + 1, allowLambda: false)), start);
    }

    bool ParenthesizedDeclaration(int start, int depth)
    {
        p = start + 1;
        try { return GroupedDeclaration(TupleElementExpression(depth + 1)); }
        catch (ParseFailure) { return false; }
        finally { p = start; }
    }

    // Type keywords that can never name a constant in pattern position. An escaped name such as
    // `@string` is an identifier and keeps its `@`, so it never matches. `void` is not a pattern
    // type at all, so a parenthesized `(void)` keeps its constant reading and still renders.
    static readonly HashSet<string> PatternKeywordTypes =
        new(Cs.PrimitiveNames.Values.Where(name => name != "void").Concat(["nint", "nuint"]), StringComparer.Ordinal);

    // The innermost operand of a parenthesized trial constant can only be a type: a type keyword,
    // or an array type. `T[]` reads as an element access without indexes. `G<T>[]` reads as the
    // comparisons `G < T > []` (a shift for `>>`) whose right end is an empty collection `[]`, or
    // an index-less access of one for a jagged array; neither is ever a constant. Only the
    // innermost operand counts: `(int.MaxValue)` is a constant whose receiver is a type keyword.
    static bool TypeOnlyOperand(CsExpression expression)
    {
        while (expression.Kind == ExpressionKind.Parenthesized)
            expression = expression.Arguments[0];
        // `((int, string))`: a tuple is never a constant, so a parenthesized tuple is a
        // parenthesized positional pattern, as an unparenthesized one already is.
        if (expression.Kind == ExpressionKind.Tuple) return true;
        if (expression.Kind == ExpressionKind.Identifier) return PatternKeywordTypes.Contains(expression.Text);
        while (expression.Kind == ExpressionKind.Binary && GenericBracket(expression.Operator))
            expression = expression.Arguments[1];
        return expression.Kind switch
        {
            ExpressionKind.Index => expression.Arguments.Count == 1,
            ExpressionKind.Collection => expression.Arguments.Count == 0,
            _ => false
        };
    }

    // `(int, string)[]` (or a jagged `(int, string)[][]`) reads as index-less accesses of a tuple.
    static bool TupleArray(CsExpression expression)
    {
        while (expression.Kind == ExpressionKind.Index)
            expression = expression.Arguments[0];
        return expression.Kind == ExpressionKind.Tuple;
    }

    static bool GenericBracket(CsOperator op) =>
        op is CsOperator.LessThan or CsOperator.GreaterThan or CsOperator.RightShift or CsOperator.UnsignedRightShift;

    static bool GroupedDeclaration(CsExpression expression)
    {
        while (expression is { Kind: ExpressionKind.Parenthesized, Arguments.Count: 1 })
            expression = expression.Arguments[0];
        return expression.Kind == ExpressionKind.Declaration;
    }
}
