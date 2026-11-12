using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SemanticPrimitiveCompositionTests
{
    static CsSymbol Op(CsOperator op, params CsNode[] parts) => Cs.Symbol(new() { Operator = op, Parts = parts });

    [Fact]
    public void Functions_and_queries_use_symbols_for_parameters_clauses_and_every_operand()
    {
        var parameter = Cs.Symbol("x", new() { Operation = SymbolOperation.Parameter, Type = Cs.Int });
        var lambda = Cs.Symbol(new()
        {
            Operation = SymbolOperation.Lambda,
            ParameterSymbols = Cs.Compound(parameter),
            Value = Op(CsOperator.Add, Cs.Symbol("x"), Cs.Value(1))
        });
        var query = Cs.Symbol(new()
        {
            Operation = SymbolOperation.Query,
            Parts = new CsNode[] {
            Cs.Symbol("x", new() { Operation = SymbolOperation.QueryClause, QueryKind = QueryClauseKind.From, Value = Cs.Symbol("items") }),
            Cs.Symbol(new() { Operation = SymbolOperation.QueryClause, QueryKind = QueryClauseKind.Where,
                Value = Op(CsOperator.GreaterThan, Cs.Symbol("x"), Cs.Value(1)) }),
            Cs.Symbol(new() { Operation = SymbolOperation.QueryClause, QueryKind = QueryClauseKind.Select, Value = Cs.Symbol("x") }) }
        });
        var tree = Cs.Tree(Cs.Symbol("increment", new() { AsVar = true, Value = lambda }), Cs.Symbol("query", new() { AsVar = true, Value = query }));
        Assert.Empty(tree.Validate());
        var parsed = Cs.ParseTree(tree.Render());
        Assert.Empty(parsed.Diagnostics);
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsSymbol { Options.Operation: SymbolOperation.Parameter });
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsSymbol { Options.Operation: SymbolOperation.QueryClause });
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsQueryClause or CsLambdaParameter);
    }

    [Fact]
    public void Nullable_value_types_and_constrained_type_parameters_remain_available_in_73()
    {
        const string source = "struct S { } class C<T> where T : struct { S? first; System.DateTime? second; T? third; }";
        var parsed = Cs.Parse(source, new() { LanguageVersion = CsLanguageVersion.CSharp7_3 });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        Assert.Contains(Cs.Parse("class C<T> { T? value; }", new() { LanguageVersion = CsLanguageVersion.CSharp8 }).Diagnostics, d => d.Code == "GEN073");
        Assert.Empty(Cs.Parse("class C<T> { T? value; }", new() { LanguageVersion = CsLanguageVersion.CSharp9 }).Diagnostics);
    }
}
