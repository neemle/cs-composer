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
        CsTree t => t.Items,
        CsSymbol s => new[] { s.Options },
        CsSymbolOptions o => Optional(o.Type).Concat(Optional(o.Value)).Concat(Optional(o.Target)).Concat(Optional(o.Arguments)).Concat(o.Parts),
        CsInstruction i => new[] { i.Options },
        CsInstructionOptions o => Optional(o.Initializer).Concat(Optional(o.Symbol)).Concat(Optional(o.Condition))
            .Concat(Optional(o.Iterators)).Concat(Optional(o.Value)).Concat(Optional(o.Body)).Concat(Optional(o.Else)),
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Attributes.Cast<CsNode>().Concat(t.TypeParameters).Concat(t.PrimaryParameters)
            .Concat(t.BaseTypes).Concat(t.Members),
        CsMethod m => m.Attributes.Cast<CsNode>().Append(m.ReturnType).Concat(m.TypeParameters)
            .Concat(m.Parameters).Concat(Optional(m.Body)),
        CsBody b => Optional(b.Tree).Concat(Optional(b.Expression)).Concat(b.Statements),
        CsExpression e => Optional(e.Type).Concat(e.Arguments).Concat(e.Arms).Concat(e.LambdaParameters).Concat(Optional(e.LambdaBody)),
        CsLambdaParameter p => Optional(p.Type),
        CsPattern p => Optional(p.Type).Concat(Optional(p.Value)).Concat(p.Patterns),
        CsSwitchLabel l => Optional(l.Pattern).Concat(Optional(l.When)),
        CsSwitchSection s => s.Labels.Cast<CsNode>().Append(s.Body),
        CsSwitchArm a => new CsNode[] { a.Pattern }.Concat(Optional(a.When)).Append(a.Value),
        CsField f => f.Attributes.Cast<CsNode>().Append(f.Type).Concat(Optional(f.Initializer)),
        CsType t => Optional(t.Element).Concat(t.Arguments).Concat(t.Elements),
        CsParameter p => p.Attributes.Cast<CsNode>().Append(p.Type).Concat(Optional(p.Default)),
        CsStatement s => Statement(s),
        CsForInitializer f => Optional(f.Type).Concat(f.Variables).Concat(f.Expressions),
        CsVariableDeclarator v => Optional(v.Initializer),
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

    static IEnumerable<CsNode> Statement(CsStatement s) => s.Kind == StatementKind.DoWhile
        ? Optional(s.Body).Concat(Optional(s.Expression)).Concat(Optional(s.Type)).Concat(Optional(s.ForInitializer))
            .Concat(s.Iterators).Concat(Optional(s.Resource)).Concat(Optional(s.Else)).Concat(s.Catches)
            .Concat(Optional(s.Finally)).Concat(s.Sections)
        : Optional(s.Type).Concat(Optional(s.ForInitializer)).Concat(Optional(s.Expression)).Concat(s.Iterators)
            .Concat(Optional(s.Resource)).Concat(Optional(s.Body)).Concat(Optional(s.Else)).Concat(s.Catches)
            .Concat(Optional(s.Finally)).Concat(s.Sections);

    static IEnumerable<CsNode> Optional(CsNode? node) => node == null ? Array.Empty<CsNode>() : new[] { node };
}
