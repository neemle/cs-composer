using System.Text.RegularExpressions;
using Neemle.CSharpComposer.Model;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-27 phase 2 compiler witness: newly built nodes replaced next to embedded directives (D1), reformatted directive
// owners (D2), edited multi-line chains (D6) and container comments after member removal (D4) still compile, and no
// directive shares a line with code. Phase 3: a type header edited with its members (R1), edits inside brace-less
// embedded statements (R2), a Reformat with container comments (R3), a reshaped chain link (O1), replacements next to an
// indented region (O2a), in a lambda body (O2b), in interpolation holes (O2c) and next to an inactive branch (O2d) compile
// with the branch both inactive and active. Phase 4: a header with its own comment edited (only the header renders
// again), edits inside interpolation holes, of a text segment and of a format, an `if` put after `else` (L2) and an if
// rendered whole with a comment before its `else` (L3) compile too.
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

    const string Phase3 = "class P3\n{\n    static int x, a = 1, b = 2, c = 3, y = 4;\n    static int? p = null;\n    static int q = 5;\n    static bool d, g;\n    static object k = new object(), m = new object();\n" +
        "    int G = b\n        #region Q\n        + c;\n        #endregion\n    System.Func<int, int> F = x =>\n        x + 1;\n" +
        "    string S = $\"a{y,5}b{ q :x2}\";\n    string R = $\"\"\"\n        a {y}\n          b {q}\n        \"\"\";\n" +
        "    int H(int u, int v) => u + v;\n    void A() { }\n    void M()\n    {\n        if (d) x = 1;\n        if (g)\n            A();\n        else\n            A();\n" +
        "        while (g) A();\n        lock (k) A();\n        var r = new Q()\n            .Select(x => x * 2) /* s */\n            .ToList();\n" +
        "        H(a,\n#if X\n          b);\n#else\n          c);\n#endif\n    }\n}\n";
    const string Header = "class Phase3Header\n{\n    int  A;\n\n    // about B\n\n    int  B;\n}\n";
    const string Query3 = "class Q\n{\n    public Q Select(System.Func<int, int> f, int z = 0) => this;\n    public Q ToList() => this;\n}\n";

    static IEnumerable<string> Phase3Outputs()
    {
        var header = Parse(Header);
        var path = header.Descendants().First(n => n.Node is CsTypeDeclaration).Path;
        var type = (CsTypeDeclaration)header.At(path);
        yield return header.Replace(path, type with { Modifiers = new[] { "sealed" }, Members = new[] { type.Members[1] } }).Render();
        yield return Parse(Header).Reformat().Render();
        var document = Parse(Phase3);
        CsDocument Edit(CsDocument d, Func<CsNode, bool> match, int nth, Func<CsNode, CsNode> change)
        {
            var at = d.Descendants().Where(n => match(n.Node)).ElementAt(nth).Path;
            return d.Replace(at, change(d.At(at)));
        }
        var coalesce = Cs.Binary(CsOperator.Coalesce, Cs.Id("p"), Cs.Id("q"));
        // In document order: the literal 1 of `x = 1` follows the field's and the lambda's; the second `c` is the call's.
        document = Edit(document, n => n is CsExpression { Kind: ExpressionKind.Literal, Text: "1" }, 2, _ => Cs.Literal(2));
        document = Edit(document, n => n is CsStatement { Kind: StatementKind.Lock }, 0, n => ((CsStatement)n) with { Expression = Cs.Id("m") });
        document = Edit(document, n => Id(n, "b"), 0, _ => coalesce);
        document = Edit(document, n => n is CsExpression { Kind: ExpressionKind.Binary, Operator: CsOperator.Add } e && e.Arguments[0].Text == "x", 0, _ => coalesce);
        document = Edit(document, n => Id(n, "y"), 0, _ => Cs.Id("x"));
        document = Edit(document, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments[0].Text == "Select", 0, n => ((CsExpression)n) with { Arguments = new(((CsExpression)n).Arguments.Append(Cs.Literal(1))) });
        document = Edit(document, n => Id(n, "c"), 1, _ => coalesce);
        yield return document.Render();
    }

    [Fact]
    public async Task Phase_3_edits_keep_their_layout_and_compile_with_the_branch_inactive_and_active()
    {
        var outputs = Phase3Outputs().ToArray();
        foreach (string output in outputs)
        {
            Assert.DoesNotMatch(new Regex(@"\S[ \t]*#(if|else|endif|region|endregion)\b"), output);
            Assert.DoesNotMatch(new Regex(@"[ \t]\n"), output);
        }
        string edited = outputs[^1];
        Assert.Contains("if (d) x = 2;", edited);
        Assert.Contains("lock (m) A();", edited);
        Assert.Contains("    int G = (p ?? q)\n        #region Q\n", edited);
        Assert.Contains("x =>\n        p ?? q;", edited);
        Assert.Contains("$\"a{x,5}b{ q :x2}\"", edited);
        Assert.Contains(".Select(x => x * 2, 1) /* s */\n", edited);
        Assert.Contains("#else\n          p ?? q);\n#endif", edited);
        string program = Query3 + string.Concat(outputs.Select((output, i) => Regex.Replace(output, @"class (P3|Phase3\w+)", "class $1_" + i))) +
            "class Program { static void Main() { } }\n";
        Assert.Empty(await CompilerFixture.Build(program, "14.0"));
        Assert.Empty(await CompilerFixture.Build("#define X\n" + program, "14.0"));
    }

    const string Phase4 = "class P4 : P4Base /* base */\n{\n    static bool a, b, c;\n    static int x = 1, zz = 2, y = 3, w = 4;\n    static void X() { }\n    static void Y() { }\n    static void Z() { }\n" +
        "    string S = $\"a{$\"b{x,2}\"}c\";\n    string R = $\"\"\"\n        {x +\n            y}\n        \"\"\";\n    string T = $\"a{w}b{ y :x2}\";\n" +
        "    void M()\n    {\n        if (a) X(); else Y();\n        if (b) X(); // c\n        // lead\n        else Y();\n    }\n}\nclass P4Base { }\n";

    static string Phase4Output()
    {
        var document = Parse(Phase4);
        CsDocument Edit(CsDocument d, Func<CsNode, bool> match, int nth, Func<CsNode, CsNode> change)
        {
            var at = d.Descendants().Where(n => match(n.Node)).ElementAt(nth).Path;
            return d.Replace(at, change(d.At(at)));
        }
        CsStatement If(string condition) => Cs.If(Cs.Id(condition), Cs.Body(Cs.Statement(Cs.Call(Cs.Id("Z")))));
        document = Edit(document, n => n is CsTypeDeclaration { Name: "P4" }, 0, n => ((CsTypeDeclaration)n) with { Modifiers = new[] { "sealed" } });
        document = Edit(document, n => Id(n, "x"), 0, _ => Cs.Id("zz"));
        document = Edit(document, n => Id(n, "x"), 0, _ => Cs.Id("zz"));
        document = Edit(document, n => n is CsExpression { Kind: ExpressionKind.StringText, Text: "b" }, 1, _ => new CsExpression(ExpressionKind.StringText, "q", Values<CsExpression>.Empty));
        document = Edit(document, n => n is CsExpression { Format: "x2" }, 0, n => ((CsExpression)n) with { Format = "x4" });
        document = Edit(document, n => n is CsStatement { Kind: StatementKind.Expression }, 2, _ => If("b"));
        document = Edit(document, n => n is CsStatement { Kind: StatementKind.Expression }, 1, _ => If("c"));
        return document.Render();
    }

    [Fact]
    public async Task Phase_4_edits_keep_their_layout_and_compile()
    {
        string edited = Phase4Output();
        Assert.DoesNotMatch(new Regex(@"[ \t]\n"), edited);
        Assert.Contains("sealed class P4 : P4Base /* base */\n{\n    static bool a, b, c;\n", edited);
        Assert.Contains("$\"a{$\"b{zz,2}\"}c\"", edited);
        Assert.Contains("        {zz +\n            y}\n", edited);
        Assert.Contains("$\"a{w}q{ y :x4}\"", edited);
        Assert.Contains("        if (a) X(); else if (c)\n        {\n            Z();\n        }\n", edited);
        Assert.Contains("            }\n        }\n        // lead\n        else\n        {\n            Y();\n        }\n", edited);
        Assert.Empty(await CompilerFixture.Build(edited + "class Program { static void Main() { } }\n", "14.0"));
    }
}
