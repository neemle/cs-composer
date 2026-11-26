using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    // The loop builds left-associative and postfix chains iteratively. Every new link is checked against the
    // limit; a binary, member-access, invocation or element-access link over another such link adds no level
    // (a chain spine, GENE-19 D10), and the check measures only nodes not measured before.
    CsExpression Expression(int minimum, int depth, bool allowLambda = true)
    {
        Guard(depth);
        int start = p;
        if (allowLambda && minimum <= 10 && LambdaAhead(depth)) return LambdaExpression(depth);
        if (minimum <= 10 && QueryAhead()) return QueryExpression(depth);
        var left = PrefixExpression(depth);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var link = Link(left, minimum, depth, start, allowLambda);
            if (link == null) return left;
            left = Fit(link, depth);
        }
    }

    CsExpression? Link(CsExpression left, int minimum, int depth, int start, bool allowLambda)
    {
        // After an is-expression, `?` followed by `[` opens a conditional, not an element access.
        if ((Current.Text is "." or "?." or "->" or "::" or "(" or "[" || Current.Text == "?" && Peek(1) == "[" && left.Kind != ExpressionKind.Is) && minimum <= 90) return Postfix(left, start, depth);
        if (Current.Text is "++" or "--" && minimum <= 90)
        {
            Writable(left, "Increment and decrement require a writable target.");
            bool increment = Take("++"); if (!increment) Expect("--");
            return Bind(increment ? Cs.PostIncrement(left) : Cs.PostDecrement(left), start);
        }
        if (Current.Text == "switch" && minimum <= 75) return SwitchExpression(left, start, depth);
        if (Current.Text == "is" && minimum <= 50)
        {
            p++;
            if (NullableIsType(depth) is { } nullableType)
                return Bind(new CsExpression(ExpressionKind.Is, "", new[] { left }) { Type = nullableType }, start);
            var pattern = Pattern(0, depth + 1);
            var test = pattern.Kind == PatternKind.Type
                ? Bind(new CsExpression(ExpressionKind.Is, "", new[] { left }) { Type = pattern.Type }, start)
                : Bind(new CsExpression(ExpressionKind.Is, "", new[] { left }) { Pattern = pattern }, start);
            RejectTighterThanIs(test.Pattern == null);
            return test;
        }
        if (Current.Text == "?" && minimum <= 16)
        {
            p++; var yes = Expression(0, depth + 1); Expect(":"); var no = Expression(10, depth + 1);
            return Bind(new CsExpression(ExpressionKind.Conditional, "", new[] { left, yes, no }), start);
        }
        if (Current.Text == "<" && minimum <= 90 && left.Kind is ExpressionKind.Identifier or ExpressionKind.Member)
        {
            var generic = GenericReference(left, start, depth);
            if (!ReferenceEquals(generic, left)) return generic;
        }
        if (Current.Text == "!" && minimum <= 90)
        { p++; return Bind(new CsExpression(ExpressionKind.NullForgiving, "", new[] { left }), start); }
        if (Current.Text == ".." && minimum <= 76) { p++; return Range(left, start, depth); }
        if (Current.Text == "as" && minimum <= 50)
        { p++; var type = TypeName(depth + 1); return Bind(new CsExpression(ExpressionKind.As, "", new[] { left }) { Type = type }, start); }
        if (Current.Text == "with" && Peek(1) == "{" && minimum <= 75)
        { p++; return Bind(new CsExpression(ExpressionKind.With, "", new[] { left }) { Initializer = Initializer(depth + 1) }, start); }
        string operatorToken = OperatorToken(out int operatorWidth);
        var compound = CompoundOperator(operatorToken);
        bool assignment = operatorToken == "=" || compound != CsOperator.None;
        var op = assignment ? compound : BinaryOperator(operatorToken);
        int precedence = assignment ? 10 : op == CsOperator.None ? -1 : Operators.Precedence(op);
        if (precedence < minimum || precedence < 0) return null;
        if (assignment) Writable(left, "An assignment requires a writable target.");
        p += operatorWidth;
        var right = Expression(assignment || op == CsOperator.Coalesce ? precedence : precedence + 1, depth + 1, allowLambda);
        return Bind(assignment ? Cs.Assign(left, right) with { Operator = op } : Cs.Binary(op, left, right), start);
    }

    // The validator rejects these targets (GEN088), so they never parse as typed nodes: the enclosing
    // statement or member is kept as raw source instead (GENE-19 release invariant).
    static void Writable(CsExpression target, string message)
    {
        if (!ExpressionKinds.Assignable(target)) throw new ParseFailure(message);
    }

    static readonly HashSet<string> TighterThanIs = new(StringComparer.Ordinal) { ".", "?.", "->", "::", "(", "[", "++", "--", "!", "switch", "with", ".." };

    // The compiler ends an is-expression at its pattern: no operator binding tighter than `is` applies
    // to it (CS1073, CS1002). After a bare type it reads a tighter binary operator as part of a constant
    // pattern instead (CS1525), so neither form may continue as a typed node.
    void RejectTighterThanIs(bool typeOnly)
    {
        var op = BinaryOperator(OperatorToken(out _));
        if (op != CsOperator.None && Operators.Precedence(op) > 50 || !typeOnly && TighterThanIs.Contains(Current.Text))
            throw new ParseFailure("An is-expression cannot be followed by an operator that binds tighter than 'is'.");
    }

    CsExpression PrefixExpression(int depth)
    {
        Guard(depth);
        int start = p;
        string token = Current.Text;
        if (token is "++" or "--")
        {
            p++; var operand = Expression(80, depth + 1);
            Writable(operand, "Increment and decrement require a writable target.");
            return Bind(token == "++" ? Cs.PreIncrement(operand) : Cs.PreDecrement(operand), start);
        }
        if (token == "var" && Peek(1) == "(") return TupleElementExpression(depth);
        if (token == "field" && inAccessor) { p++; return Bind(new CsExpression(ExpressionKind.Field, "", Values<CsExpression>.Empty), start); }
        if (token is "__makeref" or "__reftype" or "__refvalue")
        {
            p++; Expect("("); var operand = Expression(0, depth + 1); CsType? type = null;
            if (token == "__refvalue") { Expect(","); type = TypeName(depth + 1); }
            Expect(")");
            return Bind(new CsExpression(token == "__makeref" ? ExpressionKind.MakeRef : token == "__reftype" ? ExpressionKind.RefType : ExpressionKind.RefValue, "", new[] { operand }) { Type = type }, start);
        }
        if (Cs.PrimitiveNames.Values.Contains(token) && Peek(1) == "." || token is "nint" or "nuint" && Peek(1) == ".")
        { p++; return Bind(new CsExpression(ExpressionKind.TypeReference, "", Values<CsExpression>.Empty) { Type = new CsType(token) }, start); }
        if (token == "delegate" || token == "async" && Peek(1) == "delegate") return AnonymousMethod(depth);
        if (token == "nameof" && Peek(1) == "(")
        {
            p++;
            try { return Operation(ExpressionKind.NameOf, start, depth); }
            catch (ParseFailure) { p = start; }
        }
        if (token is "this" or "base" or "sizeof" or "checked" or "unchecked" or "unsafe" or "ref" or "stackalloc")
        {
            p++;
            var kind = token switch
            {
                "this" => ExpressionKind.This,
                "base" => ExpressionKind.Base,
                "sizeof" => ExpressionKind.SizeOf,
                "nameof" => ExpressionKind.NameOf,
                "checked" => ExpressionKind.Checked,
                "unchecked" => ExpressionKind.Unchecked,
                "ref" => ExpressionKind.Ref,
                "unsafe" => ExpressionKind.Unsafe,
                _ => ExpressionKind.StackAlloc
            };
            return Operation(kind, start, depth);
        }
        if (Take("[")) return Collection(start, depth);
        if (Take("..")) return Range(null, start, depth);
        if (Take("new")) return Creation(start, depth);
        if (Current.Text == "{") return Initializer(depth);
        if (Take("typeof"))
        { Expect("("); var type = TypeName(depth + 1); Expect(")"); return Bind(new CsExpression(ExpressionKind.TypeOf, "", Values<CsExpression>.Empty) { Type = type }, start); }
        if (Take("default"))
        {
            CsType? type = null;
            if (Take("(")) { type = TypeName(depth + 1); Expect(")"); }
            return Bind(new CsExpression(ExpressionKind.Default, "", Values<CsExpression>.Empty) { Type = type }, start);
        }
        if (Take("throw")) return Bind(Cs.ThrowExpression(Expression(0, depth + 1)), start);
        if (inAsync && Take("await")) return Bind(Cs.Await(Expression(80, depth + 1)), start);
        if (token is "+" or "-" or "!" or "~" or "&" or "*" or "^")
        {
            p++;
            var op = token switch
            {
                "+" => CsOperator.UnaryPlus,
                "-" => CsOperator.Negate,
                "!" => CsOperator.Not,
                "~" => CsOperator.Complement,
                "&" => CsOperator.AddressOf,
                "*" => CsOperator.Dereference,
                _ => CsOperator.FromEnd
            };
            return Bind(Cs.Unary(op, Expression(80, depth + 1)), start);
        }
        if (Current.Text == "(" && Cast(start, depth) is { } cast) return cast;
        if (Take("("))
        {
            var inner = TupleElementExpression(depth + 1);
            if (Take(","))
            {
                var elements = new List<CsExpression> { inner };
                do { elements.Add(TupleElementExpression(depth + 1)); } while (Take(","));
                Expect(")"); return Bind(new CsExpression(ExpressionKind.Tuple, "", new(elements)), start);
            }
            Expect(")");
            // A lone `(T name)` or `(var name)` is not a declaration expression; only tuples, foreach and patterns bind one.
            if (inner is { Kind: ExpressionKind.Declaration, Arguments.Count: 0 }) throw new ParseFailure("Declaration expression requires a tuple.");
            return Bind(new CsExpression(ExpressionKind.Parenthesized, "", new[] { inner }), start);
        }
        if (token.Length == 0 || token is ";" or "}" or ")" or ",")
            throw new ParseFailure("Expression expected.");
        bool literal = char.IsDigit(token[0]) || token[0] == '.' && token.Length > 1 && char.IsDigit(token[1]) || token[0] is '\'' or '"' || token.StartsWith("@\"", StringComparison.Ordinal) ||
            token.StartsWith('$') || token is "true" or "false" or "null";
        if (!literal && token is "new" or "typeof" or "default" or "this" or "base")
            throw new ParseFailure("Expression form is outside the structured expression model.");
        p++;
        if (token.StartsWith('$') || token.StartsWith("@$", StringComparison.Ordinal)) return InterpolatedString(token, start, depth);
        return Bind(new CsExpression(literal ? ExpressionKind.Literal : ExpressionKind.Identifier,
            token, Values<CsExpression>.Empty), start, "Text", tokens[start].Start, token.Length);
    }

    CsExpression Postfix(CsExpression left, int start, int depth)
    {
        bool conditionalMember = Take("?."); bool pointer = Take("->"); bool alias = Take("::");
        if (conditionalMember || pointer || alias || Take("."))
        {
            var name = Current;
            Identifier();
            return Bind(left.Member(name.Text) with { ConditionalAccess = conditionalMember, PointerAccess = pointer, AliasAccess = alias }, start, "Text", name.Start, name.Text.Length);
        }
        bool conditionalIndex = Take("?");
        if (Take("["))
        {
            var indexes = ArgumentList(depth + 1, "]"); Expect("]");
            return Bind(new CsExpression(ExpressionKind.Index, "", new(new[] { left }.Concat(indexes))) { ConditionalAccess = conditionalIndex }, start);
        }
        if (conditionalIndex) throw new ParseFailure("Conditional element access expected.");
        Expect("(");
        var arguments = new List<CsExpression>();
        if (Current.Text != ")") do { arguments.Add(ArgumentExpression(depth + 1)); } while (Take(","));
        Expect(")");
        return Bind(left.Call(arguments.ToArray()), start);
    }

    static CsOperator CompoundOperator(string token) => token switch
    {
        "+=" => CsOperator.Add,
        "-=" => CsOperator.Subtract,
        "*=" => CsOperator.Multiply,
        "/=" => CsOperator.Divide,
        "%=" => CsOperator.Modulo,
        "??=" => CsOperator.Coalesce,
        "&=" => CsOperator.BitwiseAnd,
        "|=" => CsOperator.BitwiseOr,
        "^=" => CsOperator.ExclusiveOr,
        "<<=" => CsOperator.LeftShift,
        ">>=" => CsOperator.RightShift,
        ">>>=" => CsOperator.UnsignedRightShift,
        _ => CsOperator.None
    };

    static CsOperator BinaryOperator(string token) => token switch
    {
        "+" => CsOperator.Add,
        "-" => CsOperator.Subtract,
        "*" => CsOperator.Multiply,
        "/" => CsOperator.Divide,
        "%" => CsOperator.Modulo,
        "==" => CsOperator.Equal,
        "!=" => CsOperator.NotEqual,
        "<" => CsOperator.LessThan,
        "<=" => CsOperator.LessThanOrEqual,
        ">" => CsOperator.GreaterThan,
        ">=" => CsOperator.GreaterThanOrEqual,
        "&&" => CsOperator.AndAlso,
        "||" => CsOperator.OrElse,
        "??" => CsOperator.Coalesce,
        "&" => CsOperator.BitwiseAnd,
        "|" => CsOperator.BitwiseOr,
        "^" => CsOperator.ExclusiveOr,
        "<<" => CsOperator.LeftShift,
        ">>" => CsOperator.RightShift,
        ">>>" => CsOperator.UnsignedRightShift,
        _ => CsOperator.None
    };
}
