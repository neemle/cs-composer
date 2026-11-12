using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
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

    internal int ErrorCount => errors.Count;

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
        var graphErrors = GraphValidator.Validate(document);
        if (graphErrors.Any(e => e.Code is "GEN074" or "GEN075")) return graphErrors;

        if (document.Items == null) return graphErrors;
        if (graphErrors.Count == 0) { validator.CollectTypes(document); validator.DirectiveSequence(document); validator.Documentation(document); }
        using var rootScope = validator.DeclarationScope(document.Items);
        validator.currentNamespace = document.Namespace ?? "";
        if (!Enum.IsDefined(typeof(CsLanguageVersion), document.LanguageVersion))
            validator.Error("GEN072", "LanguageVersion", LanguageFeatures.UnsupportedMessage);
        if (document.NullableContext != NullableMode.Omit)
            validator.Feature("Nullable context", CsLanguageVersion.CSharp8, "NullableContext");
        if (document.Namespace != null) validator.Identifier(document.Namespace, "Namespace", true);
        validator.List(document.Usings, "Usings", validator.Using);
        validator.Attributes(document.Attributes, "Attributes", 0);
        validator.List(document.Types, "Types", (type, path) => validator.members.Validate(type, path, null, 0));
        foreach (var d in document.Items.OfType<CsDelegate>()) validator.members.Validate(d, "Items", null, 0);
        bool typesSeen = false;
        foreach (var item in document.Items)
        {
            if (item is CsTypeDeclaration or CsDelegate or CsNamespace) typesSeen = true;
            if (item is CsDirective directive) validator.Directive(directive, "Items.Directive", 0);
            if (item is CsExternAlias alias) validator.Identifier(alias.Name, "Items.ExternAlias");
            if (item is CsGlobalStatement global)
            {
                validator.Feature("Top-level statements", CsLanguageVersion.CSharp9, "Items.GlobalStatement");
                if (typesSeen) validator.Error("GEN092", "Items.GlobalStatement", "Top-level statements must precede type/namespace declarations.");
                try { validator.Body(SymbolModel.ToBody(Cs.Tree(global.Value)), "Items.GlobalStatement", 0); }
                catch (SymbolModel.InvalidModel e) { validator.errors.Add(e.Diagnostic); }
            }
        }
        foreach (var space in document.Items.OfType<CsNamespace>()) validator.NamespaceItems(space, "Items", 0);
        foreach (var error in graphErrors)
            if (!validator.errors.Any(e => e.Code == error.Code)) validator.errors.Add(error);
        return new Values<GenDiagnostic>(validator.errors);
    }

    void NamespaceItems(CsNamespace space, string path, int depth)
    {
        if (!Depth(depth, path) || !Required(space.Items, path + ".Items")) return;
        using var namespaceScope = NamespaceScope(space.Name);
        using var declarationScope = DeclarationScope(space.Items);
        Identifier(space.Name, path + ".Name", true);
        if (space.IsFileScoped) Feature("File-scoped namespace", CsLanguageVersion.CSharp10, path);
        foreach (var item in space.Items)
        {
            if (item is CsMember type) members.Validate(type, path + ".Types", null, depth + 1);
            else if (item is CsNamespace child) NamespaceItems(child, path + ".Namespace", depth + 1);
            else if (item is CsUsing use) Using(use, path + ".Using");
            else if (item is CsExternAlias alias) Identifier(alias.Name, path + ".ExternAlias");
        }
    }

    void Using(CsUsing value, string path)
    {
        if (value.IsGlobal) Feature("Global using", CsLanguageVersion.CSharp10, path + ".IsGlobal");
        if (value.Target == null) Identifier(value.Name, path + ".Name", true);
        else
        {
            Type(value.Target, path + ".Target", 0);
            if (value.Name != "" || value.Alias == null && !value.IsStatic) Error("GEN065", path, "A type target requires an alias or static import and no name payload.");
            if (value.Alias != null && value.Target.Form is TypeForm.Tuple or TypeForm.Array or TypeForm.Pointer) Feature("Using alias to any type", CsLanguageVersion.CSharp12, path);
        }
        if (value.IsUnsafe)
        {
            Feature("Unsafe using directive", CsLanguageVersion.CSharp12, path);
            if (!value.IsStatic && value.Alias == null) Error("GEN065", path, "Unsafe belongs to a static or alias using.");
        }
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

    internal void Attributes(Values<CsAttribute>? items, string path, int depth) =>
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
        if (type!.Form == TypeForm.Nullable) NullableFeature(type, path);
        TypeShape(type!, path, depth);
    }

    void TypeShape(CsType type, string path, int depth)
    {
        bool named = type.Form is TypeForm.Named or TypeForm.Parameter or TypeForm.Raw or TypeForm.Nested;
        bool element = type.Form is TypeForm.Generic or TypeForm.UnboundGeneric or TypeForm.Nested or TypeForm.Nullable or TypeForm.Array or TypeForm.Pointer or TypeForm.Ref;
        bool arguments = type.Form is TypeForm.Generic or TypeForm.Nested;
        if (!named && type.Name != "" || !element && type.Element != null || !arguments && type.Arguments?.Count > 0 ||
            type.Form != TypeForm.Tuple && type.Elements?.Count > 0 || type.Form is not (TypeForm.Array or TypeForm.UnboundGeneric) && type.Rank != 1)
            Error("GEN065", path, "Payload belongs to another type form.");
        if (type.Form != TypeForm.Ref && type.ReadOnly || type.Form != TypeForm.FunctionPointer &&
            (type.CallingConvention != FunctionPointerConvention.Default || type.UnmanagedConventions?.Count > 0 || type.FunctionParameters?.Count > 0)) Error("GEN065", path, "Payload belongs to another type form.");
        switch (type.Form)
        {
            case TypeForm.UnboundGeneric:
                if (type.Rank < 1) Error("GEN008", path, "Unbound generic arity must be positive.");
                Type(type.Element, path + ".Element", depth + 1); break;
            case TypeForm.Ref:
                if (type.Element?.Form == TypeForm.Ref) Error("GEN012", path, "A ref type cannot contain another ref type.");
                Type(type.Element, path + ".Element", depth + 1); break;
            case TypeForm.FunctionPointer:
                Feature("Function pointer types", CsLanguageVersion.CSharp9, path);
                if (!Enum.IsDefined(type.CallingConvention)) Error("GEN012", path, "Unknown function pointer convention.");
                if (type.UnmanagedConventions?.Count > 0 && type.CallingConvention != FunctionPointerConvention.Unmanaged) Error("GEN012", path, "Calling conventions require unmanaged.");
                if (type.FunctionParameters?.Count == 0) Error("GEN012", path, "Function pointer requires a return type.");
                List(type.FunctionParameters, path + ".FunctionParameters", (parameter, child) =>
                {
                    Type(parameter.Type, child + ".Type", depth + 1);
                    if (parameter.Modifier != null && parameter.Modifier is not ("ref" or "out" or "in" or "ref readonly")) Error("GEN016", child, "Invalid function pointer parameter modifier.");
                });
                if (type.UnmanagedConventions == null) Required(null, path + ".UnmanagedConventions");
                else foreach (var convention in type.UnmanagedConventions) Identifier(convention, path + ".UnmanagedConventions");
                break;
            case TypeForm.Raw: RawType(type, path); break;
            case TypeForm.Named: Identifier(type.Name, path, true); if (type.Name is "nint" or "nuint") Feature("Native integer type", CsLanguageVersion.CSharp9, path); break;
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

    internal void Expression(CsExpression? expression, string path, int depth, bool allowThrow = false, bool initializerContext = false, ExpressionKind? parent = null)
    {
        if (!Required(expression, path) || !Depth(depth, path)) return;
        if (!Required(expression!.Text, path + ".Text")) return;
        if (!Required(expression.Arguments, path + ".Arguments")) return;
        if (expression.Kind == ExpressionKind.Binary && !Operators.Binary(expression.Operator) ||
            expression.Kind == ExpressionKind.Unary && !Operators.Unary(expression.Operator) ||
            expression.Kind == ExpressionKind.Assignment && !Operators.Assignment(expression.Operator))
            Error("GEN064", path + ".Operator", "Operator is not supported for this expression kind.");
        if (expression.Operator != CsOperator.None && expression.Kind != ExpressionKind.Binary &&
            expression.Kind != ExpressionKind.Unary && expression.Kind != ExpressionKind.Assignment)
            Error("GEN065", path + ".Operator", "Payload does not belong to this node kind.");
        if ((expression.Kind == ExpressionKind.Binary || expression.Kind == ExpressionKind.Unary) &&
            expression.Text != "") Error("GEN065", path + ".Text", "Payload does not belong to this node kind.");
        ExpressionPayload(expression, path, depth, initializerContext);
        OperationPayload(expression, path, depth, parent, initializerContext);
        if (!Required(expression.Arms, path + ".Arms")) return;
        if (expression.Arms.Count != 0 && expression.Kind != ExpressionKind.Switch ||
            expression.Kind is ExpressionKind.Switch or ExpressionKind.Throw && expression.Text != "")
            Error("GEN065", path, "Payload does not belong to this expression kind.");
        if (expression.Kind == ExpressionKind.Throw && !allowThrow)
            Error("GEN083", path, "Throw expression requires a coalescing/conditional branch, switch arm or expression-bodied function/member.");
        if (expression.Kind == ExpressionKind.Switch) SwitchExpression(expression, path, depth);
        if (!Required(expression.LambdaParameters, path + ".LambdaParameters")) return;
        if (expression.Kind is not (ExpressionKind.Lambda or ExpressionKind.AnonymousMethod) && (expression.LambdaBody != null || expression.LambdaParameters.Count > 0 ||
            expression.IsAsync || expression.IsStatic) || expression.Kind == ExpressionKind.Lambda && expression.Text != "")
            Error("GEN065", path, "Payload does not belong to this expression kind.");
        if (expression.Kind is ExpressionKind.Lambda or ExpressionKind.AnonymousMethod) Lambda(expression, path, depth);
        if (ExpressionKinds.Mutation(expression.Kind))
        {
            if (expression.Text != "") Error("GEN065", path + ".Text", "Mutation expressions have no text payload.");
            if (expression.Arguments.Count == 1 && expression.Arguments[0] is { } operand && !ExpressionKinds.Assignable(operand))
                Error("GEN088", path, "Increment and decrement require a writable target.");
        }
        if (expression.Kind == ExpressionKind.Assignment && expression.Arguments.Count == 2 &&
            expression.Arguments[0] is { } target && !ExpressionKinds.Assignable(target))
            Error("GEN088", path + ".Arguments[0]", "An assignment requires a writable target.");
        if (InvalidArity(expression)) Error("GEN013", path, "Expression has an invalid number of operands.");
        if (expression.Kind == ExpressionKind.Identifier || expression.Kind == ExpressionKind.Member)
            Identifier(expression.Text, path + ".Text");
        if (!Enum.IsDefined(typeof(ExpressionKind), expression.Kind))
            Error("GEN014", path, "Unknown expression kind.");
        List(expression.Arguments, path + ".Arguments", (argument, child) => Expression(argument, child, depth + 1,
            allowThrow: expression.Kind is ExpressionKind.Conditional || expression.Kind == ExpressionKind.Binary && expression.Operator == CsOperator.Coalesce,
            initializerContext: expression.Kind is ExpressionKind.Initializer or ExpressionKind.AnonymousObject || initializerContext && expression.Kind == ExpressionKind.Assignment, parent: expression.Kind));
    }

    static bool InvalidArity(CsExpression expression)
    {
        int count = expression.Arguments.Count;
        bool unary = expression.Kind == ExpressionKind.Member || expression.Kind == ExpressionKind.Await ||
            expression.Kind == ExpressionKind.Unary || expression.Kind is ExpressionKind.Switch or ExpressionKind.Throw or ExpressionKind.Cast or ExpressionKind.Is or ExpressionKind.Parenthesized or ExpressionKind.As or ExpressionKind.NameOf or ExpressionKind.Checked or ExpressionKind.Unchecked or ExpressionKind.Ref or ExpressionKind.NullForgiving or ExpressionKind.Argument or ExpressionKind.Spread or ExpressionKind.With or ExpressionKind.MakeRef or ExpressionKind.RefType or ExpressionKind.RefValue or ExpressionKind.Interpolation or ExpressionKind.Unsafe || ExpressionKinds.Mutation(expression.Kind);
        bool atomic = expression.Kind == ExpressionKind.Identifier || expression.Kind == ExpressionKind.Literal ||
            expression.Kind is ExpressionKind.Raw or ExpressionKind.Lambda or ExpressionKind.TypeOf or ExpressionKind.Default or ExpressionKind.This or ExpressionKind.Base or ExpressionKind.SizeOf or ExpressionKind.Omitted or ExpressionKind.Query or ExpressionKind.AnonymousMethod or ExpressionKind.TypeReference or ExpressionKind.Field or ExpressionKind.StringLiteral or ExpressionKind.StringText;
        bool binary = expression.Kind == ExpressionKind.Assignment || expression.Kind is ExpressionKind.Binary or ExpressionKind.Range;
        return unary && count != 1 || binary && count != 2 ||
            expression.Kind == ExpressionKind.Invocation && count < 1 || expression.Kind == ExpressionKind.Index && count < 2 ||
            expression.Kind == ExpressionKind.Conditional && count != 3 || atomic && count != 0;
    }

    internal void Parameters(Values<CsParameter>? items, string path, int depth)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        List(items, path, (parameter, child) => Parameter(parameter, child, depth, names));
        if (items != null && items.Take(Math.Max(0, items.Count - 1)).Any(p => p?.IsArgList == true)) Error("GEN016", path, "Arglist must be last.");
    }

    void Parameter(CsParameter parameter, string path, int depth, HashSet<string> names)
    {
        if (parameter.IsArgList)
        {
            if (parameter.Type != null || parameter.Name != "" || parameter.Modifier != null || parameter.Default != null || parameter.Attributes?.Count > 0) Error("GEN065", path, "Arglist has no parameter payload.");
            return;
        }
        LanguageFeatures.Parameter(this, parameter, path);
        Type(parameter.Type, path + ".Type", depth + 1);
        Identifier(parameter.Name, path + ".Name");
        if (!names.Add(Names.Key(parameter.Name))) Error("GEN015", path, "Duplicate parameter name.");
        var modifiers = new[]
        {
            "ref", "out", "in", "params", "this", "this ref", "this in", "ref readonly", "scoped", "scoped ref", "scoped in", "scoped ref readonly", "this ref readonly", "this scoped ref", "this scoped in"
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
        Attributes(parameter.Attributes, path + ".Attributes", depth + 1);
        Identifier(parameter.Name, path + ".Name");
        if (!names.Add(Names.Key(parameter.Name))) Error("GEN017", path, "Duplicate generic parameter.");
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
        else if (constraint.Kind == "default") Feature("Default constraint", CsLanguageVersion.CSharp9, path);
        else if (constraint.Kind == "allows ref struct") Feature("Allows ref struct constraint", CsLanguageVersion.CSharp13, path);
        else if (constraint.Kind == "new()") state.Constructor = true;
        else Error("GEN020", path, "Unknown generic constraint.");
    }

    internal void Body(CsBody? body, string path, int depth, bool inCatch = false, CsStatement? switchContext = null, bool inLoop = false, bool allowThrow = false, IReadOnlyDictionary<string, bool>? labels = null)
    {
        if (!Required(body, path) || !Depth(depth, path)) return;
        if (body!.Tree != null)
        {
            if (body.Expression != null || body.Raw != null || body.Statements?.Count > 0)
                Error("GEN022", path, "A body tree cannot coexist with another body representation.");
            try { Body(SymbolModel.ToBody(body.Tree), path + ".Tree", depth + 1, inCatch, switchContext, inLoop, allowThrow, labels); }
            catch (SymbolModel.InvalidModel error) { errors.Add(error.Diagnostic); }
            return;
        }
        if (!Required(body.Statements, path + ".Statements")) return;
        int representations = (body.Raw != null ? 1 : 0) + (body.Expression != null ? 1 : 0) +
            (body.Statements.Count > 0 ? 1 : 0);
        if (representations > 1)
            Error("GEN022", path, "Body must use a single raw, expression, or statement representation.");
        if (body.Expression != null) Expression(body.Expression, path + ".Expression", depth + 1, allowThrow: true);
        var statements = new StatementValidator(this, inCatch, switchContext, inLoop, labels);
        List(body.Statements, path + ".Statements", (statement, child) => statements.Validate(statement, child, depth));
    }

    internal void Accessors(Values<CsAccessor>? items, string path, int depth, bool isEvent, bool abstractMember)
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
        if (accessor.Modifiers == null) Required(null, path + ".Modifiers");
        else if (accessor.Modifiers.Count > 1 || accessor.Modifiers.Any(m => m != "readonly")) Error("GEN031", path, "Invalid accessor modifier.");
        if (accessor.Modifiers?.Contains("readonly") == true) Feature("Readonly accessor", CsLanguageVersion.CSharp8, path);
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
