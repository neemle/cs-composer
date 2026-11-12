using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsType? NullableIsType(int depth)
    {
        int start = p;
        try
        {
            var type = TypeName(depth + 1);
            // The same question token can instead start a conditional expression.
            // A nullable type test is selected only when no conditional operand follows.
            if (type.Form == TypeForm.Nullable && Current.Text is ";" or ")" or "]" or "}" or "," or ":" or "&&" or "||" or "==" or "!=" or "&" or "|" or "^" or "") return type;
        }
        catch (ParseFailure) { }
        p = start; return null;
    }

    CsSymbol VariableDesignation(int depth)
    {
        Guard(depth); int start = p;
        if (Take("("))
        {
            var names = new List<CsNode>();
            do { names.Add(VariableDesignation(depth + 1)); } while (Take(","));
            Expect(")"); return Bind(Cs.Symbol(new() { Operation = SymbolOperation.Tuple, Parts = new(names) }), start);
        }
        var name = Current; Identifier();
        return Bind(Cs.Symbol(name.Text), start, "Name", name.Start, name.Text.Length);
    }

    CsPattern RecursivePattern(CsType? type, int start, int depth)
    {
        Guard(depth);
        bool positional = Take("(");
        var positions = new List<CsPattern>();
        if (positional)
        {
            if (Current.Text != ")") do { positions.Add(Subpattern(depth + 1)); } while (Take(","));
            Expect(")");
        }
        bool properties = Take("{");
        var members = new List<CsPattern>();
        if (properties)
        {
            while (Current.Text is not ("}" or ""))
            {
                members.Add(Subpattern(depth + 1, requireName: true));
                if (!Take(",")) break;
            }
            Expect("}");
        }
        return Bind(new CsPattern(PatternKind.Recursive)
        {
            Type = type,
            HasPositional = positional,
            HasProperties = properties,
            Patterns = new(positions),
            Properties = new(members),
            Name = PatternDesignation()
        }, start);
    }

    CsPattern Subpattern(int depth, bool requireName = false)
    {
        Guard(depth);
        int start = p;
        string? name = null;
        try
        {
            name = QualifiedName(); Expect(":");
        }
        catch (ParseFailure) { p = start; name = null; }
        if (requireName && name == null) throw new ParseFailure("Property pattern requires a member name.");
        var pattern = Pattern(0, depth + 1);
        return name == null ? pattern : Bind(new CsPattern(PatternKind.Property) { Name = name, Patterns = new[] { pattern } }, start);
    }

    string? PatternDesignation() => Current.Text is not ("when" or "and" or "or" or "is" or "as" or "switch" or "with") &&
        Rendering.Names.IsIdentifier(Current.Text) ? Identifier() : null;

    CsPattern ListPattern(int start, int depth)
    {
        Guard(depth);
        var items = new List<CsPattern>();
        while (Current.Text is not ("]" or ""))
        {
            int itemStart = p;
            if (Take(".."))
                items.Add(Bind(new CsPattern(PatternKind.Slice) { Patterns = Current.Text is "," or "]" ? Values<CsPattern>.Empty : new[] { Pattern(0, depth + 1) } }, itemStart));
            else items.Add(Pattern(0, depth + 1));
            if (!Take(",")) break;
        }
        Expect("]");
        return Bind(new CsPattern(PatternKind.List) { Patterns = new(items), Name = PatternDesignation() }, start);
    }
}
