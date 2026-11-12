namespace Neemle.CSharpComposer.Model;

internal static partial class SymbolModel
{
    sealed partial class Lowerer
    {
        CsLambdaParameter Parameter(CsNode node, string path, bool attach)
        {
            if (node is CsLambdaParameter existing) return existing;
            Reject(node is not CsSymbol { Options.Operation: SymbolOperation.Parameter }, path, "A signature compound contains parameter symbols.");
            var symbol = (CsSymbol)node; var o = symbol.Options;
            return Keep(new CsLambdaParameter(symbol.Name!, o.Type, o.ParameterModifier)
            {
                Attributes = o.Attributes,
                Default = o.Value == null ? null : Expression(o.Value, path + ".Value", attach)
            }, symbol, attach);
        }

        CsQueryClause QueryClause(CsNode node, string path, bool attach)
        {
            if (node is CsQueryClause existing) return existing;
            Reject(node is not CsSymbol { Options.Operation: SymbolOperation.QueryClause }, path, "A query contains clause symbols.");
            var symbol = (CsSymbol)node; var o = symbol.Options;
            return Keep(new CsQueryClause(o.QueryKind)
            {
                Name = symbol.Name,
                Type = o.Type,
                Into = o.Into,
                Descending = o.Descending,
                Value = o.Value == null ? null : Expression(o.Value, path + ".Value", attach),
                Key = o.Target == null ? null : Expression(o.Target, path + ".Target", attach),
                EqualsKey = o.EqualityKey == null ? null : Expression(o.EqualityKey, path + ".EqualityKey", attach),
                Clauses = o.Clauses == null ? Values<CsQueryClause>.Empty : new(o.Clauses.Options.Parts.Select((p, i) => QueryClause(p, path + ".Clauses[" + i + "]", attach)))
            }, symbol, attach);
        }
    }
}
