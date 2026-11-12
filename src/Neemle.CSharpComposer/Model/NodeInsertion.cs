namespace Neemle.CSharpComposer.Model;

internal static class NodeInsertion
{
    internal static CsNode Insert(CsNode parent, int index, CsNode child)
    {
        var siblings = parent.Children;
        int InList<T>(Values<T> list) where T : CsNode => siblings.Take(index).Count(n => list.Any(v => ReferenceEquals(v, n)));
        int CountBefore<T>() where T : CsNode => siblings.Take(index).Count(n => n is T);
        return (parent, child) switch
        {
            (_, CsSyntaxTrivia trivia) => parent with { SyntaxTrivia = Put(parent.SyntaxTrivia, CountBefore<CsSyntaxTrivia>(), trivia) },
            (_, CsComment comment) => parent with { Comments = Put(parent.Comments, CountBefore<CsComment>(), comment) },
            (CsTree t, _) => t with { Items = Put(t.Items, InList(t.Items), child) },
            (CsSymbolOptions o, CsLambdaParameter parameter) => o with { Parameters = Put(o.Parameters, InList(o.Parameters), parameter) },
            (CsSymbolOptions o, CsAttribute attribute) => o with { Attributes = Put(o.Attributes, InList(o.Attributes), attribute) },
            (CsSymbolOptions o, CsQueryClause clause) => o with { QueryClauses = Put(o.QueryClauses, InList(o.QueryClauses), clause) },
            (CsSymbolOptions o, CsType type) when o.Operation == SymbolOperation.None && !SymbolModel.Declaration(new(null, o)) && SymbolModes.Count(o) == 0 => o with { TypeArguments = Put(o.TypeArguments, InList(o.TypeArguments), type) },
            (CsSymbolOptions o, _) when o.AsCompound || o.AsInitializer || o.AsConditional || o.Operation is SymbolOperation.Tuple or SymbolOperation.Collection or SymbolOperation.AnonymousObject or SymbolOperation.ImplicitIndex or SymbolOperation.StackAlloc or SymbolOperation.CollectionArguments or SymbolOperation.InterpolatedString or SymbolOperation.Declaration => o with { Parts = Put(o.Parts, InList(o.Parts), child) },
            (CsDocument d, _) => d with { Items = Put(d.Items, InList(d.Items), child) },
            (CsNamespace n, _) => n with { Items = Put(n.Items, InList(n.Items), child) },
            (CsXmlNode x, CsXmlNode content) => x with { Content = Put(x.Content, InList(x.Content), content) },
            (CsXmlNode x, CsXmlAttribute attribute) => x with { Attributes = Put(x.Attributes, InList(x.Attributes), attribute) },
            (CsCref r, CsFunctionPointerParameter parameter) => r with { Parameters = Put(r.Parameters ?? Values<CsFunctionPointerParameter>.Empty, r.Parameters == null ? 0 : InList(r.Parameters), parameter) },
            (CsExtension e, CsMember member) => e with { Members = Put(e.Members, InList(e.Members), member) },
            (CsExtension e, TypeParameter parameter) => e with { TypeParameters = Put(e.TypeParameters, InList(e.TypeParameters), parameter) },
            (CsDelegate d, CsParameter parameter) => d with { Parameters = Put(d.Parameters, InList(d.Parameters), parameter) },
            (CsDelegate d, TypeParameter parameter) => d with { TypeParameters = Put(d.TypeParameters, InList(d.TypeParameters), parameter) },
            (CsOperatorDeclaration o, CsParameter parameter) => o with { Parameters = Put(o.Parameters, InList(o.Parameters), parameter) },
            (CsProperty p, CsParameter parameter) => p with { Parameters = Put(p.Parameters, InList(p.Parameters), parameter) },
            (CsType t, CsFunctionPointerParameter parameter) => t with { FunctionParameters = Put(t.FunctionParameters, InList(t.FunctionParameters), parameter) },
            (CsExpression e, CsQueryClause clause) => e with { QueryClauses = Put(e.QueryClauses, InList(e.QueryClauses), clause) },
            (CsQueryClause q, CsQueryClause clause) => q with { Clauses = Put(q.Clauses, InList(q.Clauses), clause) },
            (CsInstructionOptions o, CsSwitchSection section) => o with { Sections = Put(o.Sections, InList(o.Sections), section) },
            (CsInstructionOptions o, CsCatch handler) => o with { Catches = Put(o.Catches, InList(o.Catches), handler) },
            (CsPattern p, CsPattern pattern) when p.Kind == PatternKind.Recursive && p.HasProperties && (pattern.Kind == PatternKind.Property || !p.HasPositional) => p with { Properties = Put(p.Properties, InList(p.Properties), pattern) },
            (CsPattern p, CsPattern pattern) => p with { Patterns = Put(p.Patterns, InList(p.Patterns), pattern) },
            (CsTypeDeclaration t, CsMember member) => t with { Members = Put(t.Members, CountBefore<CsMember>(), member) },
            (CsStatement s, CsSwitchSection section) when s.Kind == StatementKind.Switch =>
                s with { Sections = Put(s.Sections, CountBefore<CsSwitchSection>(), section) },
            (CsSwitchSection s, CsSwitchLabel label) => s with { Labels = Put(s.Labels, CountBefore<CsSwitchLabel>(), label) },
            (CsExpression e, CsSwitchArm arm) when e.Kind == ExpressionKind.Switch =>
                e with { Arms = Put(e.Arms, CountBefore<CsSwitchArm>(), arm) },
            (CsForInitializer f, CsVariableDeclarator variable) =>
                f with { Variables = Put(f.Variables, CountBefore<CsVariableDeclarator>(), variable) },
            (CsForInitializer f, CsExpression expression) =>
                f with { Expressions = Put(f.Expressions, CountBefore<CsExpression>(), expression) },
            (CsStatement s, CsExpression iterator) when s.Kind == StatementKind.For =>
                s with
                {
                    Iterators = Put(s.Iterators, CountBefore<CsExpression>() -
                    (s.Expression != null && siblings.Take(index).Contains(s.Expression) ? 1 : 0), iterator)
                },
            (CsExpression e, CsLambdaParameter parameter) when e.Kind is ExpressionKind.Lambda or ExpressionKind.AnonymousMethod =>
                e with { LambdaParameters = Put(e.LambdaParameters, CountBefore<CsLambdaParameter>(), parameter) },
            (CsBody b, CsStatement statement) => b with { Statements = Put(b.Statements, CountBefore<CsStatement>(), statement) },
            (CsExpression e, CsExpression argument) => e with { Arguments = Put(e.Arguments, InList(e.Arguments), argument) },
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
