using System.Text;

namespace Neemle.CSharpComposer.Rendering;

// GENE-29 F2 (AD 84939297 v2 C2): the layout IR below the line level. The writers below the line writer still build
// strings; the IR travels in those strings as marker characters, so a writer that does not break anything is
// unchanged, and the line writer prints each line through Print, which decides every group at its real column.
// - Group: printed flat when it fits in the line width and holds no hard line, else broken.
// - Indent: one more indent unit for the line breaks inside it, when its innermost group is broken (or it is at the
//   top of the line).
// - Soft: a space when its group is flat, a line break when it is broken; SoftEmpty: nothing, or a line break.
// - Hard: always a line break (a line comment ends its line), and every group around it breaks.
// - Nested: a line break that does not break its group: a line of a block body written inside an expression (a block
//   lambda keeps its brace layout). Deeper .. Shallower: one more indent unit for the line breaks inside, in any group
//   (the depth of a nested body's line inside the body).
// - Margin: a line break inside a comment (GENE-29 F5, AD B4): the next line of the comment starts at the indentation of
//   the line the comment is on, and keeps the spaces it starts with; it breaks no group.
// Text that is never reformatted (literals, comments, opaque text) is written through Verbatim, which escapes any
// marker character it holds, so no source text can be read as layout. Line breaks in such text are written as they
// are. SyntaxTriviaWriter handles (\u0001 .. \u0002) pass through with no width.
internal static class Layout
{
    internal const char Escape = '\u0010', GroupOpen = '\u0011', GroupClose = '\u0012', IndentOpen = '\u0013', IndentClose = '\u0014',
        Soft = '\u0015', SoftEmpty = '\u0016', Hard = '\u0017', Nested = '\u0018', Deeper = '\u0019', Shallower = '\u001A', Margin = '\u001B';

    const char HandleOpen = '\u0001', HandleClose = '\u0002';

    // A forced line break that continues one indent unit further (a line comment inside an expression).
    internal static readonly string Continuation = new([IndentOpen, Hard, IndentClose]);

    static bool IsMarker(char c) => c is >= Escape and <= Margin;

    internal static string Verbatim(string text)
    {
        if (text.AsSpan().IndexOfAnyInRange(Escape, Margin) < 0) return text;
        var output = new StringBuilder(text.Length + 4);
        foreach (char c in text)
        {
            if (IsMarker(c)) output.Append(Escape);
            output.Append(c);
        }
        return output.ToString();
    }

    internal static string Group(string text) => GroupOpen + text + GroupClose;

    internal static string Indent(string text) => IndentOpen + text + IndentClose;

    // A list that breaks one item per line: `open`, the items at one more indent unit with the separator at each line
    // end, and `close` right after the last item.
    // A trailing line comment on the last item puts `close` on a line of its own.
    internal static string List(string open, IEnumerable<Item> items, string close)
    {
        string joined = Join(items, out bool comment);
        return joined.Length == 0 ? open + close : open + Group(Indent(SoftEmpty + joined) + (comment ? SoftEmpty.ToString() : "") + close);
    }

    static string Join(IEnumerable<Item> items) => Join(items, out _);

    static string Join(IEnumerable<Item> items, out bool comment)
    {
        var output = new StringBuilder();
        Item? previous = null;
        foreach (var item in items)
        {
            if (previous is { } last) output.Append(last.Text).Append(',').Append(last.Trailing).Append(item.Led ? Hard : Soft);
            previous = item;
        }
        comment = previous is { Trailing.Length: > 0 };
        if (previous is { } final) output.Append(final.Text).Append(final.Trailing);
        return output.ToString();
    }

    // A braced list (an initializer, a switch expression's arms, a collection expression): flat on one line, or broken
    // with its delimiters on lines of their own and one item per line between them. `lead` (Soft after `new T`, SoftEmpty
    // before `[`, or none) comes before `open` in the group, so the break before a broken list's opening delimiter is the
    // group's. `tail`: the list's own comments, each on a line of its own after the items; when the list holds no item and
    // only block comments, they stay inline.
    internal static string Braced(string lead, char open, IEnumerable<Item> items, IEnumerable<string> tail, char close, bool padded)
    {
        string joined = Join(items), space = padded ? " " : "";
        var comments = tail.ToArray();
        if (joined.Length == 0 && comments.All(c => !c.StartsWith("//", StringComparison.Ordinal)))
            return (lead == Soft.ToString() ? " " : "") + open + space + string.Join(" ", comments) + (comments.Length == 0 ? "" : space) + close;
        char inner = padded ? Soft : SoftEmpty;
        return Group(lead + open + Indent(inner + joined + string.Concat(comments.Select(c => Hard + c))) + inner + close);
    }

    // The text with its layout written on one line: soft breaks become spaces or nothing, hard breaks and nested lines
    // line breaks (for a caller that needs plain text: a diagnostic key, a cref, a hole patched into source).
    internal static string Flat(string text)
    {
        if (text.AsSpan().IndexOfAnyInRange(Escape, Margin) < 0) return text;
        var output = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case Escape: output.Append(text[++i]); break;
                case Soft: output.Append(' '); break;
                case Hard or Nested or Margin: output.Append('\n'); break;
                case GroupOpen or GroupClose or IndentOpen or IndentClose or SoftEmpty or Deeper or Shallower: break;
                default: output.Append(c); break;
            }
        }
        return output.ToString();
    }

    // The text without its markers, and for each of its characters the index it has in `text` (one more entry: the
    // end), so a scan of the plain text maps back.
    internal static string Plain(string text, out int[] map)
    {
        var output = new StringBuilder(text.Length);
        var indexes = new List<int>(text.Length + 1);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == Escape) i++;
            else if (c == Margin) { output.Append('\n'); indexes.Add(i); continue; }
            else if (IsMarker(c)) continue;
            output.Append(text[i]);
            indexes.Add(i);
        }
        indexes.Add(text.Length);
        map = indexes.ToArray();
        return output.ToString();
    }

    // Prints `text` starting at `column` on a line indented by `indent`; a line break inside it continues at `indent`
    // plus one `unit` per open Indent of a broken group. `width` <= 0 disables width breaking.
    internal static string Print(string text, string indent, string unit, int width, int column)
    {
        if (text.AsSpan().IndexOfAnyInRange(Escape, Margin) < 0) return text;
        return new Printer(text, indent, unit, width).Run(column);
    }

    sealed class Printer
    {
        readonly string text, indent, unit;
        readonly int width;
        readonly int[] flat, hard, stop, brk, match;
        readonly StringBuilder output = new();
        readonly Stack<bool> groups = new(), indents = new();
        int level, column, pendingLevel, space = -1;
        bool pending;

        internal Printer(string text, string indent, string unit, int width)
        {
            this.text = text; this.indent = indent; this.unit = unit; this.width = width;
            int n = text.Length;
            flat = new int[n + 1]; hard = new int[n + 1]; stop = new int[n + 2]; brk = new int[n + 2]; match = new int[n];
            // Forward: widths (a handle has none), hard breaks, group ends and the escaped (literal) characters.
            var literal = new bool[n];
            var open = new Stack<int>();
            bool handle = false;
            for (int i = 0; i < n; i++)
            {
                char c = text[i];
                int w = 0, h = 0;
                if (handle) handle = c != HandleClose;
                else if (c == HandleOpen) handle = true;
                else if (literal[i] || !IsMarker(c) || c == Soft) w = 1;
                else if (c == Escape) literal[i + 1] = true;
                else if (c == GroupOpen) open.Push(i);
                else if (c == GroupClose) match[open.Pop()] = i;
                else if (c == Hard) h = 1;
                flat[i + 1] = flat[i] + w;
                hard[i + 1] = hard[i] + h;
            }
            // Backward: the next line break (`stop`) and the next place a line may break (`brk`).
            stop[n] = stop[n + 1] = brk[n] = brk[n + 1] = n;
            for (int i = n - 1; i >= 0; i--)
            {
                char c = text[i];
                bool newline = !literal[i] && c is '\r' or '\n' or Hard or Nested or Margin;
                stop[i] = newline ? i : stop[i + 1];
                brk[i] = newline || !literal[i] && c is Soft or SoftEmpty ? i : brk[i + 1];
            }
        }

        internal string Run(int start)
        {
            column = start;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case Escape: Emit(text[++i]); break;
                    case GroupOpen:
                        bool parent = groups.Count == 0 || groups.Peek();
                        groups.Push(parent && (hard[match[i]] > hard[i] || !Fits(i)));
                        break;
                    case GroupClose: groups.Pop(); break;
                    case IndentOpen:
                        bool counts = groups.Count == 0 || groups.Peek();
                        indents.Push(counts);
                        if (counts) level++;
                        break;
                    case IndentClose: if (indents.Pop()) level--; break;
                    // A nested line's depth follows its line break, and applies to it.
                    case Deeper: level++; pendingLevel = level; break;
                    case Shallower: level--; break;
                    case Soft or SoftEmpty:
                        if (groups.Count == 0 || groups.Peek()) Break();
                        else if (c == Soft) { Emit(' '); space = output.Length; }
                        break;
                    case Hard or Nested: Break(); break;
                    case Margin:
                        Flush();
                        output.Append('\n');
                        column = 0;
                        // An empty line of the comment stays empty (a comment's text never ends with a line break here).
                        if (text[i + 1] == Margin) break;
                        output.Append(indent);
                        for (int k = 0; k < level; k++) output.Append(unit);
                        column = indent.Length + unit.Length * level;
                        break;
                    case '\r' or '\n':
                        // A line break in the text (a directive inserted into a list) ends the line: a soft space
                        // right before it goes.
                        if (space == output.Length) output.Length--;
                        pending = false;
                        output.Append(c);
                        column = 0;
                        break;
                    case HandleOpen:
                        int end = text.IndexOf(HandleClose, i);
                        Flush();
                        output.Append(text, i, end + 1 - i);
                        i = end;
                        break;
                    default: Emit(c); break;
                }
            }
            return output.ToString();
        }

        bool Fits(int open)
        {
            if (width <= 0) return true;
            int close = match[open], at = pending ? indent.Length + unit.Length * pendingLevel : column;
            int end = stop[open] < close ? stop[open] : brk[close + 1];
            return at + flat[end] - flat[open] <= width;
        }

        // A line break, written when text follows: consecutive breaks make one, at the indentation of the last.
        void Break()
        {
            pending = true;
            pendingLevel = level;
            while (output.Length > 0 && output[^1] is ' ' or '\t' && column > 0) { output.Length--; column--; }
        }

        void Flush()
        {
            if (!pending) return;
            pending = false;
            output.Append('\n').Append(indent);
            for (int i = 0; i < pendingLevel; i++) output.Append(unit);
            column = indent.Length + unit.Length * pendingLevel;
        }

        // Text after a line break starts at the indentation: a space the writer put before it (` + b` after a line
        // comment) goes.
        void Emit(char c)
        {
            if (pending && c == ' ') return;
            Flush();
            output.Append(c);
            column++;
        }
    }
}
