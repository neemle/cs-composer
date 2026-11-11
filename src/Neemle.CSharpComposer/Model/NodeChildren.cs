namespace Neemle.CSharpComposer.Model;

internal static class NodeChildren
{
    internal static IReadOnlyList<CsNode> Get(CsNode node)
    {
        var result = new List<CsNode>();
        result.AddRange(node.Comments.Where(c => c.Placement != CommentPlacement.Trailing));
        result.AddRange(Payload(node));
        result.AddRange(node.Comments.Where(c => c.Placement == CommentPlacement.Trailing));
        return result;
    }

    static IEnumerable<CsNode> Payload(CsNode node) => node switch
    {
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Attributes.Cast<CsNode>().Concat(t.TypeParameters).Concat(t.PrimaryParameters)
            .Concat(t.BaseTypes).Concat(t.Members),
        CsMethod m => m.Attributes.Cast<CsNode>().Append(m.ReturnType).Concat(m.TypeParameters)
            .Concat(m.Parameters).Concat(Optional(m.Body)),
        CsBody b => Optional(b.Expression).Concat(b.Statements),
        CsExpression e => e.Arguments,
        CsField f => f.Attributes.Cast<CsNode>().Append(f.Type).Concat(Optional(f.Initializer)),
        CsType t => Optional(t.Element).Concat(t.Arguments).Concat(t.Elements),
        CsParameter p => p.Attributes.Cast<CsNode>().Append(p.Type).Concat(Optional(p.Default)),
        CsStatement s => Optional(s.Type).Concat(Optional(s.Expression)).Concat(Optional(s.Resource))
            .Concat(Optional(s.Body)).Concat(Optional(s.Else)).Concat(s.Catches).Concat(Optional(s.Finally)),
        CsCatch c => Optional(c.Type).Append(c.Body),
        TupleElement t => new[] { t.Type },
        AttributeArgument a => new[] { a.Value },
        CsAttribute a => new[] { a.Type }.Cast<CsNode>().Concat(a.Arguments),
        TypeParameter t => t.Constraints,
        CsConstraint c => Optional(c.Type),
        CsAccessor a => a.Attributes.Cast<CsNode>().Concat(Optional(a.Body)),
        CsProperty p => p.Attributes.Cast<CsNode>().Append(p.Type).Concat(p.Accessors)
            .Concat(Optional(p.Expression)).Concat(Optional(p.Initializer)),
        CsConstructor c => c.Attributes.Cast<CsNode>().Concat(c.Parameters).Concat(c.InitializerArguments)
            .Concat(Optional(c.Body)),
        CsEvent e => e.Attributes.Cast<CsNode>().Append(e.Type).Concat(e.Accessors).Concat(Optional(e.Initializer)),
        CsEnumMember e => e.Attributes.Cast<CsNode>().Concat(Optional(e.Value)),
        _ => Array.Empty<CsNode>()
    };

    static IEnumerable<CsNode> Optional(CsNode? node) => node == null ? Array.Empty<CsNode>() : new[] { node };
}
