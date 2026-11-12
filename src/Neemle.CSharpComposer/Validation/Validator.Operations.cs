using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    void OperationPayload(CsExpression e, string path, int depth, ExpressionKind? parent, bool initializerContext)
    {
        if (e.Kind == ExpressionKind.Unsafe) Feature("Unsafe expression", CsLanguageVersion.Preview, path);
        if (e.Kind == ExpressionKind.NameOf && e.Arguments.Any(a => a?.Type != null && HasUnboundType(a.Type))) Feature("Unbound generic nameof", CsLanguageVersion.CSharp14, path);
        bool stringOperation = e.Kind is ExpressionKind.StringLiteral or ExpressionKind.InterpolatedString;
        if (!Enum.IsDefined(e.StringStyle)) Error("GEN092", path, "Unknown string style.");
        if (e.StringStyle != CsStringStyle.Regular && !stringOperation || e.Utf8 && e.Kind != ExpressionKind.StringLiteral || (e.Alignment != null || e.Format != null) && e.Kind != ExpressionKind.Interpolation) Error("GEN065", path, "String payload belongs to another expression kind.");
        if (e.StringStyle == CsStringStyle.Raw || e.Utf8 || e.Kind == ExpressionKind.Literal && (e.Text.EndsWith("u8", StringComparison.Ordinal) || e.Text.StartsWith("\"\"\"", StringComparison.Ordinal))) Feature("Raw or UTF-8 string", CsLanguageVersion.CSharp11, path);
        if (e.Kind == ExpressionKind.InterpolatedString && e.Arguments.Any(a => a?.Kind is not (ExpressionKind.StringText or ExpressionKind.Interpolation))) Error("GEN092", path, "Interpolated string requires text/interpolation parts.");
        if (e.Kind is ExpressionKind.StringText or ExpressionKind.Interpolation && parent != ExpressionKind.InterpolatedString) Error("GEN092", path, "String part belongs to an interpolated string.");
        if (e.Alignment != null) Expression(e.Alignment, path + ".Alignment", depth + 1);
        if ((e.PointerAccess || e.AliasAccess) && (e.Kind != ExpressionKind.Member || e.ConditionalAccess || e.PointerAccess && e.AliasAccess)) Error("GEN065", path, "Pointer/alias access belongs to a named member with one access mode.");
        if (e.AliasAccess && e.Arguments.Count == 1 && e.Arguments[0]?.Kind != ExpressionKind.Identifier) Error("GEN092", path, "Alias qualifier requires a simple alias name.");
        if (e.Kind == ExpressionKind.Field) Feature("Field-backed property", CsLanguageVersion.CSharp14, path);
        if (e.Kind is not (ExpressionKind.Lambda or ExpressionKind.AnonymousMethod) && (e.LambdaAttributes?.Count > 0 || e.ReturnModifier != null || !e.HasParameterList)) Error("GEN065", path, "Function signature belongs to a function expression.");
        if (e.LambdaAttributes == null) Required(null, path + ".LambdaAttributes");
        if (e.Kind == ExpressionKind.Query) Query(e.QueryClauses, path + ".QueryClauses", depth + 1, first: true);
        else if (e.QueryClauses?.Count > 0) Error("GEN065", path, "Query clauses belong to a query expression.");
        if (e.QueryClauses == null) Required(null, path + ".QueryClauses");
        if (!Enum.IsDefined(e.ArgumentModifier)) Error("GEN092", path, "Unknown argument modifier.");
        if (e.ArgumentModifier != ArgumentModifier.None && e.Kind != ExpressionKind.Argument)
            Error("GEN065", path, "Argument modifier belongs to an argument expression.");
        if (e.TypeArguments?.Count > 0 && e.Kind is not (ExpressionKind.Identifier or ExpressionKind.Member))
            Error("GEN065", path, "Generic type arguments require a name reference.");
        List(e.TypeArguments, path + ".TypeArguments", (type, child) => Type(type, child, depth + 1));
        if (e.Kind is ExpressionKind.As or ExpressionKind.SizeOf && e.Type == null) Required(null, path + ".Type");
        if (e.Kind is ExpressionKind.Tuple && e.Arguments.Count < 2) Error("GEN092", path, "Tuple needs at least two elements.");
        if (e.Kind == ExpressionKind.Argument)
        {
            if (e.Text != "") Identifier(e.Text, path + ".Text");
            if (parent is not (ExpressionKind.Invocation or ExpressionKind.New or ExpressionKind.Tuple or ExpressionKind.Index or ExpressionKind.CollectionArguments))
                Error("GEN092", path, "Argument requires an invocation, construction, index or tuple.");
            if (parent == ExpressionKind.Tuple && e.ArgumentModifier != ArgumentModifier.None)
                Error("GEN092", path, "Tuple elements cannot have argument modifiers.");
        }
        if (e.Kind == ExpressionKind.Omitted && parent != ExpressionKind.Range) Error("GEN092", path, "Omitted endpoint belongs only to a range.");
        if (e.Kind == ExpressionKind.CollectionArguments)
        {
            Feature("Collection expression arguments", CsLanguageVersion.CSharp15, path);
            if (parent != ExpressionKind.Collection) Error("GEN092", path, "Collection arguments belong to a collection expression.");
        }
        if (e.Kind == ExpressionKind.Collection && e.Arguments.Where((a, i) => a?.Kind == ExpressionKind.CollectionArguments && i != 0).Any()) Error("GEN092", path, "Collection arguments must be the first element.");
        if (e.Kind == ExpressionKind.Spread && parent != ExpressionKind.Collection) Error("GEN092", path, "Spread belongs only to a collection expression.");
        if (e.Kind == ExpressionKind.ImplicitIndex && !initializerContext) Error("GEN092", path, "Implicit receiver index requires an initializer.");
        if (e.Kind == ExpressionKind.Declaration)
        {
            if (e.Text != "") Identifier(e.Text, path + ".Text");
            else if (e.Type != null || e.Arguments.Count < 2 || e.Arguments.Any(a => a?.Kind is not (ExpressionKind.Identifier or ExpressionKind.Tuple))) Error("GEN092", path, "Var deconstruction requires a tuple designation.");
            if (e.Text != "" && e.Arguments.Count > 0) Error("GEN013", path, "Named declaration has no designation operands.");
            if (parent is not (ExpressionKind.Argument or ExpressionKind.Tuple or ExpressionKind.Assignment)) Error("GEN092", path, "Declaration operand requires an out argument or deconstruction.");
        }
        if (e.Kind == ExpressionKind.StackAlloc)
        {
            if (e.Arguments.Count > 1 || e.Arguments.Count == 0 && e.Initializer == null || e.Type == null && e.Initializer == null)
                Error("GEN092", path, "Stack allocation requires a size or initializer.");
        }
        if (e.Kind == ExpressionKind.With && e.Initializer == null) Required(null, path + ".Initializer");
        if (e.Kind is ExpressionKind.Range || e.Kind == ExpressionKind.Unary && e.Operator == CsOperator.FromEnd)
            Feature("Indices and ranges", CsLanguageVersion.CSharp8, path);
        if (e.Kind == ExpressionKind.NullForgiving) Feature("Null-forgiving operator", CsLanguageVersion.CSharp8, path);
        if (e.Kind == ExpressionKind.With) Feature("With expression", CsLanguageVersion.CSharp9, path);
        if (e.Kind is ExpressionKind.Collection or ExpressionKind.Spread) Feature("Collection expression", CsLanguageVersion.CSharp12, path);
        if (e.Operator == CsOperator.UnsignedRightShift) Feature("Unsigned right shift", CsLanguageVersion.CSharp11, path);
    }
    static bool HasUnboundType(CsType type, int depth = 0) => depth <= 128 && (type.Form == TypeForm.UnboundGeneric || type.Element != null && HasUnboundType(type.Element, depth + 1) || type.Arguments?.Any(t => t != null && HasUnboundType(t, depth + 1)) == true);
}
