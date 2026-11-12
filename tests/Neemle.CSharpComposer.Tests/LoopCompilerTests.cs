using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LoopCompilerTests
{
    static CsExpression Add(CsExpression a, CsExpression b) => Cs.Binary(CsOperator.Add, a, b);
    static CsExpression Equal(string name, int value) => Cs.Binary(CsOperator.Equal, Cs.Id(name), Cs.Literal(value));
    static CsStatement Increment(string name) => Cs.Statement(Cs.PostIncrement(Cs.Id(name)));

    static CsDocument Fixture(CsLanguageVersion version)
    {
        var type = Cs.Document(version).Class("Loops").Public().Static();
        type.Method<int>("ForSum").Public().Static().Parameter<int>("limit").Body(
            Cs.Var("sum", Cs.Literal(0)),
            Cs.For(Cs.ForVar("i", Cs.Literal(0)), Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Id("limit")),
                new[] { Cs.PostIncrement(Cs.Id("i")) }, Cs.Body(
                    Cs.If(Equal("i", 2), Cs.Body(Cs.Continue())), Cs.If(Equal("i", 5), Cs.Body(Cs.Break())),
                    Cs.Statement(Cs.Assign(Cs.Id("sum"), Add(Cs.Id("sum"), Cs.Id("i")))))), Cs.Return(Cs.Id("sum"))).End();
        type.Method<int>("Multi").Public().Static().Body(Cs.Var("sum", Cs.Literal(0)),
            Cs.For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i", Cs.Literal(0)), Cs.Variable("j", Cs.Literal(4))),
                Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Id("j")),
                new[] { Cs.PostIncrement(Cs.Id("i")), Cs.PreDecrement(Cs.Id("j")) },
                Cs.Body(Cs.Statement(Cs.Assign(Cs.Id("sum"), Add(Cs.Id("sum"), Add(Cs.Id("i"), Cs.Id("j"))))))),
            Cs.Return(Cs.Id("sum"))).End();
        type.Method<int>("ExpressionHeader").Public().Static().Body(Cs.Local(Cs.Int, "i"), Cs.Local(Cs.Int, "j"),
            Cs.For(Cs.ForExpressions(Cs.Assign(Cs.Id("i"), Cs.Literal(0)), Cs.Assign(Cs.Id("j"), Cs.Literal(0))),
                Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Literal(3)),
                new[] { Cs.PreIncrement(Cs.Id("i")), Cs.PostIncrement(Cs.Id("j")) }, Cs.Body()),
            Cs.Return(Add(Cs.Binary(CsOperator.Multiply, Cs.Id("i"), Cs.Literal(10)), Cs.Id("j")))).End();
        type.Method<int>("Each").Public().Static().Parameter(Cs.Type("System.Collections.Generic.IEnumerable").Generic(Cs.Int), "items").Body(
            Cs.Var("sum", Cs.Literal(0)), Cs.ForEach("item", Cs.Id("items"), Cs.Body(
                Cs.If(Equal("item", 2), Cs.Body(Cs.Continue())), Cs.If(Equal("item", 4), Cs.Body(Cs.Break())),
                Cs.Statement(Cs.Assign(Cs.Id("sum"), Add(Cs.Id("sum"), Cs.Id("item")))))), Cs.Return(Cs.Id("sum"))).End();
        type.Method<int>("WhileAndDo").Public().Static().Body(Cs.Var("i", Cs.Literal(0)), Cs.Var("sum", Cs.Literal(0)),
            Cs.While(Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Literal(5)), Cs.Body(Increment("i"),
                Cs.If(Equal("i", 2), Cs.Body(Cs.Continue())), Cs.If(Equal("i", 4), Cs.Body(Cs.Break())),
                Cs.Statement(Cs.Assign(Cs.Id("sum"), Add(Cs.Id("sum"), Cs.Id("i")))))),
            Cs.DoWhile(Cs.Body(Increment("sum"), Cs.Continue()), Cs.Literal(false)), Cs.Return(Cs.Id("sum"))).End();
        type.Method<int>("FinallyAndSwitch").Public().Static().Body(Cs.Var("i", Cs.Literal(0)), Cs.Var("count", Cs.Literal(0)),
            Cs.For(Cs.Body(Increment("i"), Cs.Try(Cs.Body(Cs.Switch(Cs.Id("i"),
                Cs.Section(Cs.Body(Cs.Continue()), Cs.Case(Cs.Literal(1))),
                Cs.Section(Cs.Body(Cs.Break()), Cs.Default())),
                Cs.If(Equal("i", 3), Cs.Body(Cs.Break()))), finallyBody: Cs.Body(Increment("count"))))),
            Cs.Return(Add(Cs.Binary(CsOperator.Multiply, Cs.Id("i"), Cs.Literal(10)), Cs.Id("count")))).End();
        return type.End().Build();
    }

    [Theory]
    [InlineData(CsLanguageVersion.CSharp7_3, "7.3")]
    [InlineData(CsLanguageVersion.CSharp14, "14.0")]
    public async Task Generated_loops_compile_and_execute_with_correct_jumps_disposal_and_evaluation_order(CsLanguageVersion version, string language)
    {
        var document = Fixture(version);
        Assert.Empty(document.Validate());
        string canonical = document.Render();
        var parsed = Cs.Parse(canonical, new() { LanguageVersion = version });
        Assert.Empty(parsed.Diagnostics);
        var location = parsed.Root.Descendants().Single(n => n.Node is CsTypeDeclaration);
        string reformatted = parsed.Root.Replace(location.Path, (CsTypeDeclaration)location.Node with { Name = "ParsedLoops" }).Reformat().Render();
        string harness = "class Program { static void Check(bool ok) { if (!ok) throw new System.Exception(\"Loop semantics\"); } static void Main() { ";
        foreach (string name in new[] { "Loops", "ParsedLoops" })
        {
            harness += "Check(" + name + ".ForSum(8) == 8); Check(" + name + ".ForSum(0) == 0); " +
                "Check(" + name + ".Multi() == 8); Check(" + name + ".ExpressionHeader() == 33); " +
                "var p" + name + " = new Probe(); Check(" + name + ".Each(p" + name + ") == 4); Check(p" + name + ".Disposed); " +
                "Check(" + name + ".WhileAndDo() == 5); Check(" + name + ".FinallyAndSwitch() == 33); ";
        }
        const string probe = "class Probe : System.Collections.Generic.IEnumerable<int> { public bool Disposed; " +
            "public System.Collections.Generic.IEnumerator<int> GetEnumerator() { try { for(int i=1;i<=5;i++) yield return i; } finally { Disposed=true; } } " +
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); } }";
        await CompilerFixture.Run(canonical + reformatted + harness + "} }" + probe, language);
    }
}
