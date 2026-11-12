using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class PatternWriter
{
    internal static string Write(CsPattern pattern) => InlineComments.Apply(pattern with { Comments = new(pattern.Comments.Where(c => c.Placement != CommentPlacement.Container)) }, Core(pattern));

    static string Inside(CsPattern p) => string.Concat(p.Comments.Where(c => c.Placement == CommentPlacement.Container).Select(InlineComments.Text));

    static string Core(CsPattern pattern) => pattern.Kind switch
    {
        PatternKind.Recursive => (pattern.Type == null ? "" : Names.Type(pattern.Type)) +
            (pattern.HasPositional ? "(" + Inside(pattern) + string.Join(", ", pattern.Patterns.Select(Write)) + ")" : "") +
            (pattern.HasProperties ? (pattern.Type != null || pattern.HasPositional ? " " : "") + "{ " + (pattern.HasPositional ? "" : Inside(pattern)) + string.Join(", ", pattern.Properties.Select(Write)) + " }" : "") +
            (pattern.Name == null ? "" : " " + Names.Id(pattern.Name)),
        PatternKind.Property => Names.Qualified(pattern.Name!) + ": " + Inside(pattern) + Write(pattern.Patterns[0]),
        PatternKind.List => "[" + Inside(pattern) + string.Join(", ", pattern.Patterns.Select(Write)) + "]" + (pattern.Name == null ? "" : " " + Names.Id(pattern.Name)),
        PatternKind.Slice => ".." + Inside(pattern) + (pattern.Patterns.Count == 0 ? "" : " " + Write(pattern.Patterns[0])),
        PatternKind.Constant => Inside(pattern) + Names.Expression(pattern.Value!),
        PatternKind.Discard => Inside(pattern) + "_",
        PatternKind.Type => Inside(pattern) + Names.Type(pattern.Type!),
        PatternKind.Declaration => Names.Type(pattern.Type!) + " " + Inside(pattern) + Names.Id(pattern.Name!),
        PatternKind.Var => "var " + Inside(pattern) + (pattern.Designation == null ? Names.Id(pattern.Name!) : Names.Expression(SymbolModel.ToExpression(pattern.Designation))),
        PatternKind.Relational => Operators.Token(pattern.Operator) + " " + Inside(pattern) + Names.Expression(pattern.Value!),
        PatternKind.Not => "not " + Inside(pattern) + Child(pattern.Patterns[0], 3),
        PatternKind.And => Child(pattern.Patterns[0], 2) + " and " + Inside(pattern) + Child(pattern.Patterns[1], 3),
        PatternKind.Or => Child(pattern.Patterns[0], 1) + " or " + Inside(pattern) + Child(pattern.Patterns[1], 2),
        PatternKind.Parenthesized => "(" + Inside(pattern) + Write(pattern.Patterns[0]) + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    static string Child(CsPattern pattern, int minimum)
    {
        int precedence = pattern.Kind switch { PatternKind.Or => 1, PatternKind.And => 2, PatternKind.Not => 3, _ => 4 };
        string text = Write(pattern);
        return precedence < minimum ? "(" + text + ")" : text;
    }
}
