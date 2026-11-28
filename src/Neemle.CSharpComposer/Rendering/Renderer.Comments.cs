using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    // Items, then the container's own comments, which end the list (GENE-29 F5, AD B2: a comment before the closing
    // delimiter, or the end of the source, is the container's). Between items, the blank-line table decides (AD C3).
    void Content(CsNode owner, IEnumerable<CsNode> content, Action<CsNode> write)
    {
        // An empty opaque item writes nothing, and takes no place in the blank-line table either (GENE-29 F4: its render is
        // then a fixed point).
        var nodes = content.Where(n => n is not (CsRaw { Text: "" } or CsRawMember { Source: "" })).ToArray();
        bool statements = owner is CsBody or CsStatement or CsSwitchSection;
        for (int i = 0; i < nodes.Length; i++)
        {
            if (i > 0 && Blank(nodes[i - 1], nodes[i], statements)) Separate();
            write(nodes[i]);
        }
        // AD C3: one blank line before the end-of-file comments.
        if (nodes.Length > 0 && owner is CsDocument && owner.Comments.Any(c => c.Placement == CommentPlacement.Container)) Separate();
        Comments(owner, CommentPlacement.Container);
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
}
