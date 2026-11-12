using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    void Switch(CsStatement statement)
    {
        Line("switch (" + Names.Expression(statement.Expression!) + ")");
        Open();
        Content(statement, statement.Sections, node => SwitchSection((CsSwitchSection)node));
        Close();
    }

    void SwitchSection(CsSwitchSection section) => WithSyntaxTrivia(section, () => SwitchSectionCore(section));

    void SwitchSectionCore(CsSwitchSection section)
    {
        Comments(section, CommentPlacement.Leading);
        Content(section, section.Labels, node => SwitchLabel((CsSwitchLabel)node));
        depth++;
        // Keep the section's statement list in the shared switch scope. A caller can
        // explicitly use Cs.Block to introduce a separate local-variable scope.
        SwitchBody(section.Body);
        depth--;
        Comments(section, CommentPlacement.Trailing);
    }

    void SwitchBody(CsBody body) => WithSyntaxTrivia(body, () => SwitchBodyCore(body));

    void SwitchBodyCore(CsBody body)
    {
        if (body.Tree != null) { SwitchBody(SymbolModel.ToBody(body.Tree)); return; }
        Comments(body, CommentPlacement.Leading);
        if (body.Raw != null) Raw(body.Raw);
        else Content(body, body.Statements, node => Statement((CsStatement)node));
        Comments(body, CommentPlacement.Trailing);
    }

    void SwitchLabel(CsSwitchLabel label) => WithSyntaxTrivia(label, () => SwitchLabelCore(label));

    void SwitchLabelCore(CsSwitchLabel label)
    {
        Comments(label, CommentPlacement.Leading);
        Comments(label, CommentPlacement.Container);
        Line(label.Pattern == null ? "default:" : "case " + PatternWriter.Write(label.Pattern) +
            (label.When == null ? "" : " when " + Names.Expression(label.When)) + ":");
        Comments(label, CommentPlacement.Trailing);
    }
}
