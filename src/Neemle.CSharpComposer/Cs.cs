using Neemle.CSharpComposer.Builders;
using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Neemle.CSharpComposer;

public static class Cs
{
    public static FileBuilder File(string name) => new FileBuilder(name);
    public static FileBuilder File(string name, CsLanguageVersion version) => new FileBuilder(name, version);
    public static CsType Type(string name) => new CsType(name);
    public static CsType Type<T>() => Type(typeof(T));

    public static CsType Type(System.Type type)
    {
        if (type.IsByRef) throw new ArgumentException("By-ref types belong on parameter modifiers.", nameof(type));
        if (type.IsPointer) return Type(type.GetElementType()!).Pointer();
        if (type.IsArray) return Type(type.GetElementType()!).Array(type.GetArrayRank());
        if (type.IsGenericParameter) return TypeParameter(type.Name);
        if (System.Nullable.GetUnderlyingType(type) is System.Type inner) return Type(inner).Nullable();
        if (PrimitiveNames.TryGetValue(type, out var keyword)) return new CsType(keyword);
        return NamedType(type);
    }

    static CsType NamedType(System.Type type)
    {
        var arguments = type.IsGenericType ? type.GetGenericArguments() : System.Type.EmptyTypes;
        return NamedType(type, arguments);
    }

    static CsType NamedType(System.Type type, System.Type[] arguments)
    {
        string name = type.Name.Split('`')[0];
        if (type.IsNested) return NestedType(type, arguments, name);
        var result = new CsType("global::" + (string.IsNullOrEmpty(type.Namespace)
            ? name : type.Namespace + "." + name));
        return arguments.Length == 0 ? result : result.Generic(arguments.Select(Type).ToArray());
    }

    static CsType NestedType(System.Type type, System.Type[] arguments, string name)
    {
        var declaring = type.DeclaringType!;
        int inherited = declaring.GetGenericArguments().Length;
        return new CsType(name)
        {
            Form = TypeForm.Nested,
            Element = NamedType(declaring, arguments.Take(inherited).ToArray()),
            Arguments = new Values<CsType>(arguments.Skip(inherited).Select(Type))
        };
    }

    internal static readonly IReadOnlyDictionary<System.Type, string> PrimitiveNames =
        new Dictionary<System.Type, string>
        {
            [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
            [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(int)] = "int", [typeof(uint)] = "uint",
            [typeof(long)] = "long", [typeof(ulong)] = "ulong", [typeof(float)] = "float", [typeof(double)] = "double",
            [typeof(decimal)] = "decimal", [typeof(char)] = "char", [typeof(string)] = "string",
            [typeof(object)] = "object"
        };

    public static CsType Generic(string name, params CsType[] arguments) => Type(name).Generic(arguments);
    public static CsType Nullable(CsType type) => type.Nullable();
    public static CsType Array(CsType type, int rank = 1) => type.Array(rank);
    public static CsType Int => Type<int>();
    public static CsExpression String(string value) => Literal(value);
    public static CsType Bool => Type<bool>();
    public static CsType Void => Type(typeof(void));
    public static CsType TypeParameter(string name) => new CsType(name) { Form = TypeForm.Parameter };
    public static CsType RawType(string source) => new CsType(source) { Form = TypeForm.Raw };
    public static CsType Tuple(params TupleElement[] elements) =>
        new CsType("") { Form = TypeForm.Tuple, Elements = elements };
    public static CsExpression Id(string name) =>
        new CsExpression(ExpressionKind.Identifier, name, Values<CsExpression>.Empty);
    public static CsExpression RawExpression(string source) =>
        new CsExpression(ExpressionKind.Raw, source, Values<CsExpression>.Empty);
    public static CsExpression Literal(object? value) =>
        new CsExpression(ExpressionKind.Literal, LiteralText(value), Values<CsExpression>.Empty);
    public static CsExpression Member(CsExpression expression, string name) => expression.Member(name);
    public static CsExpression Call(CsExpression expression, params CsExpression[] arguments) =>
        expression.Call(arguments);
    public static CsExpression Await(CsExpression expression) => expression.Await();
    public static CsExpression Assign(CsExpression left, CsExpression right) =>
        new CsExpression(ExpressionKind.Assignment, "", new[] { left, right });
    public static CsStatement Return(CsExpression? expression = null) =>
        new CsStatement(StatementKind.Return, expression);
    public static CsStatement Statement(CsExpression expression) =>
        new CsStatement(StatementKind.Expression, expression);
    public static CsStatement RawStatement(string source) => new CsStatement(StatementKind.Raw, Text: source);
    public static CsBody Body(params CsStatement[] statements) => new CsBody(statements);
    public static CsExpression Binary(CsOperator op, CsExpression left, CsExpression right) =>
        new CsExpression(ExpressionKind.Binary, "", new[] { left, right }) { Operator = op };
    public static CsExpression Unary(CsOperator op, CsExpression operand) =>
        new CsExpression(ExpressionKind.Unary, "", new[] { operand }) { Operator = op };
    public static CsStatement Local(CsType type, string name, CsExpression? initializer = null) =>
        new CsStatement(StatementKind.Local, initializer, name) { Type = type };
    public static CsStatement Var(string name, CsExpression initializer) =>
        new CsStatement(StatementKind.Local, initializer, name) { IsVar = true };
    public static CsStatement Block(params CsStatement[] statements) =>
        new CsStatement(StatementKind.Block) { Body = Body(statements) };
    public static CsStatement If(CsExpression condition, CsBody body, CsStatement? elseBranch = null) =>
        new CsStatement(StatementKind.If, condition) { Body = body, Else = elseBranch };
    public static CsStatement Try(CsBody body, Values<CsCatch>? catches = null, CsBody? finallyBody = null) =>
        new CsStatement(StatementKind.Try) { Body = body, Catches = catches ?? Values<CsCatch>.Empty,
            Finally = finallyBody };
    public static CsCatch Catch(CsType type, CsBody body, string? name = null) => new CsCatch(body, type, name);
    public static CsCatch CatchAll(CsBody body) => new CsCatch(body);
    public static CsStatement Throw(CsExpression expression) => new CsStatement(StatementKind.Throw, expression);
    public static CsStatement Rethrow() => new CsStatement(StatementKind.Rethrow);
    public static CsStatement Using(CsExpression resource, CsBody body) =>
        new CsStatement(StatementKind.Using, resource) { Body = body };
    public static CsStatement Using(CsStatement declaration, CsBody body) =>
        new CsStatement(StatementKind.Using) { Resource = declaration, Body = body };
    public static CsBody RawBody(string source) => CsBody.FromRaw(source);
    public static CsRawMember RawMember(string source) => new CsRawMember(source);
    public static CsAttribute Attribute(CsType type, params AttributeArgument[] arguments) =>
        new CsAttribute(type) { Arguments = arguments };
    public static AttributeArgument Arg(CsExpression value, string? name = null) => new AttributeArgument(value, name);
    public static CsConstraint Constraint(string kind) => new CsConstraint(kind);
    public static CsConstraint Constraint(CsType type) => new CsConstraint("type", type);

    internal static string LiteralText(object? value) => value switch
    {
        null => "null",
        string text => "\"" + Escape(text, false) + "\"",
        char character => "'" + Escape(character.ToString(), true) + "'",
        bool boolean => boolean ? "true" : "false",
        _ => NumericLiteral(value)
    };

    static string NumericLiteral(object value) => value switch
    {
        float number => FloatLiteral(number),
        double number => DoubleLiteral(number),
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
        byte or sbyte or short or ushort or int => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        _ => throw new ArgumentException("Unsupported literal type; use an explicit raw expression.", nameof(value))
    };

    static string FloatLiteral(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentException("Non-finite float needs an explicit expression.");
        return value.ToString("R", CultureInfo.InvariantCulture) + "F";
    }

    static string DoubleLiteral(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException("Non-finite double needs an explicit expression.");
        return value.ToString("R", CultureInfo.InvariantCulture) + "D";
    }

    static string Escape(string value, bool character)
    {
        var result = new StringBuilder();
        foreach (char item in value) result.Append(EscapedCharacter(item, character));
        return result.ToString();
    }

    static string EscapedCharacter(char value, bool character) => value switch
    {
        '\\' => "\\\\",
        '"' => character ? "\"" : "\\\"",
        '\'' => character ? "\\'" : "'",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        _ => EscapedUnicode(value)
    };

    static string EscapedUnicode(char value) =>
        char.IsControl(value) || char.IsSurrogate(value) || value == '\u2028' || value == '\u2029'
            ? "\\u" + ((int)value).ToString("x4", CultureInfo.InvariantCulture) : value.ToString();
}
