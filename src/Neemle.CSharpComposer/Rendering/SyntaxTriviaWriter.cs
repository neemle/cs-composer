using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer.Rendering;

internal static class SyntaxTriviaWriter
{
    internal static string Content(CsNode node) => node switch
    {
        CsDirective directive => InlineComments.Apply(directive with { SyntaxTrivia = Values<CsSyntaxTrivia>.Empty }, DirectiveWriter.Write(directive)),
        CsDisabledText disabled => disabled.Text,
        _ => throw new ArgumentException("Syntax trivia requires a directive or inactive text.")
    };

    internal static string Apply(CsNode owner, string text)
    {
        if (owner.SyntaxTrivia.Count == 0) return text;
        var tokens = ActiveTokens(text);
        var insertions = new List<(int Position, int Order, string Text)>();
        for (int i = 0; i < owner.SyntaxTrivia.Count; i++)
        {
            var trivia = owner.SyntaxTrivia[i];
            int position = trivia.Placement == SyntaxTriviaPlacement.Trailing ? text.Length : 0;
            if (trivia.Anchor is { } anchor)
            {
                int boundary = Align(anchor.Tokens, tokens.Select(t => t.Text).ToArray(), anchor.Boundary);
                position = boundary < tokens.Length ? tokens[boundary].Start : text.Length;
                int beginning = boundary > 0 ? tokens[boundary - 1].End : 0;
                // Descendant trivia may occupy the same lexical gap. Preserve
                // source order when an outer conditional crosses an inner node.
                if (trivia.Content.SyntaxPosition is { } original && current.Value is { } context)
                    position = context.BeforeLaterTrivia(text, beginning, position, original);
            }
            insertions.Add((position, i, Protect(trivia.Content)));
        }
        var result = new StringBuilder(); int previous = 0;
        foreach (var group in insertions.OrderBy(x => x.Position).ThenBy(x => x.Order).GroupBy(x => x.Position))
        {
            result.Append(text[previous..group.Key]);
            int lineStart = group.Key;
            while (lineStart > 0 && text[lineStart - 1] is not ('\r' or '\n')) lineStart--;
            string indentation = text[lineStart..group.Key];
            if (indentation.Any(c => c is not (' ' or '\t'))) indentation = "";
            while (result.Length > 0 && result[^1] is ' ' or '\t') result.Length--;
            if (!EndsLine(result)) result.Append('\n');
            foreach (var insertion in group)
            {
                result.Append(insertion.Text);
                if (!EndsLine(result)) result.Append('\n');
            }
            result.Append(indentation);
            previous = group.Key;
        }
        return result.Append(text[previous..]).ToString();
    }

    static bool EndsLine(StringBuilder text) => text.Length > 0 && (text[^1] is '\r' or '\n' || current.Value?.EndsLine(text.ToString()) == true);

    static Token[] ActiveTokens(string text) => Lexer.Scan(current.Value?.Mask(text) ?? text)
        .Where(t => t.Text != "" && !t.IsComment && !t.Text.StartsWith('#')).ToArray();

    static readonly AsyncLocal<TriviaContext?> current = new();

    internal static RenderScope Begin() => new();

    internal sealed class RenderScope : IDisposable
    {
        readonly TriviaContext? previous = current.Value;
        readonly TriviaContext context = new();
        internal RenderScope() { current.Value = context; }
        internal string Complete(string text) => context.Expand(text);
        public void Dispose() { current.Value = previous; }
    }

    // Trivia is opaque while an enclosing semantic owner aligns its active
    // syntax. Scoped handles prevent inactive literals or directives from being
    // mistaken for executable tokens. No handle reaches the public output.
    internal static string Protect(CsNode node, bool includeComments = true)
    {
        string text = !includeComments && node is CsDirective directive ? DirectiveWriter.Write(directive) : Content(node);
        return current.Value?.Protect(text, node.SyntaxPosition) ?? text;
    }

    sealed class TriviaContext
    {
        readonly string prefix = "\u0001" + Guid.NewGuid().ToString("N") + ":";
        readonly List<(string Text, SyntaxPosition? Position)> values = new();
        internal string Protect(string text, SyntaxPosition? position)
        {
            int id = values.Count; values.Add((text, position));
            return prefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0002";
        }

        IEnumerable<(int Start, int End, int Id)> Handles(string text)
        {
            int position = 0;
            while ((position = text.IndexOf(prefix, position, StringComparison.Ordinal)) >= 0)
            {
                int end = text.IndexOf('\u0002', position + prefix.Length);
                if (end < 0) yield break;
                if (int.TryParse(text.AsSpan(position + prefix.Length, end - position - prefix.Length), out int id) && id >= 0 && id < values.Count)
                    yield return (position, end + 1, id);
                position = end + 1;
            }
        }

        internal string Mask(string text)
        {
            var result = text.ToCharArray();
            foreach (var handle in Handles(text)) Array.Fill(result, ' ', handle.Start, handle.End - handle.Start);
            return new string(result);
        }

        internal int BeforeLaterTrivia(string text, int start, int end, SyntaxPosition original)
        {
            foreach (var handle in Handles(text))
                if (handle.Start >= start && handle.Start < end && values[handle.Id].Position is { } position &&
                    position.Source == original.Source && position.Offset > original.Offset) end = handle.Start;
            return end;
        }

        internal bool EndsLine(string text)
        {
            var last = Handles(text).LastOrDefault();
            return last.End == text.Length && last.End > 0 && values[last.Id].Text is { Length: > 0 } value && value[^1] is '\r' or '\n';
        }

        internal string Expand(string text)
        {
            var output = new StringBuilder(); int position = 0;
            foreach (var handle in Handles(text))
            {
                output.Append(text[position..handle.Start]).Append(values[handle.Id].Text);
                position = handle.End;
            }
            return output.Append(text[position..]).ToString();
        }
    }

    static int Align(Values<string> before, string[] after, int boundary)
    {
        int prefix = 0;
        while (prefix < before.Count && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        if (boundary < prefix) return boundary;
        int suffix = 0;
        while (suffix < before.Count - prefix && suffix < after.Length - prefix && before[before.Count - suffix - 1] == after[after.Length - suffix - 1]) suffix++;
        if (boundary >= before.Count - suffix) return Math.Clamp(after.Length - (before.Count - boundary), 0, after.Length);
        int n = before.Count - prefix - suffix, m = after.Length - prefix - suffix;
        if ((long)(n + 1) * (m + 1) > 1_000_000)
        {
            // Large replacements retain the semantic owner's boundary without
            // allocating quadratic memory. Unchanged prefixes/suffixes stay exact.
            return prefix + Math.Min(boundary - prefix, m);
        }
        var cost = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) cost[i, 0] = i;
        for (int j = 0; j <= m; j++) cost[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                cost[i, j] = Math.Min(cost[i - 1, j - 1] + (before[prefix + i - 1] == after[prefix + j - 1] ? 0 : 1), Math.Min(cost[i - 1, j], cost[i, j - 1]) + 1);
        int x = n, y = m, target = boundary - prefix;
        while (x > target)
        {
            if (x > 0 && y > 0 && cost[x, y] == cost[x - 1, y - 1] + (before[prefix + x - 1] == after[prefix + y - 1] ? 0 : 1)) { x--; y--; }
            else if (y > 0 && cost[x, y] == cost[x, y - 1] + 1) y--;
            else x--;
        }
        return prefix + y;
    }
}
