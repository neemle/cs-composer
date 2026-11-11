using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Rendering;
internal static class PreservedRenderer
{
    internal static string Render(CsDocument document, RenderOptions options)
    {
        var errors = LanguageFeatures.RenderErrors(document, options);
        if (errors.Count != 0) throw new GenValidationException(errors);
        return document.Origin == null ? Renderer.Render(document, options) : Node(document);
    }
    static string Node(CsNode node)
    {
        var origin = node.Origin ?? throw new InvalidOperationException("New parsed insertion requires rendering.");
        if (node.Equals(origin.Original)) return origin.Snapshot.Text[origin.Start..origin.End];
        var patches = new List<(int Start, int End, string Text)>();
        string? name = node switch { CsMember m => m.Name, _ => null };
        string? old = origin.Original is CsMember member ? member.Name : null;
        if (name != old && origin.NameStart >= 0)
            patches.Add((origin.NameStart, origin.NameStart + origin.NameLength, Names.Id(name!)));
        var children = node.Children.Where(c => c.Origin != null && c is not CsComment).ToArray();
        if (origin.ListStart >= 0 && node is CsTypeDeclaration type &&
            origin.Original is CsTypeDeclaration previous && !type.Members.Select(m => m.Origin)
                .SequenceEqual(previous.Members.Select(m => m.Origin)))
            patches.Add((origin.ListStart, origin.ListEnd, string.Concat(type.Members.Select(Node))));
        else foreach (var child in children)
            patches.Add((child.Origin!.Start, child.Origin.End, Node(child)));
        string text = origin.Snapshot.Text[origin.Start..origin.End];
        foreach (var patch in patches.OrderByDescending(t => t.Start))
            text = text[..(patch.Start - origin.Start)] + patch.Text + text[(patch.End - origin.Start)..];
        return text;
    }
}
