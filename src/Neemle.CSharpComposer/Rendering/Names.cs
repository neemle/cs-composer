using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Neemle.CSharpComposer.Rendering;

internal static class Names
{
    internal static readonly string[] ModifierOrder =
    {
        "new", "public", "protected", "internal", "private", "static", "abstract", "virtual", "sealed", "override",
        "readonly", "ref", "required", "const", "volatile", "unsafe", "extern", "async", "partial"
    };

    const string KeywordText =
        "abstract as base bool break byte case catch char checked class const continue decimal default delegate do " +
        "double else enum event explicit extern false finally fixed float for foreach goto if implicit in int " +
        "interface internal is lock long namespace new null object operator out override params private protected " +
        "public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw " +
        "true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias and " +
        "ascending async await by descending dynamic equals file from get global group init into join let managed " +
        "nameof nint not notnull nuint on or orderby partial record remove required scoped select set unmanaged " +
        "value var when where with yield";

    static readonly HashSet<string> Keywords =
        new HashSet<string>(KeywordText.Split(' '), StringComparer.Ordinal);

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
        int start = name![0] == '@' ? 1 : 0;
        if (start == name.Length || !Start(name[start])) return false;
        for (int i = start + 1; i < name.Length; i++)
            if (!IdentifierPart(name[i])) return false;
        return true;
    }

    static bool IdentifierPart(char character)
    {
        var category = char.GetUnicodeCategory(character);
        return Start(character) || category == UnicodeCategory.DecimalDigitNumber ||
            category == UnicodeCategory.ConnectorPunctuation || category == UnicodeCategory.NonSpacingMark ||
            category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.Format;
    }

    static bool Start(char character) => character == '_' || char.IsLetter(character) ||
        char.GetUnicodeCategory(character) == UnicodeCategory.LetterNumber;

    internal static bool IsQualified(string? name) =>
        !string.IsNullOrEmpty(name) && WithoutGlobal(name!).Split('.').All(IsIdentifier);

    static string WithoutGlobal(string name) =>
        name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;

    internal static string Id(string name)
    {
        string identifier = name.StartsWith("@", StringComparison.Ordinal) ? name.Substring(1) : name;
        return Keywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    internal static string Qualified(string name) =>
        name.StartsWith("global::", StringComparison.Ordinal)
            ? "global::" + Qualified(name.Substring(8))
            : string.Join(".", name.Split('.').Select(Id));

    internal static string Type(CsType type) => type.Form switch
    {
        TypeForm.Raw => NormalizeLines(type.Name),
        TypeForm.Named => NamedType(type.Name),
        TypeForm.Parameter => Id(type.Name),
        TypeForm.Generic => Type(type.Element!) + Arguments(type.Arguments),
        TypeForm.Nested => Type(type.Element!) + "." + Id(type.Name) + OptionalArguments(type.Arguments),
        TypeForm.Nullable => Type(type.Element!) + "?",
        TypeForm.Array => Type(type.Element!) + "[" + new string(',', type.Rank - 1) + "]",
        TypeForm.Pointer => Type(type.Element!) + "*",
        TypeForm.Tuple => "(" + string.Join(", ", type.Elements.Select(TupleElement)) + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    static string NamedType(string name) => Cs.PrimitiveNames.Values.Contains(name) ? name : Qualified(name);

    static string Arguments(Values<CsType> arguments) => "<" + string.Join(", ", arguments.Select(Type)) + ">";

    static string OptionalArguments(Values<CsType> arguments) => arguments.Count == 0 ? "" : Arguments(arguments);

    static string TupleElement(TupleElement element) =>
        Type(element.Type) + (element.Name == null ? "" : " " + Id(element.Name));

    internal static string NormalizeLines(string text)
    {
        var output = new System.Text.StringBuilder();
        int position = 0;
        foreach (var token in Parsing.Lexer.Scan(text))
        {
            output.Append(text[position..token.Start].Replace("\r\n", "\n").Replace('\r', '\n'));
            output.Append(token.Text);
            position = token.End;
        }
        return output.ToString();
    }

    internal static string Expression(CsExpression expression) => ExpressionWriter.Write(expression);

    internal static string Attribute(CsAttribute attribute) =>
        "[" + (attribute.Target == null ? "" : attribute.Target + ": ") + Type(attribute.Type) +
        AttributeArguments(attribute.Arguments) + "]";

    static string AttributeArguments(Values<AttributeArgument> arguments) => arguments.Count == 0
        ? "" : "(" + string.Join(", ", arguments.Select(AttributeArgument)) + ")";

    static string AttributeArgument(AttributeArgument argument) =>
        (argument.Name == null ? "" : Id(argument.Name) + (argument.IsConstructorName ? ": " : " = ")) +
        Expression(argument.Value);

    internal static string Parameter(CsParameter parameter) =>
        string.Concat(parameter.Attributes.Select(attribute => Attribute(attribute) + " ")) +
        (parameter.Modifier == null ? "" : parameter.Modifier + " ") + Type(parameter.Type) + " " +
        Id(parameter.Name) + (parameter.Default == null ? "" : " = " + Expression(parameter.Default));

    internal static string Generics(Values<TypeParameter> generics) => generics.Count == 0
        ? "" : "<" + string.Join(", ", generics.Select(TypeParameter)) + ">";

    static string TypeParameter(TypeParameter parameter) =>
        (parameter.Variance == null ? "" : parameter.Variance + " ") + Id(parameter.Name);

    internal static IEnumerable<CsConstraint> Constraints(TypeParameter parameter) =>
        parameter.Constraints.OrderBy(constraint => constraint.Kind == "new()" ? 2 : constraint.Kind == "type" ? 1 : 0);
}
