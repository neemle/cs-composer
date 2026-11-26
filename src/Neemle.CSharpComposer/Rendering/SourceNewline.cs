using System.Text;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal static class SourceNewline
{
    static readonly char[] Breaks = { '\r', '\n' };

    internal static string? At(string text, int index) => index >= text.Length ? null : text[index] switch
    {
        '\n' => "\n",
        '\r' => index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r",
        _ => null
    };

    internal static int NextBreak(string text, int position) => text.IndexOfAny(Breaks, position);

    internal static string? LineEnding(string text, int position)
    {
        int index = NextBreak(text, position);
        return index < 0 ? null : At(text, index);
    }

    internal static string Destination(SourceOrigin origin)
    {
        string text = origin.Snapshot.Text;
        int lineStart = SourceLayout.LineStart(text, origin.CoreStart);
        string? preceding = lineStart == 0 ? null : lineStart >= 2 && text[lineStart - 2] == '\r' &&
            text[lineStart - 1] == '\n' ? "\r\n" : text[lineStart - 1].ToString();
        return LineEnding(text, origin.CoreStart) ?? preceding ?? Dominant(text);
    }

    internal static string Dominant(string text)
    {
        var counts = new Dictionary<string, int>();
        foreach (var (start, end) in Layout(text))
            for (int i = start; i < end; i++)
            {
                if (At(text, i) is not { } newline) continue;
                counts[newline] = counts.GetValueOrDefault(newline) + 1;
                i += newline.Length - 1;
            }
        return counts.OrderByDescending(pair => pair.Value).Select(pair => pair.Key).FirstOrDefault() ?? "\n";
    }

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
