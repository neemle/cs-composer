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
                    var pattern = ClausePattern(depth + 1);
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
        return Bind(Cs.Switch(value, sections.ToArray()), start, listStart: open, listEnd: close);
    }

    CsExpression SwitchExpression(CsExpression value, int start, int depth)
    {
        Guard(depth);
        Expect("switch");
        Expect("{");
        var arms = new List<CsSwitchArm>();
        while (Current.Text is not ("}" or ""))
        {
            int armStart = p;
            var pattern = ClausePattern(depth + 1);
            var when = Take("when") ? Expression(0, depth + 1, allowLambda: false) : null;
            Expect("=>");
            var expression = Expression(0, depth + 1);
            arms.Add(Bind(Cs.Arm(pattern, expression, when), armStart));
            if (!Take(",")) break;
        }
        Expect("}");
        return Bind(Cs.SwitchExpression(value, arms.ToArray()), start);
    }

    // A case-label or switch-arm pattern: there, `when` after the pattern starts the when clause.
    CsPattern ClausePattern(int depth)
    {
        bool saved = whenClause;
        whenClause = true;
        try { return Pattern(0, depth); }
        finally { whenClause = saved; }
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
            left = Fit(Bind(op == "and" ? Cs.AndPattern(left, right) : Cs.OrPattern(left, right), start), depth);
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
        if (Take("(") && ParenthesizedPattern(start, depth) is { } parenthesized) return parenthesized;
        if (Take("_")) return Bind(Cs.DiscardPattern(), start);
        if (Take("var")) return VarPattern(start, depth);
        if (Current.Text is "<" or "<=" or ">" or ">=")
        {
            var op = BinaryOperator(tokens[p++].Text);
            return Bind(Cs.RelationalPattern(op, Expression(51, depth + 1, allowLambda: false)), start);
        }
        return TypeOrConstantPattern(start, depth);
    }

    // After the `(` of a pattern: a positional pattern, a parenthesized constant or a grouped
    // pattern. Null, with the position back at `(`, when it opens a tuple array type instead.
    CsPattern? ParenthesizedPattern(int start, int depth)
    {
        // `()` and `(name: ...` open a positional pattern (GENE-29 F6: `()` too).
        if (Current.Text == ")" || Peek(1) == ":") { p = start; return RecursivePattern(null, start, depth + 1); }
        // Try a parenthesized constant expression before a grouped pattern, unless the
        // parenthesized region itself holds only a declaration.
        bool tupleArray = false;
        if (!ParenthesizedDeclaration(start, depth))
            try
            {
                var constant = PatternOperand(depth + 1);
                if (constant.Kind == ExpressionKind.Tuple)
                {
                    p = start;
                    return RecursivePattern(null, start, depth + 1);
                }
                // GENE-29 F6: a property clause or a designation after it makes it a positional pattern.
                if (PositionalTail()) { p = start; return RecursivePattern(null, start, depth + 1); }
                // A keyword or array type in parentheses reads as an expression here, but it
                // can only be a type; the grouped-pattern path below models it as one.
                if (!TypeOnlyOperand(constant)) return Bind(Cs.ConstantPattern(constant), start);
                tupleArray = TupleArray(constant);
            }
            catch (ParseFailure) { }
        // `(int, string)[]` is a tuple array type, which the type-pattern path parses.
        if (tupleArray) { p = start; return null; }
        p = start + 1;
        var nested = Pattern(0, depth + 1);
        if (Current.Text == ",") { p = start; return RecursivePattern(null, start, depth + 1); }
        Expect(")");
        if (PositionalTail()) { p = start; return RecursivePattern(null, start, depth + 1); }
        return Bind(Cs.ParenthesizedPattern(nested), start);
    }

    // After `(pattern)`: a property clause or a designation, which only a positional pattern of one subpattern has
    // (`(1) { }`, `(int a) d`); a parenthesized pattern has neither.
    bool PositionalTail() => Current.Text == "{" || Designates();

    // After `var`: a deconstructing designation or a single name.
    CsPattern VarPattern(int start, int depth)
    {
        if (Current.Text == "(")
            return Bind(new CsPattern(PatternKind.Var) { Designation = VariableDesignation(depth + 1) }, start);
        var name = Current; Identifier();
        return Bind(Cs.VarPattern(name.Text), start);
    }

    // A type, declaration or type-led recursive pattern; otherwise a constant pattern.
    CsPattern TypeOrConstantPattern(int start, int depth)
    {
        try
        {
            if (TypeLedPattern(start, depth) is { } typed) return typed;
        }
        catch (ParseFailure) { }
        p = start;
        return Bind(Cs.ConstantPattern(Expression(51, depth + 1, allowLambda: false)), start);
    }

    // Null when the type is a bare name with nothing after it that makes it a type.
    CsPattern? TypeLedPattern(int start, int depth)
    {
        var type = TypeName(depth + 1, stopAtQuestion: true);
        if (Current.Text is "(" or "{") return RecursivePattern(type, start, depth + 1);
        bool definite = Cs.PrimitiveNames.Values.Contains(type.Name) || PatternKeywordTypes.Contains(type.Name) ||
            type.Form != TypeForm.Named;
        // The reserved keywords `is` and `as` after a type continue the expression, and so does
        // `switch` after a type that cannot be a constant: `o is string switch { … }`. After a bare
        // name, the compiler reads `K switch { … }` as a (never constant) switch expression.
        if (Current.Text is not ("when" or "and" or "or" or "is" or "as") && !(definite && Current.Text == "switch") &&
            Current.Text.Length != 0 && Neemle.CSharpComposer.Rendering.Names.IsIdentifier(Current.Text))
        {
            var name = Current; Identifier();
            return Bind(Cs.DeclarationPattern(type, name.Text), start);
        }
        // Bare names can denote either a constant or a type; retain name syntax
        // as a constant pattern, leaving symbol binding to the C# compiler.
        return definite ? Bind(Cs.TypePattern(type), start) : null;
    }

    // Where the current pattern operand starts, or -1 outside one. A cast that opens there, or
    // behind only opening parentheses, is checked against PatternCombinatorFollows.
    int patternOperand = -1;

    bool whenClause;

    // The trial constant of a parenthesized pattern. It stops below relational precedence, as an
    // unparenthesized constant does, so `o is (1) && b` leaves `&& b` to the enclosing expression.
    // (A relational operand is a plain expression: the compiler reads `> (K) or 2` as a cast.)
    CsExpression PatternOperand(int depth)
    {
        int saved = patternOperand;
        patternOperand = p;
        try { return Expression(51, depth, allowLambda: false); }
        finally { patternOperand = saved; }
    }

    // After `(X)` in a pattern operand the compiler reads a contextual `or`/`and` as a pattern
    // combinator, not as a cast operand, when a pattern can follow it: `(K) or (1)`,
    // `(string) or null`, `(K) or < 0`. A following `+`, `-`, `*`, `&` or `^` keeps the cast
    // (`(K) or -1`), and so does a keyword run whose last keyword is the operand itself
    // (`(int)or or > 5`). In a case label or switch arm, `(X) when` starts the when clause.
    bool PatternCombinatorFollows(int open)
    {
        if (patternOperand < 0) return false;
        for (int i = patternOperand; i < open; i++)
            if (tokens[i].Text != "(") return false;
        if (whenClause && Current.Text == "when") return true;
        if (!CombinatorKeyword(Current.Text)) return false;
        int offset = 1;
        bool combinator = true;
        while (CombinatorKeyword(Peek(offset))) { combinator = !combinator; offset++; }
        return combinator == SubpatternStart(Peek(offset));
    }

    static bool CombinatorKeyword(string text) => text is "or" or "and";

    // A token that can start the next pattern. Prefix operators that are also binary operators
    // (`+ - * & ^`) are not among them; the compiler keeps the cast before those.
    static bool SubpatternStart(string text) =>
        SubpatternTokens.Contains(text) || LiteralStart.IsMatch(text) ||
        Neemle.CSharpComposer.Rendering.Names.IsIdentifier(text) && !ContinuationKeywords.Contains(text);

    static readonly HashSet<string> SubpatternTokens =
        new(["(", "[", "{", "<", "<=", ">", ">=", "!", "~", "++", "--"], StringComparer.Ordinal);

    // Reserved keywords that continue an expression rather than start one.
    static readonly HashSet<string> ContinuationKeywords = new(["is", "as", "switch"], StringComparer.Ordinal);

    // A numeric (`2`, `.5`), string, character, interpolated or verbatim literal.
    static readonly System.Text.RegularExpressions.Regex LiteralStart = new(@"^(\d|\.\d|[""'$@])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
