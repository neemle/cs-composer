using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer.Rendering;

internal static class SyntaxTriviaWriter
{
    // Graph validation, which every render runs, admits only a directive or inactive text as trivia content.
    internal static string Content(CsNode node) => node is CsDirective directive
        ? InlineComments.Apply(directive with { SyntaxTrivia = Values<CsSyntaxTrivia>.Empty }, DirectiveWriter.Write(directive))
        : ((CsDisabledText)node).Text;

    // `inline` text is written inside a line its writer began; other text (a declaration, a statement) is whole lines.
    internal static string Apply(CsNode owner, string text, bool inline = true)
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
        int start = -1;
        foreach (var group in insertions.OrderBy(x => x.Position).ThenBy(x => x.Order).GroupBy(x => x.Position))
        {
            result.Append(text[previous..group.Key]);
            int lineStart = group.Key;
            while (lineStart > 0 && text[lineStart - 1] is not ('\r' or '\n')) lineStart--;
            string indentation = text[lineStart..group.Key];
            // GENE-27 D2: a directive inserted where the line already holds code (or where inline text starts, inside
            // a line its owner's writer began) is followed by a continuation line, indented one level past the line
            // the owner's text starts on: whole lines start with their own indentation, and inline text is measured
            // in the finished output.
            if (indentation.Any(c => c is not (' ' or '\t')) || inline && indentation.Length == 0 && lineStart == 0)
                indentation = !inline ? text[..text.TakeWhile(c => c is ' ' or '\t').Count()] + current.Value!.Unit :
                    current.Value?.Continuation(ref start) ?? "";
            while (result.Length > 0 && result[^1] is ' ' or '\t' or Layout.Soft or Layout.SoftEmpty) result.Length--;
            // Whole lines start a line already: a directive before their first token needs no line break (AD C3: no
            // blank line after `{`).
            if ((inline || result.Length != 0) && !EndsLine(result)) result.Append('\n');
            foreach (var insertion in group)
            {
                result.Append(insertion.Text);
                if (!EndsLine(result)) result.Append('\n');
            }
            result.Append(indentation);
            previous = group.Key;
        }
        result.Append(text[previous..]);
        return start < 0 ? result.ToString() : current.Value!.Anchor(start) + result.ToString();
    }

    static bool EndsLine(StringBuilder text) => text.Length > 0 && (text[^1] is '\r' or '\n' || current.Value?.EndsLine(text.ToString()) == true);

    // The tokens of rendered text, read without its layout markers (GENE-29 F2), at their places in `text`.
    static Token[] ActiveTokens(string text)
    {
        string plain = Layout.Plain(text, out int[] map);
        return Lexer.Scan(current.Value?.Mask(plain) ?? plain)
            .Where(t => t.Text != "" && !t.IsComment && !t.Text.StartsWith('#'))
            .Select(t => t with { Start = map[t.Start], End = map[t.End] }).ToArray();
    }

    static readonly AsyncLocal<TriviaContext?> current = new();

    internal static RenderScope Begin(string unit) => new(new TriviaContext(unit));

    internal sealed class RenderScope : IDisposable
    {
        readonly TriviaContext? previous = current.Value;
        readonly TriviaContext context;
        internal RenderScope(TriviaContext context) { this.context = context; current.Value = context; }
        internal string Complete(string text) => context.Expand(text);
        public void Dispose() { current.Value = previous; }
    }

    // Trivia is opaque while an enclosing semantic owner aligns its active
    // syntax. Scoped handles prevent inactive literals or directives from being
    // mistaken for executable tokens. No handle reaches the public output.
    internal static string Protect(CsNode node, bool includeComments = true)
    {
        string text = Layout.Flat(!includeComments && node is CsDirective directive ? DirectiveWriter.Write(directive) : Content(node));
        return current.Value?.Protect(text, node.SyntaxPosition) ?? text;
    }

    internal sealed class TriviaContext(string unit)
    {
        readonly string prefix = "\u0001" + Guid.NewGuid().ToString("N") + ":";
        internal string Unit => unit;
        // A value is protected text (Anchor -1), the start of an owner's text (Anchor -2), or the indentation of a
        // continuation line after a directive, resolved against the owner start it names (Anchor >= 0).
        readonly List<(string Text, SyntaxPosition? Position, int Anchor)> values = new();
        internal string Protect(string text, SyntaxPosition? position) => Handle(text, position, -1);

        string Handle(string text, SyntaxPosition? position, int anchor)
        {
            int id = values.Count; values.Add((text, position, anchor));
            return prefix + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0002";
        }

        // The handle of a continuation line's indentation; the owner start it is measured from is reserved on first use.
        internal string Continuation(ref int anchor)
        {
            if (anchor < 0) { anchor = values.Count; values.Add(("", null, -2)); }
            return Handle("", null, anchor);
        }

        internal string Anchor(int anchor) => prefix + anchor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0002";

        // Handles are only ever inspected whole: rendered text is cut at token starts of masked text, at handle
        // starts or at its own ends, so every prefix is followed by its terminator.
        IEnumerable<(int Start, int End, int Id)> Handles(string text)
        {
            int position = 0;
            while ((position = text.IndexOf(prefix, position, StringComparison.Ordinal)) >= 0)
            {
                int end = text.IndexOf('\u0002', position + prefix.Length);
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
            // The caller passes non-empty text, so a handle that ends it is a real one, and protected text is never
            // null: graph validation rejects a null inactive text before any render.
            return last.End == text.Length && values[last.Id].Text.Length > 0 && values[last.Id].Text[^1] is '\r' or '\n';
        }

        internal string Expand(string text)
        {
            var output = new StringBuilder(); int position = 0;
            var starts = new Dictionary<int, int>();
            foreach (var handle in Handles(text))
            {
                output.Append(text[position..handle.Start]);
                var value = values[handle.Id];
                if (value.Anchor == -2) starts[handle.Id] = output.Length;
                else if (value.Anchor >= 0) output.Append(LineIndentation(output, starts[value.Anchor])).Append(unit);
                else output.Append(value.Text);
                position = handle.End;
            }
            return output.Append(text[position..]).ToString();
        }

        // The indentation of the finished line holding `position`.
        static string LineIndentation(StringBuilder output, int position)
        {
            int start = position, end;
            while (start > 0 && output[start - 1] is not ('\r' or '\n')) start--;
            for (end = start; end < output.Length && output[end] is ' ' or '\t'; end++) { }
            return output.ToString(start, end - start);
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
        // A boundary right before replaced tokens stays before what replaced them: trivia between the unchanged text and
        // a replaced node (an inactive branch that ends a call) never moves into the replacement (GENE-27 O2d).
        if (boundary == prefix) return prefix;
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
