using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class PatternWriter
{
    internal static string Write(CsPattern pattern) => MeaningGuard.Parenthesize(pattern, InlineComments.Apply(pattern with { Comments = new(pattern.Comments.Where(c => c.Placement != CommentPlacement.Container)) }, Core(pattern)));

    static string Inside(CsPattern p) => string.Concat(p.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text));

    static string Core(CsPattern pattern) => pattern.Kind switch
    {
        PatternKind.Recursive => (pattern.Type == null ? "" : Names.Type(pattern.Type)) +
            (pattern.HasPositional ? "(" + Inside(pattern) + string.Join(", ", pattern.Patterns.Select(Write)) + ")" : "") +
            (pattern.HasProperties ? (pattern.Type != null || pattern.HasPositional ? " " : "") + Properties(pattern) : "") +
            (pattern.Name == null ? "" : " " + Names.Designation(pattern.Name)),
        PatternKind.Property => Names.Qualified(pattern.Name!) + ": " + Inside(pattern) + Write(pattern.Patterns[0]),
        PatternKind.List => "[" + Inside(pattern) + string.Join(", ", pattern.Patterns.Select(Write)) + "]" + (pattern.Name == null ? "" : " " + Names.Designation(pattern.Name)),
        PatternKind.Slice => ".." + Inside(pattern) + (pattern.Patterns.Count == 0 ? "" : " " + Write(pattern.Patterns[0])),
        PatternKind.Constant => Inside(pattern) + Constant(pattern.Value!),
        PatternKind.Discard => Inside(pattern) + "_",
        PatternKind.Type => Inside(pattern) + Names.Type(pattern.Type!),
        PatternKind.Declaration => Names.Type(pattern.Type!) + " " + Inside(pattern) + Names.Designation(pattern.Name!),
        PatternKind.Var => "var " + Inside(pattern) + (pattern.Designation == null ? Names.Designation(pattern.Name!) : Names.Expression(SymbolModel.ToExpression(pattern.Designation, "Designation"))),
        // GENE-29 F3: a relational bound is a shift expression; anything that binds looser is written in parentheses.
        PatternKind.Relational => Operators.Token(pattern.Operator) + " " + Inside(pattern) + (ExpressionWriter.Precedence(pattern.Value!) < Operators.Precedence(CsOperator.LeftShift)
            ? "(" + Names.Expression(pattern.Value!) + ")" : Names.Expression(pattern.Value!)),
        PatternKind.Not => "not " + Inside(pattern) + Child(pattern.Patterns[0], 3),
        PatternKind.And => Child(pattern.Patterns[0], 2) + " and " + Inside(pattern) + Child(pattern.Patterns[1], 3),
        PatternKind.Or => Child(pattern.Patterns[0], 1) + " or " + Inside(pattern) + Child(pattern.Patterns[1], 2),
        PatternKind.Parenthesized => "(" + Inside(pattern) + Write(pattern.Patterns[0]) + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    // A property clause; an empty one is `{ }` (GENE-29 F6).
    static string Properties(CsPattern pattern)
    {
        string inner = (pattern.HasPositional ? "" : Inside(pattern)) + string.Join(", ", pattern.Properties.Select(Write));
        return inner.Length == 0 ? "{ }" : "{ " + inner.TrimEnd(' ') + " }";
    }

    static string Constant(CsExpression value)
    {
        using var context = Names.Enter(pattern: true);
        return Names.Expression(value);
    }

    static string Child(CsPattern pattern, int minimum)
    {
        int precedence = pattern.Kind switch { PatternKind.Or => 1, PatternKind.And => 2, PatternKind.Not => 3, _ => 4 };
        string text = Write(pattern);
        return precedence < minimum ? "(" + text + ")" : text;
    }
}
