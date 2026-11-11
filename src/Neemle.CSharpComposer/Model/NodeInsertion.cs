namespace Neemle.CSharpComposer.Model;
internal static class NodeInsertion
{
    internal static CsNode Insert(CsNode parent, int index, CsNode child)
    {
        var siblings = parent.Children;
        int CountBefore<T>() where T : CsNode => siblings.Take(index).Count(n => n is T);
        return (parent, child) switch
        {
            (_, CsComment comment) => parent with { Comments = Put(parent.Comments, CountBefore<CsComment>(), comment) },
            (CsDocument d, _) => d with { Items = Put(d.Items, index - d.Comments.Count, child) },
            (CsNamespace n, _) => n with { Items = Put(n.Items, index - n.Comments.Count, child) },
            (CsTypeDeclaration t, CsMember member) => t with { Members = Put(t.Members, CountBefore<CsMember>(), member) },
            (CsBody b, CsStatement statement) => b with { Statements = Put(b.Statements, CountBefore<CsStatement>(), statement) },
            (CsExpression e, CsExpression argument) => e with { Arguments = Put(e.Arguments, CountBefore<CsExpression>(), argument) },
            (CsMethod m, CsParameter parameter) => m with { Parameters = Put(m.Parameters, CountBefore<CsParameter>(), parameter) },
            (CsMethod m, TypeParameter parameter) => m with { TypeParameters = Put(m.TypeParameters, CountBefore<TypeParameter>(), parameter) },
            (CsTypeDeclaration t, CsType type) => t with { BaseTypes = Put(t.BaseTypes, CountBefore<CsType>(), type) },
            (CsTypeDeclaration t, TypeParameter parameter) => t with { TypeParameters = Put(t.TypeParameters, CountBefore<TypeParameter>(), parameter) },
            (CsTypeDeclaration t, CsParameter parameter) => t with { PrimaryParameters = Put(t.PrimaryParameters, CountBefore<CsParameter>(), parameter) },
            (CsProperty p, CsAccessor a) => p with { Accessors = Put(p.Accessors, CountBefore<CsAccessor>(), a) },
            (CsEvent e, CsAccessor a) => e with { Accessors = Put(e.Accessors, CountBefore<CsAccessor>(), a) },
            _ => throw new ArgumentException("This node kind cannot be inserted into the selected child slot.")
        };
    }

    static Values<T> Put<T>(Values<T> items, int index, T item)
    {
        var list = items.ToList();
        if (index < 0 || index > list.Count) throw new ArgumentOutOfRangeException(nameof(index));
        list.Insert(index, item);
        return new(list);
    }
}
