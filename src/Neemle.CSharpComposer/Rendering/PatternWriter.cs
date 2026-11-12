using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class PatternWriter
{
    internal static string Write(CsPattern pattern) => InlineComments.Apply(pattern, Core(pattern));

    static string Core(CsPattern pattern) => pattern.Kind switch
    {
        PatternKind.Constant => Names.Expression(pattern.Value!),
        PatternKind.Discard => "_",
        PatternKind.Type => Names.Type(pattern.Type!),
        PatternKind.Declaration => Names.Type(pattern.Type!) + " " + Names.Id(pattern.Name!),
        PatternKind.Var => "var " + Names.Id(pattern.Name!),
        PatternKind.Relational => Operators.Token(pattern.Operator) + " " + Names.Expression(pattern.Value!),
        PatternKind.Not => "not " + Child(pattern.Patterns[0], 3),
        PatternKind.And => Child(pattern.Patterns[0], 2) + " and " + Child(pattern.Patterns[1], 3),
        PatternKind.Or => Child(pattern.Patterns[0], 1) + " or " + Child(pattern.Patterns[1], 2),
        PatternKind.Parenthesized => "(" + Write(pattern.Patterns[0]) + ")",
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };

    static string Child(CsPattern pattern, int minimum)
    {
        int precedence = pattern.Kind switch { PatternKind.Or => 1, PatternKind.And => 2, PatternKind.Not => 3, _ => 4 };
        string text = Write(pattern);
        return precedence < minimum ? "(" + text + ")" : text;
    }
}
