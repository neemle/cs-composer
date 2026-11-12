using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Content(CsNode owner, IEnumerable<CsNode> content, Action<CsNode> write)
    {
        var nodes = content.ToArray();
        var original = PayloadList(owner.Origin?.Original ?? owner).ToArray();
        var comments = owner.Comments.Where(c => c.Placement == CommentPlacement.Container)
            .Select(c => (Comment: c, Index: c.Origin == null ? 0 : original.Count(n =>
                n.Origin != null && n.Origin.CoreStart < c.Origin.CoreStart))).ToArray();
        for (int i = 0; i <= nodes.Length; i++)
        {
            foreach (var comment in comments.Where(c => Math.Min(c.Index, nodes.Length) == i)) Opaque(DocumentationWriter.Comment(comment.Comment));
            if (i < nodes.Length) write(nodes[i]);
        }
    }

    static IEnumerable<CsNode> PayloadList(CsNode node) => node switch
    {
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Members,
        CsExtension e => e.Members,
        CsStatement s => s.Sections,
        CsSwitchSection s => s.Labels,
        CsBody b => b.Statements,
        _ => Array.Empty<CsNode>()
    };
}
