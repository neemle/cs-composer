namespace Neemle.CSharpComposer.Model;
internal static class NodeItems
{
    internal static Values<CsNode> Replace<T>(Values<CsNode> items, IEnumerable<T> values) where T : CsNode
    {
        var replacement = values.Cast<CsNode>().ToArray();
        int first = items.ToList().FindIndex(n => n is T);
        if (first < 0) return new(items.Concat(replacement));
        var result = items.Where(n => n is not T).ToList();
        result.InsertRange(Math.Min(first, result.Count), replacement);
        return new(result);
    }
}
