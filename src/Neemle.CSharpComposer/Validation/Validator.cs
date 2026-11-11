using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

internal sealed class Validator
{
    readonly List<GenDiagnostic> errors = new List<GenDiagnostic>();
    readonly MemberValidator members;
    readonly CsLanguageVersion version;

    Validator(CsLanguageVersion version)
    {
        this.version = version;
        members = new MemberValidator(this);
    }

    internal void Feature(string name, CsLanguageVersion minimum, string path)
    {
        if (version < minimum)
            Error("GEN073", path, name + " requires C# " + LanguageFeatures.Display(minimum) + ".");
    }

    internal void Error(string code, string path, string message) =>
        errors.Add(new GenDiagnostic(code, path, message));

    internal bool Depth(int depth, string path)
    {
        if (depth <= 128) return true;
        Error("GEN001", path, "Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        return false;
    }

    internal bool Required(object? value, string path)
    {
        if (value != null) return true;
        Error("GEN002", path, "Required node or collection is null.");
        return false;
    }

    internal void Identifier(string? name, string path, bool qualified = false)
    {
        if (qualified ? Names.IsQualified(name) : Names.IsIdentifier(name)) return;
        Error("GEN003", path, "Invalid " + (qualified ? "qualified " : "") + "identifier.");
    }

    internal static IReadOnlyList<GenDiagnostic> Validate(CsDocument document)
    {
        var validator = new Validator(document.LanguageVersion);
        if (!Enum.IsDefined(typeof(CsLanguageVersion), document.LanguageVersion))
            validator.Error("GEN072", "LanguageVersion", LanguageFeatures.UnsupportedMessage);
        if (document.NullableContext != NullableMode.Omit)
            validator.Feature("Nullable context", CsLanguageVersion.CSharp8, "NullableContext");
        validator.List(document.Usings, "Usings", validator.Using);
        validator.Attributes(document.Attributes, "Attributes", 0);
        validator.List(document.Types, "Types", (type, path) => validator.members.Validate(type, path, null, 0));
        return new Values<GenDiagnostic>(validator.errors);
    }

    void Using(CsUsing value, string path)
    {
        if (value.IsGlobal) Feature("Global using", CsLanguageVersion.CSharp10, path + ".IsGlobal");
        Identifier(value.Name, path + ".Name", true);
        if (value.Alias != null) Identifier(value.Alias, path + ".Alias");
        if (value.IsStatic && value.Alias != null)
            Error("GEN005", path, "Using aliases cannot be static.");
    }

    internal void List<T>(Values<T>? items, string path, Action<T, string> visit)
    {
        if (!Required(items, path)) return;
        for (int i = 0; i < items!.Count; i++)
        {
            string child = path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            if (Required(items[i], child)) visit(items[i], child);
        }
    }

    internal void Attributes(Values<CsAttribute> items, string path, int depth) =>
        List(items, path, (attribute, child) => Attribute(attribute, child, depth));

    void Attribute(CsAttribute attribute, string path, int depth)
    {
        Type(attribute.Type, path + ".Type", depth + 1);
        var targets = new[]
        {
            "assembly", "module", "field", "event", "method", "param", "property", "return", "type", "typevar"
        };
        if (attribute.Target != null && !targets.Contains(attribute.Target))
            Error("GEN006", path, "Unknown attribute target.");
        List(attribute.Arguments, path + ".Arguments", (argument, child) =>
        {
            if (argument.Name != null) Identifier(argument.Name, child + ".Name");
            Expression(argument.Value, child + ".Value", depth + 1);
        });
    }

    internal void Type(CsType? type, string path, int depth)
    {
        if (!Required(type, path) || !Depth(depth, path)) return;
        if (type!.Form == TypeForm.Nullable && LanguageFeatures.IsAnnotation(type.Element))
            Feature("Nullable annotation", CsLanguageVersion.CSharp8, path);
        TypeShape(type!, path, depth);
    }

    void TypeShape(CsType type, string path, int depth)
    {
        switch (type.Form)
        {
            case TypeForm.Raw: RawType(type, path); break;
            case TypeForm.Named: Identifier(type.Name, path, true); break;
            case TypeForm.Parameter: Identifier(type.Name, path); break;
            case TypeForm.Nested: NestedType(type, path, depth); break;
            case TypeForm.Generic: GenericType(type, path, depth); break;
            case TypeForm.Array: ArrayType(type, path, depth); break;
            case TypeForm.Pointer: Type(type.Element, path + ".Element", depth + 1); break;
            case TypeForm.Nullable: NullableType(type, path, depth); break;
            case TypeForm.Tuple: TupleType(type, path, depth); break;
            default: Error("GEN012", path, "Unknown type form."); break;
        }
    }

    void RawType(CsType type, string path)
    {
        if (string.IsNullOrWhiteSpace(type.Name)) Error("GEN007", path, "Raw type source is empty.");
    }

    void NestedType(CsType type, string path, int depth)
    {
        Identifier(type.Name, path);
        Type(type.Element, path + ".Element", depth + 1);
        List(type.Arguments, path + ".Arguments", (argument, child) => Type(argument, child, depth + 1));
    }

    void GenericType(CsType type, string path, int depth)
    {
        Type(type.Element, path + ".Element", depth + 1);
        if (type.Arguments != null && type.Arguments.Count == 0)
            Error("GEN008", path, "Generic type needs at least one argument.");
        List(type.Arguments, path + ".Arguments", (argument, child) => Type(argument, child, depth + 1));
    }

    void ArrayType(CsType type, string path, int depth)
    {
        if (type.Rank < 1 || type.Rank > 32) Error("GEN009", path, "Array rank must be between 1 and 32.");
        Type(type.Element, path + ".Element", depth + 1);
    }

    void NullableType(CsType type, string path, int depth)
    {
        if (type.Element?.Form == TypeForm.Nullable) Error("GEN010", path, "Nullable type is already nullable.");
        Type(type.Element, path + ".Element", depth + 1);
    }

    void TupleType(CsType type, string path, int depth)
    {
        if (type.Elements != null && type.Elements.Count < 2)
            Error("GEN011", path, "Tuple type needs at least two elements.");
        List(type.Elements, path + ".Elements", (element, child) =>
        {
            Type(element.Type, child + ".Type", depth + 1);
            if (element.Name != null) Identifier(element.Name, child + ".Name");
        });
    }

    internal void Expression(CsExpression? expression, string path, int depth)
    {
        if (!Required(expression, path) || !Depth(depth, path)) return;
        if (!Required(expression!.Arguments, path + ".Arguments")) return;
        if (expression.Kind == ExpressionKind.Binary && !Operators.Binary(expression.Operator) ||
            expression.Kind == ExpressionKind.Unary && !Operators.Unary(expression.Operator))
            Error("GEN064", path + ".Operator", "Operator is not supported for this expression kind.");
        if (expression.Operator != CsOperator.None && expression.Kind != ExpressionKind.Binary &&
            expression.Kind != ExpressionKind.Unary)
            Error("GEN065", path + ".Operator", "Payload does not belong to this node kind.");
        if ((expression.Kind == ExpressionKind.Binary || expression.Kind == ExpressionKind.Unary) &&
            expression.Text != "") Error("GEN065", path + ".Text", "Payload does not belong to this node kind.");
        if (InvalidArity(expression)) Error("GEN013", path, "Expression has an invalid number of operands.");
        if (expression.Kind == ExpressionKind.Identifier || expression.Kind == ExpressionKind.Member)
            Identifier(expression.Text, path + ".Text");
        if (!Enum.IsDefined(typeof(ExpressionKind), expression.Kind))
            Error("GEN014", path, "Unknown expression kind.");
        List(expression.Arguments, path + ".Arguments", (argument, child) => Expression(argument, child, depth + 1));
    }

    static bool InvalidArity(CsExpression expression)
    {
        int count = expression.Arguments.Count;
        bool unary = expression.Kind == ExpressionKind.Member || expression.Kind == ExpressionKind.Await ||
            expression.Kind == ExpressionKind.Unary;
        bool atomic = expression.Kind == ExpressionKind.Identifier || expression.Kind == ExpressionKind.Literal ||
            expression.Kind == ExpressionKind.Raw;
        bool binary = expression.Kind == ExpressionKind.Assignment || expression.Kind == ExpressionKind.Binary;
        return unary && count != 1 || binary && count != 2 ||
            expression.Kind == ExpressionKind.Invocation && count < 1 || atomic && count != 0;
    }

    internal void Parameters(Values<CsParameter>? items, string path, int depth)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        List(items, path, (parameter, child) => Parameter(parameter, child, depth, names));
    }

    void Parameter(CsParameter parameter, string path, int depth, HashSet<string> names)
    {
        LanguageFeatures.Parameter(this, parameter, path);
        Type(parameter.Type, path + ".Type", depth + 1);
        Identifier(parameter.Name, path + ".Name");
        if (!names.Add(parameter.Name)) Error("GEN015", path, "Duplicate parameter name.");
        var modifiers = new[]
        {
            "ref", "out", "in", "params", "this", "this ref", "this in", "ref readonly", "scoped ref", "scoped in"
        };
        if (parameter.Modifier != null && !modifiers.Contains(parameter.Modifier))
            Error("GEN016", path, "Invalid parameter modifier.");
        if (parameter.Default != null) Expression(parameter.Default, path + ".Default", depth + 1);
        Attributes(parameter.Attributes, path + ".Attributes", depth + 1);
    }

    internal void Generics(Values<TypeParameter>? items, string path, int depth, bool allowVariance)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        List(items, path, (parameter, child) => GenericParameter(parameter, child, depth, allowVariance, names));
    }

    void GenericParameter(TypeParameter parameter, string path, int depth, bool allowVariance, HashSet<string> names)
    {
        Identifier(parameter.Name, path + ".Name");
        if (!names.Add(parameter.Name)) Error("GEN017", path, "Duplicate generic parameter.");
        if (parameter.Variance != null &&
            (!allowVariance || parameter.Variance != "in" && parameter.Variance != "out"))
            Error("GEN018", path, "Variance is permitted only as in/out on interface type parameters.");
        var state = new ConstraintState();
        List(parameter.Constraints, path + ".Constraints", (constraint, child) =>
            Constraint(constraint, child, depth, state));
        if (state.Primary > 1 || state.Structure && state.Constructor)
            Error("GEN021", path, "Incompatible generic constraints.");
    }

    void Constraint(CsConstraint constraint, string path, int depth, ConstraintState state)
    {
        if (constraint.Kind == "class?" || constraint.Kind == "notnull")
            Feature("Nullable constraint", CsLanguageVersion.CSharp8, path);
        if (!state.Seen.Add(constraint)) Error("GEN019", path, "Duplicate generic constraint.");
        if (constraint.Kind == "type") Type(constraint.Type, path + ".Type", depth + 1);
        else if (new[] { "class", "class?", "struct", "notnull", "unmanaged" }.Contains(constraint.Kind))
        {
            state.Primary++;
            if (constraint.Kind == "struct" || constraint.Kind == "unmanaged") state.Structure = true;
        }
        else if (constraint.Kind == "new()") state.Constructor = true;
        else Error("GEN020", path, "Unknown generic constraint.");
    }

    internal void Body(CsBody? body, string path, int depth, bool inCatch = false)
    {
        if (!Required(body, path) || !Depth(depth, path)) return;
        if (!Required(body!.Statements, path + ".Statements")) return;
        int representations = (body.Raw != null ? 1 : 0) + (body.Expression != null ? 1 : 0) +
            (body.Statements.Count > 0 ? 1 : 0);
        if (representations > 1)
            Error("GEN022", path, "Body must use a single raw, expression, or statement representation.");
        if (body.Expression != null) Expression(body.Expression, path + ".Expression", depth + 1);
        var statements = new StatementValidator(this, inCatch);
        List(body.Statements, path + ".Statements", (statement, child) => statements.Validate(statement, child, depth));
    }

    internal void Accessors(Values<CsAccessor> items, string path, int depth, bool isEvent, bool abstractMember)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List(items, path, (accessor, child) =>
        {
            if (!seen.Add(accessor.Kind)) Error("GEN024", child, "Duplicate accessor.");
            Accessor(accessor, child, depth, isEvent, abstractMember);
        });
        if (seen.Contains("set") && seen.Contains("init"))
            Error("GEN029", path, "Property cannot combine set and init.");
        if (isEvent && seen.Count != 0 && !(seen.Contains("add") && seen.Contains("remove")))
            Error("GEN030", path, "Custom event needs both add and remove.");
    }

    void Accessor(CsAccessor accessor, string path, int depth, bool isEvent, bool abstractMember)
    {
        if (accessor.Kind == "init") Feature("Init accessor", CsLanguageVersion.CSharp9, path + ".Kind");
        if (!(isEvent ? new[] { "add", "remove" } : new[] { "get", "set", "init" }).Contains(accessor.Kind))
            Error("GEN025", path, "Invalid accessor kind.");
        var access = new[] { "private", "protected", "internal", "protected internal", "private protected" };
        if (accessor.Accessibility != null && !access.Contains(accessor.Accessibility))
            Error("GEN026", path, "Invalid accessor accessibility.");
        if (abstractMember && accessor.Body != null) Error("GEN027", path, "Abstract accessor cannot have a body.");
        if (isEvent && accessor.Body == null) Error("GEN028", path, "Custom event accessor needs a body.");
        if (accessor.Body != null) Body(accessor.Body, path + ".Body", depth + 1);
        Attributes(accessor.Attributes, path + ".Attributes", depth + 1);
    }

    sealed class ConstraintState
    {
        internal readonly HashSet<CsConstraint> Seen = new HashSet<CsConstraint>();
        internal int Primary;
        internal bool Structure;
        internal bool Constructor;
    }
}
