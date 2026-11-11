using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Rendering;
internal sealed partial class Renderer
{
    internal static string Node(CsNode node, RenderOptions options, CsLanguageVersion version, int indent = 0)
    {
        var writer = new Renderer(options) { depth = indent, languageVersion = version };
        writer.WriteNode(node);
        return writer.output.ToString();
    }
    void WriteNode(CsNode node)
    {
        switch (node)
        {
            case CsDocument document: Document(document); break;
            case CsMember member: Member(member); break;
            case CsNamespace space: NamespaceNode(space); break;
            case CsRaw raw: Opaque(raw.Text); break;
            case CsBody body: Body(body); break;
            case CsStatement statement: Statement(statement); break;
            case CsExpression expression: output.Append(Names.Expression(expression)); break;
            case CsType type: output.Append(Names.Type(type)); break;
            case CsParameter parameter: output.Append(Names.Parameter(parameter)); break;
            case CsAttribute attribute: output.Append(Names.Attribute(attribute)); break;
            case CsAccessor accessor: Accessor(accessor); break;
            case CsUsing use: Line(Using(use)); break;
            case CsComment comment: Opaque(comment.Text); break;
            case TypeParameter parameter: output.Append(parameter.Name); break;
            case CsConstraint constraint: output.Append(Constraint(constraint)); break;
            case TupleElement element: output.Append(Names.Type(element.Type) + (element.Name == null ? "" : " " + element.Name)); break;
            case AttributeArgument argument: output.Append(Names.Expression(argument.Value)); break;
            case CsCatch catcher: Line("catch"); Body(catcher.Body); break;
            default: throw new ArgumentException("Unsupported node kind.");
        }
    }
    void NamespaceNode(CsNamespace space)
    {
        Comments(space, CommentPlacement.Leading);
        Line("namespace " + Names.Qualified(space.Name) + (space.IsFileScoped ? ";" : ""));
        if (!space.IsFileScoped) Open();
        Content(space, space.Items, WriteNode);
        if (!space.IsFileScoped) Close();
        Comments(space, CommentPlacement.Trailing);
    }
    void Comments(CsNode node, CommentPlacement placement)
    {
        foreach (var comment in node.Comments.Where(c => c.Placement == placement))
        {
            if (placement != CommentPlacement.Trailing) { Opaque(comment.Text); continue; }
            if (output.Length > 0 && output[^1] == '\n') output.Length--;
            output.Append(' ').Append(comment.Text);
            if (comment.Text.Length == 0 || comment.Text[^1] is not ('\r' or '\n')) output.Append('\n');
        }
    }
    void Opaque(string text)
    {
        if (text.Length == 0) return;
        Indent();
        output.Append(text);
        if (text[^1] is not ('\n' or '\r')) output.Append('\n');
    }
}
