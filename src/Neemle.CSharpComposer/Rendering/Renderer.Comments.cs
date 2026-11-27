using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    // Items with the container's own comments. A parsed comment stays before the first item that followed it in source
    // and is still in the list, wherever that item now is, or ends the list when none is (GENE-27 D4); a comment built
    // without a source position ends the list (GENE-29 F2, AD C3). A blank line separates comments from the item after
    // them, so that they read back as the container's own, not as that item's (GENE-27 R3). Between items, the blank-line
    // table decides (AD C3).
    void Content(CsNode owner, IEnumerable<CsNode> content, Action<CsNode> write)
    {
        // An empty opaque item writes nothing, and takes no place in the blank-line table either (GENE-29 F4: its render is
        // then a fixed point).
        var nodes = content.Where(n => n is not (CsRaw { Text: "" } or CsRawMember { Source: "" })).ToArray();
        var original = PayloadList(owner.Origin?.Original ?? owner).ToArray();
        var sources = owner.Origin == null ? Enumerable.Range(0, nodes.Length).ToArray() :
            nodes.Select(n => n.Origin == null ? -1 : Array.FindIndex(original, o => ReferenceEquals(o.Origin, n.Origin))).ToArray();
        var comments = owner.Comments.Where(c => c.Placement == CommentPlacement.Container)
            .Select(c => (Comment: c, Index: c.Origin == null ? nodes.Length : Position(sources, original.Count(n =>
                n.Origin != null && n.Origin.CoreStart < c.Origin.CoreStart)))).ToArray();
        bool statements = owner is CsBody or CsStatement or CsSwitchSection;
        for (int i = 0; i <= nodes.Length; i++)
        {
            var group = comments.Where(c => c.Index == i).ToArray();
            if (i > 0 && i < nodes.Length && group.Length == 0 && Blank(nodes[i - 1], nodes[i], statements)) Separate();
            // AD C3: one blank line before the end-of-file comments.
            if (i > 0 && i == nodes.Length && group.Length != 0 && owner is CsDocument) Separate();
            foreach (var comment in group) CommentNode(comment.Comment);
            if (i == nodes.Length) break;
            if (group.Length != 0) Separate();
            write(nodes[i]);
        }
    }

    // GENE-29 F2 (AD C3): whether a blank line separates two neighbouring items. Statement lists hold none; a run of
    // directives and inactive text has one before and after it; neighbouring usings, extern aliases, fields, enum members,
    // attributes or top-level statements have none; any other two items have one.
    static bool Blank(CsNode previous, CsNode next, bool statements)
    {
        if (statements) return false;
        bool before = previous is CsDirective or CsDisabledText, after = next is CsDirective or CsDisabledText;
        if (before || after) return before != after;
        int run = Run(previous);
        return run == 0 || run != Run(next);
    }

    static int Run(CsNode node) => node switch
    {
        CsUsing => 1,
        CsExternAlias => 2,
        CsField => 3,
        CsEnumMember => 4,
        CsAttribute => 5,
        CsGlobalStatement => 6,
        _ => 0
    };

    // An empty line, unless the output is empty or already ends in one.
    void Separate()
    {
        if (output.Length != 0 && !output.ToString(Math.Max(0, output.Length - 2), Math.Min(2, output.Length)).Equals("\n\n", StringComparison.Ordinal)) Line();
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
