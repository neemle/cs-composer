namespace Neemle.CSharpComposer.Model;

internal static class NodeChildren
{
    internal static IReadOnlyList<CsNode> Get(CsNode node)
    {
        var result = new List<CsNode>();
        result.AddRange(node.Comments.Where(c => c.Placement != CommentPlacement.Trailing));
        if (node is CsMember { ExplicitInterface: not null } member) result.Add(member.ExplicitInterface);
        result.AddRange(Payload(node));
        result.AddRange(node.Comments.Where(c => c.Placement == CommentPlacement.Trailing));
        result.AddRange(node.SyntaxTrivia);
        return result;
    }

    static IEnumerable<CsNode> Payload(CsNode node) => node switch
    {
        CsSyntaxTrivia t => new[] { t.Content },
        CsComment c => Optional(c.Documentation),
        CsXmlNode x => x.Attributes.Cast<CsNode>().Concat(x.Content),
        CsXmlAttribute a => Optional(a.Reference),
        CsCref r => Optional(r.Qualifier).Concat(Optional(r.Type)).Concat(r.TypeArguments).Concat(r.Parameters ?? Values<CsFunctionPointerParameter>.Empty).Concat(Optional(r.Member)),
        CsDirective d => Optional(d.Condition).Concat(Optional(d.Start)).Concat(Optional(d.End)),
        CsUsing u => Optional(u.Target),
        CsGlobalStatement s => new[] { s.Value },
        CsTree t => t.Items,
        CsSymbol s => new[] { s.Options },
        CsSymbolOptions o => o.Parameters.Cast<CsNode>().Concat(Optional(o.ParameterSymbols)).Concat(Optional(o.Clauses)).Concat(Optional(o.EqualityKey)).Concat(o.Attributes).Concat(Optional(o.Body)).Concat(Optional(o.Alignment)).Concat(o.QueryClauses).Concat(o.TypeArguments).Concat(Optional(o.Type)).Concat(Optional(o.Pattern)).Concat(Optional(o.Initializer)).Concat(Optional(o.Value)).Concat(Optional(o.Target)).Concat(Optional(o.Arguments)).Concat(o.Parts),
        CsInstruction i => new[] { i.Options },
        CsInstructionOptions o => Optional(o.Initializer).Concat(Optional(o.Symbol)).Concat(Optional(o.Condition))
            .Concat(Optional(o.Iterators)).Concat(Optional(o.Value)).Concat(Optional(o.Body)).Concat(Optional(o.Else))
            .Concat(o.Sections).Concat(o.Catches).Concat(Optional(o.Finally)).Concat(Optional(o.Function)),
        CsDocument d => d.Items,
        CsNamespace n => n.Items,
        CsTypeDeclaration t => t.Attributes.Cast<CsNode>().Concat(t.TypeParameters).Concat(t.PrimaryParameters)
            .Concat(t.BaseTypes).Concat(t.UnionCases).Concat(t.BaseArguments ?? Values<CsExpression>.Empty).Concat(t.Members),
        CsDelegate d => d.Attributes.Cast<CsNode>().Append(d.ReturnType).Concat(d.TypeParameters).Concat(d.Parameters),
        CsDestructor d => d.Attributes.Cast<CsNode>().Concat(Optional(d.Body)),
        CsOperatorDeclaration o => o.Attributes.Cast<CsNode>().Append(o.ReturnType).Concat(o.Parameters).Concat(Optional(o.Body)),
        CsExtension e => e.Attributes.Cast<CsNode>().Concat(e.ReceiverAttributes).Append(e.ReceiverType).Concat(e.TypeParameters).Concat(e.Members),
        CsMethod m => m.Attributes.Cast<CsNode>().Append(m.ReturnType).Concat(m.TypeParameters)
            .Concat(m.Parameters).Concat(Optional(m.Body)),
        CsBody b => Optional(b.Tree).Concat(Optional(b.Expression)).Concat(b.Statements),
        CsExpression e => e.LambdaAttributes.Cast<CsNode>().Concat(e.QueryClauses).Concat(Optional(e.Alignment)).Concat(e.TypeArguments).Concat(Optional(e.Type)).Concat(Optional(e.Pattern)).Concat(Optional(e.Initializer)).Concat(e.Arguments).Concat(e.Arms).Concat(e.LambdaParameters).Concat(Optional(e.LambdaBody)),
        CsQueryClause q => Optional(q.Type).Concat(Optional(q.Value)).Concat(Optional(q.Key)).Concat(Optional(q.EqualsKey)).Concat(q.Clauses),
        CsLambdaParameter p => p.Attributes.Cast<CsNode>().Concat(Optional(p.Type)).Concat(Optional(p.Default)),
        CsPattern p => Optional(p.Type).Concat(Optional(p.Value)).Concat(Optional(p.Designation)).Concat(p.Patterns).Concat(p.Properties),
        CsSwitchLabel l => Optional(l.Pattern).Concat(Optional(l.When)),
        CsSwitchSection s => s.Labels.Cast<CsNode>().Append(s.Body),
        CsSwitchArm a => new CsNode[] { a.Pattern }.Concat(Optional(a.When)).Append(a.Value),
        CsField f => f.Attributes.Cast<CsNode>().Append(f.Type).Concat(Optional(f.FixedSize)).Concat(Optional(f.Initializer)).Concat(f.AdditionalVariables),
        CsType t => Optional(t.Element).Concat(t.Arguments).Concat(t.Elements).Concat(t.FunctionParameters),
        CsFunctionPointerParameter p => new[] { p.Type },
        CsParameter p => p.Attributes.Cast<CsNode>().Concat(Optional(p.Type)).Concat(Optional(p.Default)),
        CsStatement s => Statement(s),
        CsForInitializer f => Optional(f.Type).Concat(f.Variables).Concat(f.Expressions),
        CsVariableDeclarator v => Optional(v.FixedSize).Concat(Optional(v.Initializer)),
        CsCatch c => Optional(c.Type).Concat(Optional(c.Filter)).Append(c.Body),
        TupleElement t => new[] { t.Type },
        AttributeArgument a => new[] { a.Value },
        CsAttribute a => new[] { a.Type }.Cast<CsNode>().Concat(a.Arguments),
        TypeParameter t => t.Attributes.Cast<CsNode>().Concat(t.Constraints),
        CsConstraint c => Optional(c.Type),
        CsAccessor a => a.Attributes.Cast<CsNode>().Concat(Optional(a.Body)),
        CsProperty p => p.Attributes.Cast<CsNode>().Append(p.Type).Concat(p.Parameters).Concat(p.Accessors)
            .Concat(Optional(p.Expression)).Concat(Optional(p.Initializer)),
        CsConstructor c => c.Attributes.Cast<CsNode>().Concat(c.Parameters).Concat(c.InitializerArguments)
            .Concat(Optional(c.Body)),
        CsEvent e => e.Attributes.Cast<CsNode>().Append(e.Type).Concat(e.Accessors).Concat(Optional(e.Initializer)).Concat(e.AdditionalVariables),
        CsEnumMember e => e.Attributes.Cast<CsNode>().Concat(Optional(e.Value)),
        _ => Array.Empty<CsNode>()
    };

    static IEnumerable<CsNode> Statement(CsStatement s) => s.Kind == StatementKind.DoWhile
        ? Optional(s.Body).Concat(Optional(s.Expression)).Concat(Optional(s.Type)).Concat(Optional(s.ForInitializer)).Concat(Optional(s.Binding))
            .Concat(s.Iterators).Concat(Optional(s.Resource)).Concat(Optional(s.Else)).Concat(s.Catches)
            .Concat(Optional(s.Finally)).Concat(s.Sections).Concat(Optional(s.Function)).Concat(Optional(s.Directive)).Concat(Optional(s.Disabled))
        : Optional(s.Type).Concat(Optional(s.ForInitializer)).Concat(Optional(s.Binding)).Concat(Optional(s.Expression)).Concat(s.Iterators)
            .Concat(Optional(s.Resource)).Concat(Optional(s.Body)).Concat(Optional(s.Else)).Concat(s.Catches)
            .Concat(Optional(s.Finally)).Concat(s.Sections).Concat(Optional(s.Function)).Concat(Optional(s.Directive)).Concat(Optional(s.Disabled));

    static IEnumerable<CsNode> Optional(CsNode? node) => node == null ? Array.Empty<CsNode>() : new[] { node };
}
