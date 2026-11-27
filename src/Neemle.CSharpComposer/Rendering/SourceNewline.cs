using System.Runtime.CompilerServices;
using System.Text;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal static class SourceNewline
{
    static readonly ConditionalWeakTable<string, string> Dominants = new();

    // GENE-29 F4 (AD C3): the newline a parsed document or tree renders with when RenderOptions.Newline is null: "\r\n"
    // when its layout (the text outside literal tokens) holds more CRLF than lone LF line breaks, else "\n" (a tie and a
    // lone CR count as LF). Kept per source text, so the renders of one parse decide it once.
    internal static string Dominant(string text) => Dominants.GetValue(text, source =>
    {
        int crlf = 0, lf = 0;
        foreach (var (start, end) in Layout(source))
            for (int i = source.IndexOf('\n', start, end - start); i >= 0; i = source.IndexOf('\n', i + 1, end - i - 1))
                if (i > start && source[i - 1] == '\r') crlf++;
                else lf++;
        return crlf > lf ? "\r\n" : "\n";
    });

    // AD C4: every line break outside a literal token (layout, comments' surroundings and directive lines) becomes
    // `newline`; line breaks inside string literals, comments and inactive text stay as written.
    internal static string Convert(string text, string newline)
    {
        var output = new StringBuilder();
        int position = 0;
        foreach (var (start, end) in Layout(text))
        {
            output.Append(text, position, start - position);
            output.Append(text[start..end].Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
                .Replace("\n", newline, StringComparison.Ordinal));
            position = end;
        }
        return output.Append(text, position, text.Length - position).ToString();
    }

    static IEnumerable<(int Start, int End)> Layout(string text)
    {
        int position = 0;
        foreach (var token in Lexer.Scan(text))
        {
            if (token.Start > position) yield return (position, token.Start);
            if (token.Text.StartsWith('#') && !token.IsDisabled) yield return (token.Start, token.End);
            position = Math.Max(position, token.End);
        }
    }
}
