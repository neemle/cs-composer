using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsType TypeName(int depth = 0, bool stopBeforeArray = false, bool stopAtQuestion = false)
    {
        Guard(depth);
        int start = p;
        CsType type;
        if (Take("ref"))
        { bool readOnly = Take("readonly"); return Bind(TypeName(depth + 1, stopBeforeArray, stopAtQuestion).Ref(readOnly), start); }
        if (Take("delegate"))
        {
            Expect("*");
            var convention = Take("managed") ? FunctionPointerConvention.Managed : Take("unmanaged") ? FunctionPointerConvention.Unmanaged : FunctionPointerConvention.Default;
            var conventions = new List<string>();
            if (Take("[")) { do { conventions.Add(Identifier()); } while (Take(",")); Expect("]"); }
            Expect("<"); var parameters = new List<CsFunctionPointerParameter>();
            do { string? modifier = ParameterModifier(); parameters.Add(new(TypeName(depth + 1), modifier)); } while (Take(","));
            Expect(">");
            type = new("") { Form = TypeForm.FunctionPointer, CallingConvention = convention, UnmanagedConventions = new(conventions), FunctionParameters = new(parameters) };
        }
        else if (Take("("))
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
            type = TypeArguments(type, depth + 1);
            while (Take("."))
            {
                string nested = Identifier();
                var part = TypeArguments(new CsType(nested), depth + 1);
                type = part.Form is TypeForm.Generic or TypeForm.UnboundGeneric
                    ? part with { Element = new CsType(nested) { Form = TypeForm.Nested, Element = type } }
                    : new CsType(nested) { Form = TypeForm.Nested, Element = type };
            }
        }
        while (Current.Text is "?" or "[" or "*")
        {
            if (stopBeforeArray && Current.Text == "[" || stopAtQuestion && Current.Text == "?") break;
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
        return type.Form == TypeForm.Named ? type : Bind(type with { Origin = null }, start);
    }

    CsType TypeArguments(CsType type, int depth)
    {
        if (!Take("<")) return type;
        if (Current.Text is "," or ">")
        {
            int arity = 1; while (Take(",")) arity++; Expect(">"); return type.Unbound(arity);
        }
        var arguments = new List<CsType>();
        do { arguments.Add(TypeName(depth + 1)); } while (Take(","));
        Expect(">"); return type.Generic(arguments.ToArray());
    }

    Values<CsAttribute> Attributes(bool fileOnly = false)
    {
        var result = new List<CsAttribute>();
        while (Current.Text == "[" && (!fileOnly || Peek(1) is "assembly" or "module" && Peek(2) == ":"))
        {
            p++;
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

    Values<CsParameter> Parameters(string open = "(", string close = ")")
    {
        Expect(open);
        var result = new List<CsParameter>();
        if (Current.Text != close) do
            {
                int start = p;
                if (Take("__arglist")) { result.Add(Bind(new CsParameter(null, "") { IsArgList = true }, start)); continue; }
                var attributes = Attributes();
                string? modifier = ParameterModifier();
                var type = TypeName();
                var name = Current;
                Identifier();
                var initializer = Take("=") ? Expression() : null;
                result.Add(Bind(new CsParameter(type, name.Text)
                {
                    Attributes = attributes,
                    Modifier = modifier,
                    Default = initializer
                }, start, "Name", name.Start, name.Text.Length));
            } while (Take(","));
        Expect(close);
        return new(result);
    }

    string? ParameterModifier()
    {
        if (Current.Text == "scoped" && (Peek(1) is "[" or "?" or "*" or "." or "<" or "::" || Peek(2) is "," or ")" or "=" or "]")) return null;
        if (Take("scoped"))
        {
            if (Current.Text is not ("ref" or "in")) return "scoped";
            return "scoped " + ParameterModifier();
        }
        if (Take("ref")) return Take("readonly") ? "ref readonly" : "ref";
        if (Take("this")) { var modifier = ParameterModifier(); return modifier == null ? "this" : "this " + modifier; }
        return Current.Text is "out" or "in" or "params" ? tokens[p++].Text : null;
    }

    Values<TypeParameter> Generics()
    {
        if (!Take("<")) return Values<TypeParameter>.Empty;
        var result = new List<TypeParameter>();
        do
        {
            int start = p;
            var attributes = Attributes();
            string? variance = Current.Text is "in" or "out" ? tokens[p++].Text : null;
            var name = Current;
            Identifier();
            result.Add(Bind(new TypeParameter(name.Text) { Variance = variance, Attributes = attributes }, start, "Name", name.Start, name.Text.Length));
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
                else if (Take("allows")) { Expect("ref"); Expect("struct"); constraints.Add(new("allows ref struct")); }
                else constraints.Add(new("type", TypeName()));
            } while (Take(","));
            int index = Array.FindIndex(result, t => t.Name == name);
            if (index < 0) throw new ParseFailure("Constraint has no matching type parameter.");
            result[index] = result[index] with { Constraints = new(constraints) };
        }
        return new(result);
    }

    Values<string> Modifiers(string? owner = null)
    {
        var result = new List<string>();
        while (new[] { "public", "private", "internal", "protected", "static", "partial", "sealed", "abstract",
            "virtual", "override", "async", "readonly", "required", "new", "unsafe", "extern", "volatile", "const",
            "file", "ref", "closed", "safe", "fixed" }.Contains(Current.Text))
        {
            if (Current.Text == "ref" && Peek(1) is not ("struct" or "partial")) break;
            if (Current.Text is "async" or "partial" or "required" or "file" or "closed" or "safe")
            {
                if (Peek(1) is "." or "::" or "<" or "[" or "?" or "*") break;
                if (!(Current.Text == "partial" && Rendering.Names.Key(Peek(1)) == Rendering.Names.Key(owner) && Peek(2) == "(") && Peek(1) is not ("class" or "struct" or "interface" or "record" or "enum" or "union") &&
                    Peek(2) is ";" or "=" or "," or "(" or "{" or "=>" or "[") break;
            }
            result.Add(Current.Text); p++;
        }
        return new(result);
    }
}
