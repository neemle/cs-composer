using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Neemle.CSharpComposer.Rendering;

internal static class Names
{
    internal static readonly string[] ModifierOrder =
    {
        "public", "protected", "internal", "private", "static", "new", "abstract", "virtual", "sealed", "override",
        "readonly", "ref", "required", "const", "volatile", "unsafe", "extern", "async", "partial"
    };

    // Reserved keywords are always written escaped. Contextual keywords are identifiers wherever the surrounding
    // syntax does not read them as keywords, so they are escaped only there (GENE-29 S0.5): every one in a type name
    // (the start of a declaration), the few an expression or a pattern designation reads as syntax (ExpressionWords),
    // `field` in a get, set or init accessor and the query keywords in a query expression. A name written with `@` in
    // the model keeps it: the parser records `@x` as written, and Key reads `@x` and `x` as one name.
    const string ReservedText =
        "abstract as base bool break byte case catch char checked class const continue decimal default delegate do " +
        "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int " +
        "interface internal is lock long namespace new null object operator out override params private protected " +
        "public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw " +
        "true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while";

    const string ContextualText =
        "add alias and ascending async await by descending dynamic equals file from get global group init into join let managed " +
        "nameof nint not notnull nuint on or orderby partial record remove required scoped select set unmanaged " +
        "value var when where with yield field union closed";

    static readonly HashSet<string> Reserved = new(ReservedText.Split(' '), StringComparer.Ordinal);

    static readonly HashSet<string> TypeWords = new((ReservedText + " " + ContextualText).Split(' '), StringComparer.Ordinal);

    // `await` (an async body, and every fragment parse), `nameof(`, `var(` and the var pattern, the pattern
    // combinators and a case guard's `when`.
    static readonly HashSet<string> ExpressionWords = new(["await", "nameof", "var", "and", "or", "not", "when"], StringComparer.Ordinal);

    static readonly HashSet<string> QueryWords = new(
        ["from", "where", "select", "group", "into", "orderby", "join", "let", "on", "equals", "by", "ascending", "descending"], StringComparer.Ordinal);

    static readonly HashSet<string> FieldAccessors = new(["get", "set", "init"], StringComparer.Ordinal);

    [ThreadStatic] static bool inAccessor;

    [ThreadStatic] static bool inQuery;

    // Marks the text written until the returned context is disposed as inside a property accessor or a query
    // expression (or both); a region inside one stays inside it.
    internal static Context Enter(bool accessor = false, bool query = false)
    {
        var previous = new Context(inAccessor, inQuery);
        inAccessor |= accessor;
        inQuery |= query;
        return previous;
    }

    // A property's get, set or init accessor, where `field` is the backing field (an event's add and remove have none).
    internal static bool BacksField(CsNode? node) => node is CsAccessor accessor && FieldAccessors.Contains(accessor.Kind);

    // The context to restore: the one before Enter.
    internal readonly struct Context(bool accessor, bool query) : IDisposable
    {
        public void Dispose()
        {
            inAccessor = accessor;
            inQuery = query;
        }
    }

    internal static IEnumerable<string> OrderModifiers(IEnumerable<string> modifiers) =>
        modifiers.OrderBy(ModifierIndex).ThenBy(value => value, StringComparer.Ordinal);

    static int ModifierIndex(string modifier)
    {
        int index = System.Array.IndexOf(ModifierOrder, modifier);
        return index < 0 ? int.MaxValue : index;
    }

    internal static bool IsIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var decoded = IdentifierText.Decode(name);
        if (decoded == null) return false;
        if (decoded.StartsWith('@')) decoded = decoded[1..];
        var runes = decoded.EnumerateRunes().ToArray();
        return runes.Length > 0 && IdentifierText.Start(runes[0]) && runes.Skip(1).All(IdentifierText.Part);
    }

    internal static bool IsQualified(string? name) =>
        !string.IsNullOrEmpty(name) && QualifiedParts(name!).All(IsIdentifier);

    static IEnumerable<string> QualifiedParts(string name) => name.Replace("::", ".").Split('.');

    internal static string Key(string? name) => name == null ? "" : (IdentifierText.Decode(name) ?? name).TrimStart('@');

    // A declared or member name: a parameter, a variable, a member, a label or a designation.
    internal static string Id(string name) => Escape(name, Declared);

    // A simple name in an expression, and a pattern designation.
    internal static string Simple(string name) => Escape(name, identifier => Declared(identifier) || ExpressionWords.Contains(identifier));

    // A member name after `.`, `?.`, `->` or `::`, where only a reserved keyword reads as syntax.
    internal static string Member(string name) => Escape(name, Reserved.Contains);

    // A name in a type: a type, type parameter or namespace segment written where a type is read.
    static string TypeId(string name) => Escape(name, TypeWords.Contains);

    static bool Declared(string identifier) => Reserved.Contains(identifier) || inAccessor && identifier == "field" || inQuery && QueryWords.Contains(identifier);

    static string Escape(string name, Func<string, bool> keyword)
    {
        name = IdentifierText.Decode(name) ?? throw new ArgumentException("Invalid identifier escape.", nameof(name));
        if (name.StartsWith('@')) return name;
        return keyword(name) ? "@" + name : name;
    }

    internal static string Qualified(string name) => Qualified(name, Id);

    static string Qualified(string name, Func<string, string> part)
    {
        int alias = name.IndexOf("::", StringComparison.Ordinal);
        if (alias >= 0) return (name[..alias] == "global" ? "global" : part(name[..alias])) + "::" + Qualified(name[(alias + 2)..], part);
        return string.Join(".", name.Split('.').Select(part));
    }

    internal static string Type(CsType type) => InlineComments.Apply(type with { Comments = new(type.Comments.Where(c => c.Placement != CommentPlacement.Container)) }, TypeCore(type));

    static string Inside(CsNode node) => string.Concat(node.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text));

    static string TypeCore(CsType type) => type.Form switch
    {
        TypeForm.Ref => "ref " + Inside(type) + (type.ReadOnly ? "readonly " : "") + Type(type.Element!),
        TypeForm.UnboundGeneric => Type(type.Element!) + "<" + Inside(type) + new string(',', type.Rank - 1) + ">",
        TypeForm.FunctionPointer => "delegate*" + Inside(type) + (type.CallingConvention == FunctionPointerConvention.Default ? "" : " " + type.CallingConvention.ToString().ToLowerInvariant()) +
            (type.UnmanagedConventions.Count == 0 ? "" : "[" + string.Join(", ", type.UnmanagedConventions.Select(Id)) + "]") + "<" +
            string.Join(", ", type.FunctionParameters.Select(FunctionPointerParameter)) + ">",
        TypeForm.Raw => Inside(type) + NormalizeLines(type.Name),
        TypeForm.Named => QualifiedType(type),
        TypeForm.Parameter => Inside(type) + TypeId(type.Name),
        TypeForm.Generic => Type(type.Element!) + "<" + Inside(type) + string.Join(", ", type.Arguments.Select(Type)) + ">",
        TypeForm.Nested => Type(type.Element!) + "." + Inside(type) + TypeId(type.Name) + OptionalArguments(type.Arguments),
        TypeForm.Nullable => Type(type.Element!) + Inside(type) + "?",
        TypeForm.Array => Type(type.Element!) + "[" + Inside(type) + new string(',', type.Rank - 1) + "]",
        TypeForm.Pointer => Type(type.Element!) + Inside(type) + "*",
        TypeForm.Tuple => "(" + Inside(type) + string.Join(", ", type.Elements.Select(TupleElement)) + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    static string QualifiedType(CsType type)
    {
        string text = NamedType(type.Name), comments = Inside(type);
        if (comments.Length == 0) return text;
        int separator = text.LastIndexOf('.');
        if (separator >= 0) return text.Insert(separator + 1, comments);
        int alias = text.IndexOf("::", StringComparison.Ordinal);
        return alias >= 0 ? text.Insert(alias + 2, comments) : comments + text;
    }

    // `var` names a type called var where it is not inferred (a declaration of several variables, or one without a
    // value); written escaped it would read back as a different name.
    static string NamedType(string name) => Cs.PrimitiveNames.Values.Contains(name) || name is "nint" or "nuint" or "dynamic" or "var" ? name : Qualified(name, TypeId);

    static string Arguments(Values<CsType> arguments) => "<" + string.Join(", ", arguments.Select(Type)) + ">";

    static string OptionalArguments(Values<CsType> arguments) => arguments.Count == 0 ? "" : Arguments(arguments);

    internal static string FunctionPointerParameter(CsFunctionPointerParameter parameter) => InlineComments.Apply(parameter,
        (parameter.Modifier == null ? "" : parameter.Modifier + " ") + Type(parameter.Type));

    static string TupleElement(TupleElement element) => InlineComments.Apply(element,
        Type(element.Type) + (element.Name == null ? "" : " " + Id(element.Name)));

    internal static string NormalizeLines(string text)
    {
        var output = new System.Text.StringBuilder();
        int position = 0;
        foreach (var token in Parsing.Lexer.Scan(text))
        {
            output.Append(text[position..token.Start].Replace("\r\n", "\n").Replace('\r', '\n'));
            output.Append(token.Text.StartsWith('#') ? token.Text.Replace("\r\n", "\n").Replace('\r', '\n') : token.Text);
            position = token.End;
        }
        return output.ToString();
    }

    internal static string Expression(CsExpression expression) => ExpressionWriter.Write(expression);

    internal static string Attribute(CsAttribute attribute) => AttributeText(attribute, true);

    // A parsed attribute's own source span is its type and arguments; the target and the brackets belong to the
    // enclosing attribute list. So an attribute rendered on its own (Reformat of that one node) writes neither.
    internal static string AttributeText(CsAttribute attribute, bool list) => InlineComments.Apply(attribute with { Comments = new(attribute.Comments.Where(c => c.Placement != CommentPlacement.Container)) },
        (list ? "[" + (attribute.Target == null ? "" : attribute.Target + ": ") : "") + Type(attribute.Type) +
        AttributeArguments(attribute.Arguments, Inside(attribute)) + (list ? "]" : ""));

    static string AttributeArguments(Values<AttributeArgument> arguments, string comments) => arguments.Count == 0 && comments.Length == 0
        ? "" : "(" + comments + string.Join(", ", arguments.Select(AttributeArgument)) + ")";

    static string AttributeArgument(AttributeArgument argument) => InlineComments.Apply(argument,
        (argument.Name == null ? "" : Id(argument.Name) + (argument.IsConstructorName ? ": " : " = ")) +
        Expression(argument.Value));

    internal static string Parameter(CsParameter parameter) => InlineComments.Apply(parameter with { Comments = new(parameter.Comments.Where(c => c.Placement != CommentPlacement.Container)) }, ParameterCore(parameter));

    static string ParameterCore(CsParameter parameter) => parameter.IsArgList ? "__arglist" :
        string.Concat(parameter.Attributes.Select(attribute => Attribute(attribute) + " ")) +
        (parameter.Modifier == null ? "" : parameter.Modifier + " ") + Type(parameter.Type!) + " " + Inside(parameter) +
        Id(parameter.Name) + (parameter.Default == null ? "" : " = " + Expression(parameter.Default));

    internal static string Generics(Values<TypeParameter> generics) => generics.Count == 0
        ? "" : "<" + string.Join(", ", generics.Select(TypeParameter)) + ">";

    static string TypeParameter(TypeParameter parameter) => InlineComments.Apply(parameter,
        string.Concat(parameter.Attributes.Select(a => Attribute(a) + " ")) + (parameter.Variance == null ? "" : parameter.Variance + " ") + Id(parameter.Name));

    internal static IEnumerable<CsConstraint> Constraints(TypeParameter parameter) =>
        parameter.Constraints.OrderBy(constraint => constraint.Kind == "allows ref struct" ? 3 : constraint.Kind == "new()" ? 2 : constraint.Kind == "type" ? 1 : 0);
}
