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
    readonly List<(int Gap, CsComment Comment)> orphans;
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
            if (gap >= 0) { Flush(gap, j); AppendGap(gap); }
            if (source[j] >= 0) output.Append(render(items[j]));
            else Place(items[j], j, replaced[j]);
            started = true;
        }
        Flush(original.Length, items.Length);
        output.Append(original.Length == 0 ? text[EmptySplit()..origin.ListEnd] : GapText(original.Length));
        return output.ToString();
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

    List<(int, CsComment)> Orphans()
    {
        var kept = new HashSet<int>(source.Concat(replaced).Where(index => index >= 0));
        var result = new List<(int, CsComment)>();
        for (int k = 1; k < original.Length; k++)
        {
            if (kept.Contains(k)) continue;
            var (start, end) = Gap(k);
            result.AddRange(owner.Comments.Where(c => IsOriginal(c) && c.Origin!.CoreStart >= start &&
                c.Origin.CoreEnd <= end).Select(c => (k, c)));
        }
        return result;
    }

    void Flush(int gap, int position)
    {
        foreach (var orphan in orphans.Where(o => o.Gap <= gap).ToArray())
        {
            Place(orphan.Comment, position, -1);
            orphans.Remove(orphan);
        }
    }

    void AppendGap(int k)
    {
        if (k == 0) return;
        string gap = GapText(k);
        if (started || gap.Any(c => !char.IsWhiteSpace(c))) output.Append(gap);
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
