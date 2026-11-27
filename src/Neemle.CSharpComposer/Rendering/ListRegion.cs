using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
namespace Neemle.CSharpComposer.Rendering;

internal sealed class ListRegion
{
    readonly CsNode owner;
    readonly SourceOrigin origin;
    readonly LayoutScope scope;
    readonly string text;
    readonly CsNode[] items;
    readonly CsNode[] original;
    readonly int[] source;
    readonly int[] replaced;
    readonly Func<CsNode, string> render;
    readonly string fallbackUnit;
    readonly List<int> orphans;
    readonly StringBuilder output = new();
    bool started;

    internal ListRegion(CsNode owner, CsNode[] items, LayoutScope scope, Func<CsNode, string> render,
        string fallbackUnit)
    {
        this.owner = owner;
        this.items = items;
        this.scope = scope;
        this.render = render;
        this.fallbackUnit = fallbackUnit;
        origin = scope.Origin;
        text = origin.Snapshot.Text;
        original = SourceLayout.List(origin.Original).ToArray();
        source = items.Select(SourceIndex).ToArray();
        replaced = Replacements();
        orphans = Orphans();
    }

    internal string Text()
    {
        output.Append(original.Length == 0 ? text[origin.ListStart..EmptySplit()] : GapText(0));
        foreach (var comment in owner.Comments)
            if (comment.Placement == CommentPlacement.Container && !IsOriginal(comment)) Place(comment, 0, -1);
        for (int j = 0; j < items.Length; j++)
        {
            int gap = source[j] >= 0 ? source[j] : replaced[j];
            if (gap >= 0) { Flush(gap, GapText(gap)); AppendGap(gap); }
            if (source[j] >= 0) output.Append(Separated(items[j], render(items[j]), j));
            else Place(items[j], j, replaced[j]);
            started = true;
        }
        string close = original.Length == 0 ? text[EmptySplit()..origin.ListEnd] : GapText(original.Length);
        Flush(original.Length, close, end: true);
        output.Append(close);
        return output.ToString();
    }

    // GENE-29 F2: a parsed enum member's text runs through its comma, and the last member's may have none; followed by
    // another member after an edit, it gets one, before the text after its core (a trailing comment, the line end).
    string Separated(CsNode item, string rendered, int j)
    {
        if (item is not CsEnumMember || j == items.Length - 1) return rendered;
        // A member written fresh (not from its source text) already ends in its comma.
        string after = text[item.Origin!.CoreEnd..item.Origin.End];
        if (!rendered.EndsWith(after, StringComparison.Ordinal)) return rendered;
        int end = rendered.Length - after.Length;
        return rendered[..end].TrimEnd().EndsWith(',') ? rendered : rendered.Insert(end, ",");
    }

    int SourceIndex(CsNode item)
    {
        return item.Origin == null ? -1 : Array.FindIndex(original, old => ReferenceEquals(old.Origin, item.Origin));
    }

    int[] Replacements()
    {
        var used = new HashSet<int>(source.Where(index => index >= 0));
        var result = new int[items.Length];
        int previous = -1;
        for (int j = 0; j < items.Length; j++)
        {
            result[j] = -1;
            if (source[j] >= 0) { previous = source[j]; continue; }
            int next = source.Skip(j).FirstOrDefault(index => index >= 0, original.Length);
            int candidate = Enumerable.Range(previous + 1, Math.Max(0, next - previous - 1))
                .FirstOrDefault(index => !used.Contains(index), -1);
            if (candidate < 0) continue;
            result[j] = previous = candidate;
            used.Add(candidate);
        }
        return result;
    }

    // The gaps before removed items that hold comments of the container (GENE-27 D4). Such a gap keeps its text: the
    // comments, the lines between them and the blank lines before them; the blank lines after them separated the
    // removed item, so the next gap's own leading lines separate what follows instead.
    List<int> Orphans()
    {
        var kept = new HashSet<int>(source.Concat(replaced).Where(index => index >= 0));
        var result = new List<int>();
        for (int k = 1; k < original.Length; k++)
        {
            if (kept.Contains(k)) continue;
            var (start, end) = Gap(k);
            if (owner.Comments.Any(c => IsOriginal(c) && c.Origin!.CoreStart >= start && c.Origin.CoreEnd <= end)) result.Add(k);
        }
        return result;
    }

    // The comment gaps of removed items up to `gap`, before `following`: the text that comes next, or with `end` the
    // text that closes the list. A gap's trailing blank lines stay only when what follows starts at once, so that a
    // comment never ends up directly above an item (where it would read back as that item's own).
    void Flush(int gap, string following, bool end = false)
    {
        var texts = orphans.Where(k => k <= gap).Select(GapText).Where(t => t.Any(c => !char.IsWhiteSpace(c))).ToList();
        orphans.RemoveAll(k => k <= gap);
        for (int i = 0; i < texts.Count; i++)
        {
            string next = i + 1 < texts.Count ? texts[i + 1] : following;
            string text = BlankFirstLine(next) || end && next.All(char.IsWhiteSpace) ? TrailingBlankLines(texts[i]) : texts[i];
            output.Append(started ? text : LeadingBlankLines(text));
            started = true;
        }
    }

    static bool BlankFirstLine(string text) =>
        SourceNewline.NextBreak(text, 0) is var lineEnd && lineEnd >= 0 && text[..lineEnd].All(char.IsWhiteSpace);

    void AppendGap(int k)
    {
        if (k == 0) return;
        string gap = GapText(k);
        // A gap that opens the list separated a removed item before it: its leading blank lines go with that item.
        if (started) output.Append(gap);
        else if (gap.Any(c => !char.IsWhiteSpace(c))) output.Append(LeadingBlankLines(gap));
    }

    // `gap` without its leading whitespace-only lines.
    static string LeadingBlankLines(string gap)
    {
        int start = 0;
        for (int end; (end = SourceNewline.NextBreak(gap, start)) >= 0 && gap[start..end].All(char.IsWhiteSpace);)
            start = end + SourceNewline.At(gap, end)!.Length;
        return gap[start..];
    }

    // `gap` up to the line break after its last non-whitespace text (a container comment ends its line: one on the
    // line of an item is that item's own).
    static string TrailingBlankLines(string gap)
    {
        string kept = gap.TrimEnd();
        return kept + SourceNewline.LineEnding(gap, kept.Length)!;
    }

    void Place(CsNode node, int position, int replacedIndex)
    {
        string newline = Newline(position, replacedIndex);
        string rendered = render(node);
        if (node.Origin == null) rendered = SourceNewline.Convert(rendered, newline);
        bool lineOpen = output.Length != 0 ? output[^1] is not ('\r' or '\n') : origin.ListStart > 0;
        if (lineOpen) output.Append(newline);
        output.Append(SourceLayout.Relocate(rendered, node.Origin, Indent(position, replacedIndex), newline));
    }

    string Newline(int position, int replacedIndex)
    {
        if (replacedIndex >= 0) return SourceNewline.Destination(original[replacedIndex].Origin!);
        foreach (int i in Neighbours(position).Where(i => source[i] >= 0))
            if (SourceNewline.LineEnding(text, items[i].Origin!.CoreEnd) is { } ending) return ending;
        return SourceNewline.LineEnding(text, origin.ListStart) ?? SourceNewline.Dominant(text);
    }

    string Indent(int position, int replacedIndex)
    {
        if (replacedIndex >= 0 && SourceLayout.StartsLine(original[replacedIndex].Origin!))
            return SourceLayout.IndentText(original[replacedIndex].Origin);
        foreach (int i in Neighbours(position))
            if (source[i] >= 0 && SourceLayout.StartsLine(items[i].Origin!)) return SourceLayout.IndentText(items[i].Origin);
        if (owner is CsDocument || owner is CsNamespace { IsFileScoped: true }) return SourceLayout.LineIndent(origin);
        return SourceLayout.LineIndent(origin) + (SourceLayout.InferUnit(null, scope) ?? fallbackUnit);
    }

    IEnumerable<int> Neighbours(int position) =>
        Enumerable.Range(0, position).Reverse().Concat(Enumerable.Range(position, items.Length - position));

    (int Start, int End) Gap(int k)
    {
        int start = k == 0 ? origin.ListStart : Math.Max(origin.ListStart, original[k - 1].Origin!.End);
        int end = k == original.Length ? origin.ListEnd : original[k].Origin!.Start;
        start = Math.Min(start, origin.ListEnd);
        return (start, Math.Max(start, Math.Min(end, origin.ListEnd)));
    }

    int EmptySplit()
    {
        int split = origin.ListEnd;
        while (split > origin.ListStart && text[split - 1] is not ('\r' or '\n')) split--;
        return split == origin.ListStart ? origin.ListEnd : split;
    }

    string GapText(int k)
    {
        var (start, end) = Gap(k);
        var gap = new StringBuilder(text[start..end]);
        var comments = origin.Original.Comments
            .Where(c => c.Origin is { } o && o.CoreStart >= start && o.CoreEnd <= end);
        foreach (var comment in comments.OrderByDescending(c => c.Origin!.CoreStart))
        {
            var current = owner.Comments.FirstOrDefault(c => ReferenceEquals(c.Origin, comment.Origin));
            if (current != null && current.Equals(comment)) continue;
            var (cutStart, cutEnd) = current == null ? CommentLine(comment.Origin!, start, end) :
                (comment.Origin!.CoreStart, comment.Origin.CoreEnd);
            gap.Remove(cutStart - start, cutEnd - cutStart);
            if (current != null) gap.Insert(cutStart - start, render(current).Trim());
        }
        return gap.ToString();
    }

    (int Start, int End) CommentLine(SourceOrigin comment, int start, int end)
    {
        int before = comment.CoreStart, after = comment.CoreEnd;
        while (before > start && text[before - 1] is ' ' or '\t') before--;
        while (after < end && text[after] is ' ' or '\t') after++;
        string lineBreak = after < end ? SourceNewline.At(text, after) ?? "" : "";
        if (after < end && lineBreak.Length == 0) return (comment.CoreStart, after);
        return before == SourceLayout.LineStart(text, before) ? (before, after + lineBreak.Length) : (before, after);
    }

    bool IsOriginal(CsComment comment) =>
        comment.Origin != null && origin.Original.Comments.Any(c => ReferenceEquals(c.Origin, comment.Origin));
}
