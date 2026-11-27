using System.Text.RegularExpressions;
using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-27 phase 2 compiler witness: newly built nodes replaced next to embedded directives (D1), reformatted directive
// owners (D2), edited multi-line chains (D6) and container comments after member removal (D4) still compile, and no
// directive shares a line with code.
[Trait("Category", "Integration")]
public sealed class KnownLimitationCompilerTests
{
    const string Statement = "class D1Statement\n{\n    int x, a = 1, b = 2, y = 3;\n    void M()\n    {\n        x = a\n#if X\n            + b\n#endif\n            ;\n    }\n}\n";
    const string Operand = "class D1Operand\n{\n    static int a = 1, b = 2, c = 3, d = 4, y = 5;\n    int F = a\n#if X\n#endif\n        + b;\n}\n";
    const string Query = "class Q\n{\n    public Q Where(System.Func<int, bool> f) => this;\n    public Q Select(System.Func<int, int> f) => this;\n    public Q ToList() => this;\n}\n";
    const string Chain = "class D6Chain\n{\n    static Q source = new Q(), y = new Q();\n    static System.Func<int, bool> f = Accept;\n    static bool Accept(int x) => true;\n    void M()\n    {\n        var r = source /*c*/\n            .Where(x => x > 0) // step\n            .Select(x => x) // step\n            .ToList();\n    }\n}\n";
    const string Members = "class D4Members\n{\n    int A;\n\n    // about B\n\n    int B;\n\n    // container tail\n\n    int D;\n}\n";

    static CsDocument Parse(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        return parsed.Root;
    }

    static CsDocument Swap(string source, Func<CsNode, bool> match, CsNode replacement)
    {
        var document = Parse(source);
        return document.Replace(document.Descendants().First(n => match(n.Node)).Path, replacement);
    }

    static bool Id(CsNode node, string text) => node is CsExpression { Kind: ExpressionKind.Identifier } e && e.Text == text;

    static IEnumerable<string> Outputs()
    {
        yield return Swap(Statement, n => Id(n, "a"), Cs.Id("y")).Render();
        yield return Swap(Statement, n => Id(n, "a"), Cs.Binary(CsOperator.Multiply, Cs.Id("y"), Cs.Id("b"))).Render();
        yield return Parse(Statement).Reformat().Render();
        yield return Swap(Operand, n => Id(n, "a"), Cs.Id("y")).Render();
        yield return Swap(Operand, n => Id(n, "b"), Cs.Binary(CsOperator.Subtract, Cs.Id("c"), Cs.Id("d"))).Render();
        yield return Swap(Operand, n => Id(n, "a"), Cs.Binary(CsOperator.Multiply, Cs.Id("c"), Cs.Id("d"))).Render();
        yield return Parse(Operand).Reformat().Render();
        yield return Swap(Chain, n => Id(n, "source"), Cs.Id("y")).Render();
        yield return Swap(Chain, n => n is CsExpression { Kind: ExpressionKind.Lambda }, Cs.Id("f")).Render();
        yield return Swap(Chain, n => n is CsExpression { Kind: ExpressionKind.Literal, Text: "0" }, Cs.Literal(1)).Render();
        yield return Parse(Chain).Reformat().Render();
        var members = Parse(Members);
        var path = members.Descendants().First(n => n.Node is CsTypeDeclaration).Path;
        var type = (CsTypeDeclaration)members.At(path);
        yield return members.Replace(path, type with { Members = new[] { type.Members[0] } }).Render();
        yield return members.Replace(path, type with { Members = new[] { type.Members[2], type.Members[1] } }).Render();
    }

    [Fact]
    public async Task Edited_outputs_keep_directives_on_their_own_lines_and_compile()
    {
        var outputs = Outputs().ToArray();
        foreach (string output in outputs)
            Assert.DoesNotMatch(new Regex(@"\S[ \t]*#(if|else|endif)\b"), output);
        // Every output declares its class under a name of its own, so all of them build as one program.
        string program = Query + string.Concat(outputs.Select((output, i) => Regex.Replace(output, @"class (D\w+)", "class $1_" + i))) +
            "class Program { static void Main() { } }\n";
        Assert.Empty(await CompilerFixture.Build(program, "14.0"));
    }
}
