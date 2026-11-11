using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
internal sealed partial class DocumentParser
{
    CsType TypeName(int depth = 0)
    {
        Guard(depth);
        int start = p;
        CsType type;
        if (Take("("))
        {
            var elements = new List<TupleElement>();
            do
            {
                var element = TypeName(depth + 1);
                string? name = Current.Text is not ("," or ")") ? Identifier() : null;
                elements.Add(new(element, name));
            } while (Take(","));
            Expect(")");
            type = Cs.Tuple(elements.ToArray());
        }
        else
        {
            int nameStart = Current.Start;
            string name = QualifiedName();
            type = Bind(new CsType(name), start, "Name", nameStart, Last.End - nameStart);
            if (Take("<"))
            {
                var arguments = new List<CsType>();
                do { arguments.Add(TypeName(depth + 1)); } while (Take(","));
                Expect(">");
                type = type.Generic(arguments.ToArray());
            }
        }
        while (Current.Text is "?" or "[" or "*")
        {
            if (Take("?")) type = type.Nullable();
            else if (Take("*")) type = type.Pointer();
            else
            {
                Expect("[");
                int rank = 1;
                while (Take(",")) rank++;
                Expect("]");
                type = type.Array(rank);
            }
        }
        return type.Origin == null ? Bind(type, start) : type;
    }
    Values<CsAttribute> Attributes()
    {
        var result = new List<CsAttribute>();
        while (Take("["))
        {
            string? target = null;
            if (Peek(1) == ":") { target = Identifier(); p++; }
            do
            {
                int start = p;
                var type = TypeName();
                var arguments = new List<AttributeArgument>();
                if (Take("("))
                {
                    if (Current.Text != ")") do
                    {
                        string? name = null;
                        bool constructor = false;
                        if (Peek(1) is "=" or ":") { name = Identifier(); constructor = Take(":"); if (!constructor) p++; }
                        arguments.Add(new(Expression(), name, constructor));
                    } while (Take(","));
                    Expect(")");
                }
                result.Add(Bind(new CsAttribute(type) { Target = target, Arguments = new(arguments) }, start));
            } while (Take(","));
            Expect("]");
        }
        return new(result);
    }
    Values<CsParameter> Parameters()
    {
        Expect("(");
        var result = new List<CsParameter>();
        if (Current.Text != ")") do
        {
            int start = p;
            var attributes = Attributes();
            string? modifier = Current.Text is "ref" or "out" or "in" or "params" or "this" ? tokens[p++].Text : null;
            var type = TypeName();
            var name = Current;
            Identifier();
            var initializer = Take("=") ? Expression() : null;
            result.Add(Bind(new CsParameter(type, name.Text) { Attributes = attributes, Modifier = modifier,
                Default = initializer }, start, "Name", name.Start, name.Text.Length));
        } while (Take(","));
        Expect(")");
        return new(result);
    }
    Values<TypeParameter> Generics()
    {
        if (!Take("<")) return Values<TypeParameter>.Empty;
        var result = new List<TypeParameter>();
        do
        {
            int start = p;
            string? variance = Current.Text is "in" or "out" ? tokens[p++].Text : null;
            var name = Current;
            Identifier();
            result.Add(Bind(new TypeParameter(name.Text) { Variance = variance }, start, "Name", name.Start, name.Text.Length));
        } while (Take(","));
        Expect(">");
        return new(result);
    }
    Values<TypeParameter> Constraints(Values<TypeParameter> parameters)
    {
        var result = parameters.ToArray();
        while (Take("where"))
        {
            string name = Identifier();
            Expect(":");
            var constraints = new List<CsConstraint>();
            do
            {
                if (Take("new")) { Expect("("); Expect(")"); constraints.Add(new("new()")); }
                else if (Current.Text is "class" or "struct" or "notnull" or "unmanaged" or "default")
                { string kind = tokens[p++].Text; if (Take("?")) kind += "?"; constraints.Add(new(kind)); }
                else constraints.Add(new("type", TypeName()));
            } while (Take(","));
            int index = Array.FindIndex(result, t => t.Name == name);
            if (index < 0) throw new ParseFailure("Constraint has no matching type parameter.");
            result[index] = result[index] with { Constraints = new(constraints) };
        }
        return new(result);
    }
    Values<string> Modifiers()
    {
        var result = new List<string>();
        while (new[] { "public", "private", "internal", "protected", "static", "partial", "sealed", "abstract",
            "virtual", "override", "async", "readonly", "required", "new", "unsafe", "extern", "volatile", "const",
            "file", "ref" }.Contains(Current.Text)) { result.Add(Current.Text); p++; }
        return new(result);
    }
}
