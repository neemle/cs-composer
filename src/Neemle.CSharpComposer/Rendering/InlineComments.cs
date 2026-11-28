using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class InlineComments
{
    // A comment written inside a line: a block comment is followed by a space; a line comment ends its line, and the
    // text after it continues one indent unit further (GENE-29 F2, AD C3).
    internal static string Text(CsComment comment)
    {
        string text = DocumentationWriter.Comment(comment);
        return text + (Line(text) ? Layout.Continuation : " ");
    }

    static bool Line(string text) => text.StartsWith("//", StringComparison.Ordinal);

    internal static string Apply(CsNode node, string text) =>
        SyntaxTriviaWriter.Apply(node, string.Concat(node.Comments.Where(c => c.Placement != CommentPlacement.Trailing).Select(Text)) + text +
        string.Concat(node.Comments.Where(c => c.Placement == CommentPlacement.Trailing).Select(Trailing)));

    // A trailing comment follows its node after one space; a block comment needs none after it, since the writer separates
    // what follows (GENE-29 F4: no double space before an operator).
    static string Trailing(CsComment comment)
    {
        string text = DocumentationWriter.Comment(comment);
        return " " + text + (Line(text) ? Layout.Continuation : "");
    }

    // GENE-29 F2 (AD C3): an item of a breakable list. Its leading comments go on lines of their own when the list
    // breaks (a line comment always breaks it), and inline before the item when it does not; its trailing comments follow
    // its separator (F5, AD C3), block comments first. `write` writes the item without those comments.
    internal static Item Entry<T>(T node, Func<T, string> write) where T : CsNode
    {
        if (node.Comments.All(c => c.Placement == CommentPlacement.Container)) return new(write(node), "", false);
        var bare = (T)((CsNode)node with { Comments = new(node.Comments.Where(c => c.Placement == CommentPlacement.Container)) });
        string lead = "", block = "", line = "";
        foreach (var comment in node.Comments)
        {
            string text = DocumentationWriter.Comment(comment);
            if (comment.Placement == CommentPlacement.Leading) lead += text + (Line(text) ? Layout.Hard : Layout.Soft);
            else if (comment.Placement != CommentPlacement.Trailing) continue;
            else if (Line(text)) line += " " + text + Layout.Hard;
            else block += " " + text;
        }
        return new(lead + write(bare), block + line, lead.Length != 0);
    }
}

// One item of a breakable list: its text, what follows its separator (its trailing comments), and whether it starts with
// leading comments: after another item those would read back on that item's line, as its trailing comments (AD B2), so
// such an item starts a line of its own (F5).
internal readonly record struct Item(string Text, string Trailing, bool Led);
