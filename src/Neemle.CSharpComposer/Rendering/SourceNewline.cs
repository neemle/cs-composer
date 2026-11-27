using System.Runtime.CompilerServices;
using System.Text;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal static class SourceNewline
{
    static readonly char[] Breaks = { '\r', '\n' };

    static readonly ConditionalWeakTable<string, int[]> LayoutBreaks = new();

    internal static string? At(string text, int index) => index >= text.Length ? null : text[index] switch
    {
        '\n' => "\n",
        '\r' => index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r",
        _ => null
    };

    internal static int NextBreak(string text, int position) => text.IndexOfAny(Breaks, position);

    internal static string? LineEnding(string text, int position)
    {
        int[] breaks = LayoutBreakIndexes(text);
        int index = FirstAtOrAfter(breaks, position);
        return index < breaks.Length ? At(text, breaks[index]) : null;
    }

    internal static string Destination(SourceOrigin origin)
    {
        string text = origin.Snapshot.Text;
        int[] breaks = LayoutBreakIndexes(text);
        int index = FirstAtOrAfter(breaks, origin.CoreStart);
        string? preceding = index > 0 ? At(text, breaks[index - 1]) : null;
        return LineEnding(text, origin.CoreStart) ?? preceding ?? Dominant(text);
    }

    internal static string Dominant(string text) => LayoutBreakIndexes(text).Select(index => At(text, index)!)
        .GroupBy(newline => newline).OrderByDescending(group => group.Count()).Select(group => group.Key)
        .FirstOrDefault() ?? "\n";

    static int FirstAtOrAfter(int[] breaks, int position)
    {
        int index = Array.BinarySearch(breaks, position);
        return index < 0 ? ~index : index;
    }

    static int[] LayoutBreakIndexes(string text) => LayoutBreaks.GetValue(text, source =>
    {
        var indexes = new List<int>();
        foreach (var (start, end) in Layout(source))
            for (int i = start; i < end; i += At(source, i)?.Length ?? 1)
                if (At(source, i) != null) indexes.Add(i);
        return indexes.ToArray();
    });

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

    internal static string ConvertAll(string text, string newline) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", newline, StringComparison.Ordinal);

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
