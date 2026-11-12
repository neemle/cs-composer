using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class DirectiveWriter
{
    internal static string Write(CsDirective d)
    {
        string Quoted(string? text) => Names.Expression(Cs.Literal(text ?? ""));
        string action = d.Action.ToString().ToLowerInvariant();
        return d.Kind switch
        {
            DirectiveKind.If or DirectiveKind.Elif => "#" + d.Kind.ToString().ToLowerInvariant() + " " + Condition(d.Condition!),
            DirectiveKind.Define or DirectiveKind.Undef => "#" + d.Kind.ToString().ToLowerInvariant() + " " + Names.Id(d.Symbol!),
            DirectiveKind.Line => "#line " + (d.Action != DirectiveAction.None ? action : d.Start != null ?
                "(" + d.Start.Line + ", " + d.Start.Column + ")-(" + d.End!.Line + ", " + d.End.Column + ")" + (d.CharacterOffset == null ? "" : " " + d.CharacterOffset) + " " + Quoted(d.File) :
                d.Line + (d.File == null ? "" : " " + Quoted(d.File))),
            DirectiveKind.PragmaWarning => "#pragma warning " + action + (d.Warnings.Count == 0 ? "" : " " + string.Join(", ", d.Warnings)),
            DirectiveKind.PragmaChecksum => "#pragma checksum " + Quoted(d.File) + " " + Quoted(d.ChecksumAlgorithm) + " " + Quoted(d.Checksum),
            DirectiveKind.Nullable => "#nullable " + action + (d.Text == null ? "" : " " + d.Text),
            DirectiveKind.Reference or DirectiveKind.Load => (d.Kind == DirectiveKind.Reference ? "#r " : "#load ") + Quoted(d.File),
            DirectiveKind.Ignored => "#:" + d.Text,
            DirectiveKind.Shebang => "#!" + d.Text,
            DirectiveKind.Sdk or DirectiveKind.Package or DirectiveKind.Project or DirectiveKind.Property => "#:" + d.Kind.ToString().ToLowerInvariant() + " " + d.Text,
            _ => "#" + d.Kind.ToString().ToLowerInvariant() + (d.Text == null ? "" : " " + d.Text)
        };
    }
    static string Condition(CsExpression e) => e.Kind switch
    {
        ExpressionKind.Identifier or ExpressionKind.Literal => e.Text,
        ExpressionKind.Parenthesized => "(" + Condition(e.Arguments[0]) + ")",
        ExpressionKind.Unary => "!(" + Condition(e.Arguments[0]) + ")",
        ExpressionKind.Binary => "(" + Condition(e.Arguments[0]) + " " + Operators.Token(e.Operator) + " " + Condition(e.Arguments[1]) + ")",
        _ => throw new ArgumentException("Invalid preprocessor condition.")
    };
}
