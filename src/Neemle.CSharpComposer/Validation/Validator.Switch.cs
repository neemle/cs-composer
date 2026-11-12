using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    internal void Pattern(CsPattern? pattern, string path, int depth, bool designationAllowed = true)
    {
        if (!Required(pattern, path) || !Depth(depth, path)) return;
        var p = pattern!;
        if (!Enum.IsDefined(p.Kind)) Error("GEN076", path, "Unknown pattern kind.");
        bool value = p.Kind is PatternKind.Constant or PatternKind.Relational;
        bool type = p.Kind is PatternKind.Type or PatternKind.Declaration;
        bool name = p.Kind is PatternKind.Var or PatternKind.Declaration;
        int count = p.Kind is PatternKind.Not or PatternKind.Parenthesized ? 1 :
            p.Kind is PatternKind.And or PatternKind.Or ? 2 : 0;
        if ((!value && p.Value != null) || (!type && p.Type != null) || (!name && p.Name != null) ||
            p.Operator != CsOperator.None && p.Kind != PatternKind.Relational)
            Error("GEN065", path, "Payload does not belong to this pattern kind.");
        if (value) Constant(p.Value, path + ".Value", depth + 1);
        if (type)
        {
            Type(p.Type, path + ".Type", depth + 1);
            if (p.Type?.Form is TypeForm.Nullable or TypeForm.Pointer || p.Type is { Form: TypeForm.Named, Name: "void" or "dynamic" })
                Error("GEN076", path + ".Type", "A pattern cannot use a nullable, pointer, void or dynamic type.");
        }
        if (name)
        {
            Identifier(p.Name, path + ".Name");
            if (!designationAllowed && p.Name != "_")
                Error("GEN076", path + ".Name", "Variables cannot be declared within not or or patterns.");
        }
        if (p.Kind is PatternKind.Type or PatternKind.Relational or PatternKind.Not or PatternKind.And or
            PatternKind.Or or PatternKind.Parenthesized)
            Feature("Type, relational and logical patterns", CsLanguageVersion.CSharp9, path);
        if (p.Kind == PatternKind.Discard) Feature("Discard pattern", CsLanguageVersion.CSharp8, path);
        if (p.Kind == PatternKind.Relational && p.Operator is not (CsOperator.LessThan or
            CsOperator.LessThanOrEqual or CsOperator.GreaterThan or CsOperator.GreaterThanOrEqual))
            Error("GEN076", path + ".Operator", "Relational patterns require <, <=, > or >=.");
        if (p.Kind == PatternKind.Relational && p.Value is { Kind: ExpressionKind.Literal } literal &&
            (literal.Text is "null" or "true" or "false" || literal.Text.StartsWith('"') || literal.Text.StartsWith('@')))
            Error("GEN076", path + ".Value", "A relational pattern requires a numeric or character constant.");
        if (p.Patterns != null && p.Patterns.Count != count)
            Error("GEN076", path + ".Patterns", "Pattern has an invalid number of operands.");
        List(p.Patterns, path + ".Patterns", (child, childPath) => Pattern(child, childPath, depth + 1,
            designationAllowed && p.Kind is not (PatternKind.Not or PatternKind.Or)));
    }

    internal void Constant(CsExpression? expression, string path, int depth)
    {
        Expression(expression, path, depth);
        if (expression != null && !ConstantShape(expression, depth))
            Error("GEN077", path, "A case value or relational bound must be a constant expression.");
    }

    static bool ConstantShape(CsExpression expression, int depth)
    {
        if (depth > 128 || expression.Arguments == null) return false;
        // Names may refer to const fields or enum values; symbol resolution is the
        // compiler's responsibility. Raw explicitly retains the caller's contract.
        return expression.Kind is ExpressionKind.Identifier or ExpressionKind.Member or ExpressionKind.Literal or
            ExpressionKind.Raw or ExpressionKind.Unary or ExpressionKind.Binary &&
            expression.Arguments.All(child => child != null && ConstantShape(child, depth + 1));
    }

    void SwitchExpression(CsExpression expression, string path, int depth)
    {
        Feature("Switch expression", CsLanguageVersion.CSharp8, path);
        if (expression.Arms?.Count == 0) Error("GEN078", path + ".Arms", "Switch expression requires at least one arm.");
        var coverage = new PatternCoverage(this);
        List(expression.Arms, path + ".Arms", (arm, child) =>
        {
            int before = errors.Count;
            Pattern(arm.Pattern, child + ".Pattern", depth + 1);
            if (arm.When != null) Guard(arm.When, child + ".When", depth + 1);
            Expression(arm.Value, child + ".Value", depth + 1, allowThrow: true);
            if (errors.Count == before) coverage.Add(arm.Pattern, arm.When, child);
        });
    }

    internal void Guard(CsExpression expression, string path, int depth)
    {
        Expression(expression, path, depth);
        if (expression.Kind == ExpressionKind.Literal && expression.Text is not ("true" or "false"))
            Error("GEN077", path, "A case guard must be Boolean.");
    }

    internal sealed class PatternCoverage(Validator validation)
    {
        readonly HashSet<string> seen = new(StringComparer.Ordinal);
        bool all;

        static CsNode WithoutComments(CsNode node) => NodeRewrite.Children(node, child => WithoutComments(child)) with
        { Comments = Values<CsComment>.Empty };

        internal void Add(CsPattern pattern, CsExpression? guard, string path)
        {
            string key = PatternWriter.Write((CsPattern)WithoutComments(pattern));
            if (all || seen.Contains(key))
                validation.Error("GEN079", path, "This pattern is subsumed by a preceding unguarded pattern.");
            if (guard != null && !IsTrue(guard)) return;
            seen.Add(key);
            all |= CatchAll(pattern);
        }
    }

    internal static bool IsTrue(CsExpression expression) => expression is { Kind: ExpressionKind.Literal, Text: "true" };

    static bool CatchAll(CsPattern p) => p.Kind is PatternKind.Discard or PatternKind.Var ||
        p.Kind == PatternKind.Parenthesized && CatchAll(p.Patterns[0]) ||
        p.Kind == PatternKind.Or && (CatchAll(p.Patterns[0]) || CatchAll(p.Patterns[1]));
}
