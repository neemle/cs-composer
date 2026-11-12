using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsExpression Operation(ExpressionKind kind, int start, int depth)
    {
        Guard(depth);
        if (kind is ExpressionKind.This or ExpressionKind.Base) return Bind(new CsExpression(kind, "", Values<CsExpression>.Empty), start);
        if (kind == ExpressionKind.StackAlloc)
        {
            var type = Current.Text == "[" ? null : TypeName(depth + 1, stopBeforeArray: true);
            Expect("[");
            var sizes = ExpressionList("]", depth + 1); Expect("]");
            return Bind(new CsExpression(kind, "", new(sizes)) { Type = type, Initializer = Current.Text == "{" ? Initializer(depth + 1) : null }, start);
        }
        if (kind == ExpressionKind.SizeOf)
        { Expect("("); var type = TypeName(depth + 1); Expect(")"); return Bind(new CsExpression(kind, "", Values<CsExpression>.Empty) { Type = type }, start); }
        if (kind is ExpressionKind.Checked or ExpressionKind.Unchecked or ExpressionKind.NameOf or ExpressionKind.Unsafe)
        {
            Expect("("); CsExpression? value = null;
            if (kind == ExpressionKind.NameOf)
            {
                int typeStart = p;
                try
                {
                    var candidate = TypeName(depth + 1);
                    if (Current.Text != ")" || !ContainsUnbound(candidate)) throw new ParseFailure("Name reference expected.");
                    value = new(ExpressionKind.TypeReference, "", Values<CsExpression>.Empty) { Type = candidate };
                }
                catch (ParseFailure) { p = typeStart; }
            }
            value ??= Expression(0, depth + 1); Expect(")"); return Bind(new CsExpression(kind, "", new[] { value }), start);
        }
        return Bind(new CsExpression(kind, "", new[] { Expression(kind == ExpressionKind.Ref ? 10 : 80, depth + 1) }), start);
    }

    static bool ContainsUnbound(CsType type) => type.Form == TypeForm.UnboundGeneric || type.Element != null && ContainsUnbound(type.Element) || type.Arguments.Any(ContainsUnbound);

    CsExpression TupleElementExpression(int depth)
    {
        Guard(depth); int start = p;
        if (QueryAhead()) return Expression(0, depth + 1);
        if (Current.Text == "var" && Peek(1) == "(")
        {
            p += 2; var names = new List<CsExpression>();
            do { names.Add(Current.Text == "(" ? PrefixExpression(depth + 1) : Bind(Cs.Id(Identifier()), p - 1)); } while (Take(","));
            Expect(")"); return Bind(new CsExpression(ExpressionKind.Declaration, "", new(names)), start);
        }
        try
        {
            bool isVar = Take("var"); var type = isVar ? null : TypeName(depth + 1);
            if (type?.Form == TypeForm.Pointer)
            {
                var element = type;
                while (element.Form == TypeForm.Pointer) element = element.Element!;
                if (element.Form == TypeForm.Named && !Cs.PrimitiveNames.Values.Contains(element.Name)) throw new ParseFailure("Multiplication expression.");
            }
            string name = Identifier();
            if (Current.Text is not ("," or ")" or "in")) throw new ParseFailure("Tuple declaration expected.");
            return Bind(new CsExpression(ExpressionKind.Declaration, name, Values<CsExpression>.Empty) { Type = type }, start);
        }
        catch (ParseFailure) { p = start; return ArgumentExpression(depth + 1); }
    }

    CsExpression ArgumentExpression(int depth)
    {
        Guard(depth);
        int start = p;
        string? name = null;
        if (Peek(1) == ":") { name = Identifier(); Expect(":"); }
        var modifier = Current.Text switch { "ref" => ArgumentModifier.Ref, "out" => ArgumentModifier.Out, "in" => ArgumentModifier.In, _ => ArgumentModifier.None };
        if (modifier != ArgumentModifier.None) p++;
        CsExpression value;
        if (modifier == ArgumentModifier.Out)
        {
            int before = p;
            try
            {
                bool isVar = Take("var"); var type = isVar ? null : TypeName(depth + 1);
                var variable = Current; Identifier();
                value = Bind(new CsExpression(ExpressionKind.Declaration, variable.Text, Values<CsExpression>.Empty) { Type = type }, before);
            }
            catch (ParseFailure) { p = before; value = Expression(0, depth + 1); }
        }
        else value = Expression(0, depth + 1);
        return modifier == ArgumentModifier.None && name == null ? value : Bind(new CsExpression(ExpressionKind.Argument, name ?? "", new[] { value }) { ArgumentModifier = modifier }, start);
    }

    List<CsExpression> ArgumentList(int depth, string close = ")")
    {
        var result = new List<CsExpression>();
        if (Current.Text != close) do { result.Add(ArgumentExpression(depth + 1)); } while (Take(","));
        return result;
    }

    CsExpression Collection(int start, int depth)
    {
        Guard(depth);
        var parts = new List<CsExpression>();
        while (Current.Text is not ("]" or ""))
        {
            int partStart = p;
            if (Take("with"))
            { Expect("("); var args = ArgumentList(depth + 1); Expect(")"); parts.Add(Bind(new CsExpression(ExpressionKind.CollectionArguments, "", new(args)), partStart)); }
            else parts.Add(Take("..") ? Bind(new CsExpression(ExpressionKind.Spread, "", new[] { Expression(0, depth + 1) }), partStart) : Expression(0, depth + 1));
            if (!Take(",")) break;
        }
        Expect("]");
        return Bind(new CsExpression(ExpressionKind.Collection, "", new(parts)), start);
    }

    CsExpression Range(CsExpression? left, int start, int depth)
    {
        Guard(depth);
        var right = Current.Text is "]" or ")" or "," or ";" or "}" or "" ? null : Expression(77, depth + 1);
        return Bind(new CsExpression(ExpressionKind.Range, "", new[] { left ?? Omitted(), right ?? Omitted() }), start);
    }

    static CsExpression Omitted() => new(ExpressionKind.Omitted, "", Values<CsExpression>.Empty);

    CsExpression GenericReference(CsExpression value, int start, int depth)
    {
        int before = p;
        try
        {
            Expect("<");
            var types = new List<CsType>();
            do { types.Add(TypeName(depth + 1)); } while (Take(","));
            Expect(">");
            // C# grammar disambiguates type argument lists from relational operators
            // using the token following '>', independently of symbol binding.
            if (Current.Text is not ("(" or ")" or "]" or "}" or ":" or ";" or "," or "." or "?." or "?" or "==" or "!=" or "|" or "^" or "&" or "&&" or "||" or "=" or "is" or "as" or ""))
                throw new ParseFailure("Relational expression.");
            return Bind(value with { Origin = null, TypeArguments = new(types) }, start);
        }
        catch (ParseFailure) { p = before; return value; }
    }

    string OperatorToken(out int width)
    {
        width = 1;
        if (Current.Text != ">") return Current.Text;
        if (Peek(1) != ">" && Peek(1) != ">=") return Current.Text;
        if (Current.End != tokens[p + 1].Start) return Current.Text;
        width = 2;
        if (Peek(1) == ">=") return ">>=";
        if (Peek(2) is ">" or ">=" && tokens[p + 1].End == tokens[p + 2].Start) { width = 3; return Peek(2) == ">=" ? ">>>=" : ">>>"; }
        return ">>";
    }
}
