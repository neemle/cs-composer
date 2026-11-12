using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class InlineComments
{
    internal static string Text(CsComment comment) => comment.Text +
        (comment.Text.StartsWith("//", StringComparison.Ordinal) ? "\n" : " ");

    internal static string Apply(CsNode node, string text) =>
        string.Concat(node.Comments.Where(c => c.Placement != CommentPlacement.Trailing).Select(Text)) + text +
        string.Concat(node.Comments.Where(c => c.Placement == CommentPlacement.Trailing).Select(c => " " + Text(c)));
}
