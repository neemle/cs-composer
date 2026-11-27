using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    // Items with the container's own comments before them. A parsed comment stays before the first item that
    // followed it in source and is still in the list, wherever that item now is, or ends the list when none is
    // (GENE-27 D4); a comment built without a source position starts the list.
    void Content(CsNode owner, IEnumerable<CsNode> content, Action<CsNode> write)
    {
        var nodes = content.ToArray();
        var original = PayloadList(owner.Origin?.Original ?? owner).ToArray();
        var sources = owner.Origin == null ? Enumerable.Range(0, nodes.Length).ToArray() :
            nodes.Select(n => n.Origin == null ? -1 : Array.FindIndex(original, o => ReferenceEquals(o.Origin, n.Origin))).ToArray();
        var comments = owner.Comments.Where(c => c.Placement == CommentPlacement.Container)
            .Select(c => (Comment: c, Index: c.Origin == null ? 0 : Position(sources, original.Count(n =>
                n.Origin != null && n.Origin.CoreStart < c.Origin.CoreStart)))).ToArray();
        for (int i = 0; i <= nodes.Length; i++)
        {
            foreach (var comment in comments.Where(c => c.Index == i)) Opaque(DocumentationWriter.Comment(comment.Comment));
            if (i < nodes.Length) write(nodes[i]);
        }
    }

    // The position of the item with the lowest source index that is at least `index`, or the end of the list.
    static int Position(int[] sources, int index)
    {
        int position = sources.Length;
        for (int i = 0; i < sources.Length; i++)
            if (sources[i] >= index && (position == sources.Length || sources[i] < sources[position])) position = i;
        return position;
    }

    static IEnumerable<CsNode> PayloadList(CsNode node) => node switch
    {
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Members,
        CsExtension e => e.Members,
        CsStatement s => s.Sections,
        CsSwitchSection s => s.Labels,
        // Content owners are documents, namespaces, type declarations, extensions, switch statements,
        // switch sections and bodies; an origin's original is a node of the owner's own type.
        _ => ((CsBody)node).Statements
    };
}
