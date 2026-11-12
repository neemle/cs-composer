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
        "value var when where with yield field union closed";

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

    internal static string Id(string name)
    {
        name = IdentifierText.Decode(name) ?? throw new ArgumentException("Invalid identifier escape.", nameof(name));
        string identifier = name.StartsWith("@", StringComparison.Ordinal) ? name.Substring(1) : name;
        return Keywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    internal static string Qualified(string name)
    {
        int alias = name.IndexOf("::", StringComparison.Ordinal);
        if (alias >= 0) return (name[..alias] == "global" ? "global" : Id(name[..alias])) + "::" + Qualified(name[(alias + 2)..]);
        return string.Join(".", name.Split('.').Select(Id));
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
        TypeForm.Parameter => Inside(type) + Id(type.Name),
        TypeForm.Generic => Type(type.Element!) + "<" + Inside(type) + string.Join(", ", type.Arguments.Select(Type)) + ">",
        TypeForm.Nested => Type(type.Element!) + "." + Inside(type) + Id(type.Name) + OptionalArguments(type.Arguments),
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

    static string NamedType(string name) => Cs.PrimitiveNames.Values.Contains(name) || name is "nint" or "nuint" or "dynamic" ? name : Qualified(name);

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

    internal static string Attribute(CsAttribute attribute) => InlineComments.Apply(attribute with { Comments = new(attribute.Comments.Where(c => c.Placement != CommentPlacement.Container)) },
        "[" + (attribute.Target == null ? "" : attribute.Target + ": ") + Type(attribute.Type) +
        AttributeArguments(attribute.Arguments, Inside(attribute)) + "]");

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
