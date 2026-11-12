using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    void ExpressionPayload(CsExpression e, string path, int depth, bool initializerContext)
    {
        bool text = e.Kind is ExpressionKind.Identifier or ExpressionKind.Literal or ExpressionKind.Raw or ExpressionKind.Member or ExpressionKind.Argument or ExpressionKind.Declaration or ExpressionKind.StringLiteral or ExpressionKind.StringText;
        if (!text && e.Text != "") Error("GEN065", path + ".Text", "This expression has no text payload.");
        bool typed = e.Kind is ExpressionKind.New or ExpressionKind.Cast or ExpressionKind.TypeOf or ExpressionKind.Default or ExpressionKind.Array or ExpressionKind.Is or ExpressionKind.As or ExpressionKind.SizeOf or ExpressionKind.StackAlloc or ExpressionKind.Declaration or ExpressionKind.Lambda or ExpressionKind.TypeReference or ExpressionKind.RefValue;
        if (e.Type != null && !typed) Error("GEN065", path + ".Type", "Type payload does not belong to this expression kind.");
        if (e.Type != null) Type(e.Type, path + ".Type", depth + 1);
        if (e.Kind is ExpressionKind.Cast or ExpressionKind.TypeOf or ExpressionKind.TypeReference or ExpressionKind.RefValue && e.Type == null) Required(null, path + ".Type");
        if (e.Pattern != null && e.Kind != ExpressionKind.Is) Error("GEN065", path + ".Pattern", "Pattern belongs to is expressions.");
        if (e.ConditionalAccess && e.Kind is not (ExpressionKind.Member or ExpressionKind.Index))
            Error("GEN065", path + ".ConditionalAccess", "Conditional access needs a member or index.");
        if (e.ArrayRank != 1 && e.Kind != ExpressionKind.Array) Error("GEN065", path + ".ArrayRank", "Rank belongs to array construction.");
        if (e.Initializer != null)
        {
            if (e.Kind is not (ExpressionKind.New or ExpressionKind.Array or ExpressionKind.StackAlloc or ExpressionKind.With) || e.Initializer.Kind != ExpressionKind.Initializer)
                Error("GEN065", path + ".Initializer", "Construction initializer must be an initializer expression.");
            Expression(e.Initializer, path + ".Initializer", depth + 1, initializerContext: true);
        }
        if (e.Kind == ExpressionKind.New)
        {
            if (e.Type == null) Feature("Target-typed construction", CsLanguageVersion.CSharp9, path);
            if (e.Type is { Form: TypeForm.Array or TypeForm.Pointer } or { Form: TypeForm.Named, Name: "void" })
                Error("GEN091", path + ".Type", "Constructor invocation requires a constructible type.");
        }
        if (e.Kind == ExpressionKind.Is)
        {
            if ((e.Type == null) == (e.Pattern == null)) Error("GEN092", path, "Is requires exactly one type or pattern.");
            if (e.Pattern != null) Pattern(e.Pattern, path + ".Pattern", depth + 1, typeTest: true);
        }
        if (e.Kind == ExpressionKind.Array)
        {
            if (e.ArrayRank < 1 || e.ArrayRank > 32) Error("GEN092", path + ".ArrayRank", "Array rank must be between 1 and 32.");
            if (e.Arguments.Count != 0 && e.Arguments.Count != e.ArrayRank) Error("GEN092", path, "Array dimension count must equal rank.");
            if (e.Type == null && (e.Arguments.Count != 0 || e.Initializer?.Arguments == null || e.Initializer.Arguments.Count == 0))
                Error("GEN092", path, "An inferred array requires a nonempty initializer and no explicit sizes.");
            if (e.Arguments.Count == 0 && e.Initializer == null) Error("GEN092", path, "An array needs sizes or an initializer.");
            if (e.Type is { Form: TypeForm.Named, Name: "void" }) Error("GEN092", path + ".Type", "Array element type cannot be void.");
        }
        if (e.Kind == ExpressionKind.Initializer && !initializerContext) Error("GEN092", path, "Initializer requires a construction/initializer context.");
        if (e.Kind is ExpressionKind.Cast or ExpressionKind.TypeOf or ExpressionKind.Default or ExpressionKind.Is or ExpressionKind.Conditional or
            ExpressionKind.Index or ExpressionKind.Array or ExpressionKind.Initializer or ExpressionKind.Parenthesized or ExpressionKind.New && e.Text != "")
            Error("GEN065", path + ".Text", "This expression has no text payload.");
        if (Rendering.ExpressionKinds.Mutation(e.Kind) && e.Arguments.Count == 1 && ConditionalTarget(e.Arguments[0]))
            Error("GEN092", path, "Conditional access cannot be incremented or decremented.");
        if (e.Kind == ExpressionKind.Assignment && e.Operator == CsOperator.Coalesce)
            Feature("Null-coalescing assignment", CsLanguageVersion.CSharp8, path);
        if (e.Kind == ExpressionKind.Assignment && e.Arguments.Count == 2 && ConditionalTarget(e.Arguments[0]))
            Feature("Null-conditional assignment", CsLanguageVersion.CSharp14, path);
    }

    static bool ConditionalTarget(CsExpression? e)
    {
        for (int depth = 0; e != null && depth < 128; depth++)
        {
            if (e.ConditionalAccess) return true;
            if (e.Kind is not (ExpressionKind.Member or ExpressionKind.Index or ExpressionKind.Invocation) || e.Arguments?.Count == 0) return false;
            e = e.Arguments?[0];
        }
        return false;
    }
}
