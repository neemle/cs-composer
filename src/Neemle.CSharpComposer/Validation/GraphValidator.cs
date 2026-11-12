using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Validation;

internal static class GraphValidator
{
    internal static IReadOnlyList<GenDiagnostic> Validate(CsNode root)
    {
        var errors = new List<GenDiagnostic>();
        Visit(root, "Root", 0, new HashSet<CsNode>(ReferenceEqualityComparer.Instance),
            new HashSet<object>(ReferenceEqualityComparer.Instance), errors);
        return errors;
    }

    static void Visit(CsNode? node, string path, int depth, HashSet<CsNode> active,
        HashSet<object> origins, List<GenDiagnostic> errors)
    {
        if (node == null) { errors.Add(new("GEN002", path, "Required node is null.")); return; }
        if (depth > 128 || !active.Add(node)) { errors.Add(new("GEN001", path, "Node graph exceeds128 or contains a cycle.")); return; }
        if (node.Origin != null && !origins.Add(node.Origin))
            errors.Add(new("GEN074", path, "A parsed occurrence is owned twice; use Move or Detach before copying."));
        try
        {
            if (node is CsComment && node.Children.Count != 0) errors.Add(new("GEN075", path, "Comment nodes are leaves."));
            if (node is CsDocument d && d.Items.Any(n => n is not (CsUsing or CsAttribute or CsTypeDeclaration or CsNamespace or CsRaw)))
                errors.Add(new("GEN075", path, "Invalid document child kind."));
            if (node is CsNamespace n && n.Items.Any(x => x is not (CsUsing or CsTypeDeclaration or CsNamespace or CsRaw)))
                errors.Add(new("GEN075", path, "Invalid namespace child kind."));
            int index = 0;
            foreach (var child in node.Children) Visit(child, path + ".Children[" + index++ + "]", depth + 1, active, origins, errors);
        }
        catch (Exception exception) when (exception is NullReferenceException or ArgumentNullException)
        { errors.Add(new("GEN002", path, "Required child collection is null.")); }
        active.Remove(node);
    }
}
