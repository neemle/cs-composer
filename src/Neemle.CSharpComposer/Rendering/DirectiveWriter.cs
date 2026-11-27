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
    // GENE-29 F4: a condition is written with the parentheses its model holds and only those its precedence needs (|| below
    // && below == and !=, below !), so a parsed condition renders as it reads and its render is a fixed point. An operator
    // chain is written in a loop: the parentheses its looser levels need open before the head, then each operator with its
    // right operand, closing the level that needed them (GENE-19 D10).
    static string Condition(CsExpression e, int minimum = 0)
    {
        var operators = new List<CsExpression>();
        for (; e.Kind == ExpressionKind.Binary; e = e.Arguments[0]) operators.Add(e);
        var wrapped = operators.Select((o, i) => Precedence(o) < (i == 0 ? minimum : Precedence(operators[i - 1]))).ToArray();
        var output = new System.Text.StringBuilder().Append('(', wrapped.Count(w => w)).Append(Operand(e));
        for (int i = operators.Count - 1; i >= 0; i--)
        {
            output.Append(' ').Append(Operators.Token(operators[i].Operator)).Append(' ').Append(Condition(operators[i].Arguments[1], Precedence(operators[i]) + 1));
            if (wrapped[i]) output.Append(')');
        }
        return output.ToString();
    }

    static int Precedence(CsExpression e) => e.Operator switch { CsOperator.OrElse => 1, CsOperator.AndAlso => 2, _ => 3 };

    static string Operand(CsExpression e) => e.Kind switch
    {
        ExpressionKind.Identifier or ExpressionKind.Literal => e.Text,
        ExpressionKind.Parenthesized => "(" + Condition(e.Arguments[0]) + ")",
        ExpressionKind.Unary => "!" + Condition(e.Arguments[0], 4),
        _ => throw new ArgumentException("Invalid preprocessor condition.")
    };
}
