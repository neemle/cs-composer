using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LambdaCompilerTests
{
    static CsType Function(CsType argument, CsType result) => Cs.Type("System.Func").Generic(argument, result);
    static CsExpression Add(CsExpression a, CsExpression b) => Cs.Binary(CsOperator.Add, a, b);

    static CsDocument Fixture(CsLanguageVersion version)
    {
        var type = Cs.Document(version).Using("System.Linq").Using("System.Threading.Tasks").Class("Lambdas").Public().Static();
        type.Method(Function(Cs.Int, Cs.Int), "MakeAdder").Public().Static().Parameter<int>("offset")
            .Expression(Cs.Lambda(Add(Cs.Id("x"), Cs.Id("offset")), Cs.LambdaParameter("x"))).End();
        type.Method(Function(Cs.Int, Function(Cs.Int, Cs.Int)), "Nested").Public().Static()
            .Expression(Cs.Lambda(Cs.Lambda(Add(Cs.Id("x"), Cs.Id("y")), Cs.LambdaParameter("y")), Cs.LambdaParameter("x"))).End();
        type.Method<int>("Callback").Public().Static().Parameter<int>("limit").Body(Cs.Var("total", Cs.Literal(0)),
            Cs.Local(Cs.Type<Action>(), "callback", Cs.Lambda(Cs.Body(
                Cs.For(Cs.ForVar("i", Cs.Literal(0)), Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Id("limit")),
                    new[] { Cs.PostIncrement(Cs.Id("i")) }, Cs.Body(Cs.If(Cs.Binary(CsOperator.Equal, Cs.Id("i"), Cs.Literal(2)),
                        Cs.Body(Cs.Continue())), Cs.Statement(Cs.Assign(Cs.Id("total"), Add(Cs.Id("total"), Cs.Id("i"))))))))),
            Cs.Statement(Cs.Id("callback").Call()), Cs.Return(Cs.Id("total"))).End();
        type.Method<int>("Query").Public().Static().Parameter(Cs.Int.Array(), "items").Expression(
            Cs.Id("items").Member("Where").Call(Cs.Lambda(Cs.Binary(CsOperator.GreaterThan, Cs.Id("x"), Cs.Literal(1)), Cs.LambdaParameter("x")))
                .Member("Select").Call(Cs.Lambda(Cs.Binary(CsOperator.Multiply, Cs.Id("x"), Cs.Literal(2)), Cs.LambdaParameter("x")))
                .Member("Sum").Call()).End();
        type.Method(Function(Cs.Int, Cs.Type("Task").Generic(Cs.Int)), "Async").Public().Static().Expression(
            Cs.AsyncLambda(Cs.Await(Cs.Id("Task").Member("FromResult").Call(Add(Cs.Id("x"), Cs.Literal(1)))), Cs.LambdaParameter("x"))).End();
        type.Method(Cs.Type("RefHandler"), "ByRef").Public().Static().Expression(
            Cs.Lambda(Cs.PreIncrement(Cs.Id("x")), Cs.LambdaParameter("x", Cs.Int, "ref"))).End();
        if (version >= CsLanguageVersion.CSharp9)
        {
            type.Method(Function(Cs.Int, Cs.Int), "Static").Public().Static().Expression(
                Cs.Lambda(Add(Cs.Id("x"), Cs.Literal(1)), Cs.LambdaParameter("x")) with { IsStatic = true }).End();
        }
        return type.End().Build();
    }

    [Theory]
    [InlineData(CsLanguageVersion.CSharp7_3, "7.3")]
    [InlineData(CsLanguageVersion.CSharp9, "9.0")]
    [InlineData(CsLanguageVersion.CSharp14, "14.0")]
    public async Task Lambdas_compile_and_execute_as_closures_callbacks_async_functions_and_linq_arguments(CsLanguageVersion version, string language)
    {
        var document = Fixture(version);
        Assert.Empty(document.Validate());
        string canonical = document.Render();
        var parsed = Cs.Parse(canonical, new() { LanguageVersion = version });
        Assert.Empty(parsed.Diagnostics);
        Assert.Empty(parsed.Root.Validate());
        var location = parsed.Root.Descendants().Single(n => n.Node is CsTypeDeclaration);
        var renamed = parsed.Root.Replace(location.Path, (CsTypeDeclaration)location.Node with { Name = "ParsedLambdas" });
        string reformatted = (renamed with { Usings = Values<CsUsing>.Empty }).Reformat().Render();
        string harness = "public delegate int RefHandler(ref int x); class Program { static void Check(bool ok) { if(!ok) throw new System.Exception(\"Lambda semantics\"); } static void Main() { ";
        foreach (string name in new[] { "Lambdas", "ParsedLambdas" })
        {
            harness += "Check(" + name + ".MakeAdder(7)(3) == 10); Check(" + name + ".Nested()(2)(3) == 5); " +
                "Check(" + name + ".Callback(5) == 8); Check(" + name + ".Query(new[] {1,2,3}) == 10); " +
                "Check(" + name + ".Async()(3).GetAwaiter().GetResult() == 4); " +
                "int x" + name + " = 1; Check(" + name + ".ByRef()(ref x" + name + ") == 2); Check(x" + name + " == 2); ";
            if (version >= CsLanguageVersion.CSharp9) harness += "Check(" + name + ".Static()(5) == 6); ";
        }
        await CompilerFixture.Run(canonical + reformatted + harness + "} }", language);
    }
}
