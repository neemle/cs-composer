using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SyntaxGapRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "expression directives", "class C { int M() => (\n#if true\n1\n#else\n2\n#endif\n); }" };
        yield return new object[] { "parameter directives", "class C { void M(\n#if true\nint x\n#else\nstring x\n#endif\n) {} }" };
        yield return new object[] { "attribute directives", "[System.Obsolete]\n#if true\n[System.Serializable]\n#endif\nclass C {}" };
        yield return new object[] { "named index", "class C { int this[int index] => index; int M() => this[index: 1]; }" };
        yield return new object[] { "address cast", "unsafe class C { void M() { int x=1; int* p=(int*)&x; } }" };
        yield return new object[] { "dereference cast", "unsafe class C { int M(int* p) => (int)*p; }" };
        yield return new object[] { "type named var", "class var {} class C { void M() { var x; x = new var(); } }" };
        yield return new object[] { "nullable type test", "class C { bool M(object x) => x is int?; }" };
        yield return new object[] { "collection cast", "class C { int[] M() => (int[])[1,2]; }" };
        yield return new object[] { "fraction cast", "class C { int M() => (int).5; }" };
        yield return new object[] { "interpolation cast", "class C { System.FormattableString M() => (System.FormattableString)$\"{1}\"; }" };
        yield return new object[] { "nameof method", "class C { int nameof(int x) => x; int M() => nameof(x: 1); }" };
        yield return new object[] { "generic nameof method", "class C { T nameof<T>(T x) => x; int M() => nameof<int>(1); }" };
        yield return new object[] { "await method", "class C { int await(int x)=>x; int M()=>await(x: 1); }" };
        yield return new object[] { "contextual type names", "class partial {} class async {} class C { partial value; async M()=>null; }" };
        yield return new object[] { "future contextual type name", "class union {} class C { union value; union M()=>null; }" };
        yield return new object[] { "interpolation contextual names", "class C { string M() { int await=1; return $\"{await}\"; } }" };
        yield return new object[] { "escaped cast operand", "class C { int M() { int \\u0061=1; return (int)\\u0061; } }" };
        yield return new object[] { "extension receiver attributes", "using System; class AAttribute:Attribute {} static class C { extension([A] string text) { public int Count=>text.Length; } }" };
        yield return new object[] { "scoped extension receiver", "static class C { extension(scoped ref System.Span<int> values) { public int First=>values[0]; } }" };
        yield return new object[] { "in index argument", "class C { int this[in int x]=>x; int M() { int x=1; return this[in x]; } }" };
        yield return new object[] { "generic directives", "class C { System.Collections.Generic.List<\n#if true\nint\n#else\nstring\n#endif\n> M()=>null; }" };
        yield return new object[] { "nested directives", "class C { int M() => (\n#if true\n1+ (\n#if false\ninvalid \" text\n#else\n2\n#endif\n)\n#else\ninvalid\n#endif\n); }" };
        yield return new object[] { "cross-node directives", "class C {\n#if true\nint M() => (\n#if true\n1\n#endif\n);\n#endif\n}" };
        yield return new object[] { "modifier directives", "public\n#if true\nstatic\n#endif\nclass C {}" };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Valid_combinations_remain_typed_and_compile_after_formatting(string name, string source)
    {
        var parsed = Cs.Parse(source, new() { LanguageVersion = CsLanguageVersion.CSharp14 });
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Root.Validate());
        AssertTyped(parsed.Root);
        Assert.Equal(source, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        var reparsed = Cs.Parse(formatted, new() { LanguageVersion = CsLanguageVersion.CSharp14 });
        Assert.Empty(reparsed.Diagnostics);
        Assert.Empty(reparsed.Root.Validate());
        AssertTyped(reparsed.Root);
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
        await CompilerFixture.Run(formatted + "\nclass Program { static void Main() {} }", "14.0");
    }

    static void AssertTyped(CsDocument document) => Assert.DoesNotContain(document.Descendants(), n =>
        n.Node is CsRaw or CsRawMember or CsExpression { Kind: ExpressionKind.Raw } or CsStatement { Kind: StatementKind.Raw } or CsType { Form: TypeForm.Raw });

    [Fact]
    public void Casts_keep_their_operand_kinds_instead_of_becoming_binary_operations()
    {
        foreach (var (source, kind) in new[] { ("(int)*p", ExpressionKind.Unary), ("(int*)&x", ExpressionKind.Unary), ("(int[])[1,2]", ExpressionKind.Collection), ("(int).5", ExpressionKind.Literal), ("(System.FormattableString)$\"{1}\"", ExpressionKind.InterpolatedString) })
        {
            var parsed = Cs.Parse("class C { object M() => " + source + "; }");
            Assert.Empty(parsed.Diagnostics);
            var cast = Assert.Single(parsed.Root.Descendants().Select(n => n.Node).OfType<CsExpression>().Where(e => e.Kind == ExpressionKind.Cast));
            Assert.Equal(kind, Assert.Single(cast.Arguments).Kind);
        }
    }

    [Fact]
    public void Non_async_function_boundaries_keep_await_identifiers_and_invocations()
    {
        const string source = "class C { int M() { int await=1; System.Func<int> f=()=>await+1; return f()+await; } }";
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsExpression { Kind: ExpressionKind.Await });
        Assert.Contains("@await + 1", parsed.Root.Reformat().Render());
    }

    [Fact]
    public void Conditional_type_tests_do_not_consume_the_ternary_question_as_nullable()
    {
        var parsed = Cs.Parse("class C { int M(object x) => x is int ? 1 : 2; }");
        Assert.Empty(parsed.Diagnostics);
        Assert.Contains(parsed.Root.Descendants(), n => n.Node is CsExpression { Kind: ExpressionKind.Conditional });
        Assert.DoesNotContain(parsed.Root.Descendants(), n => n.Node is CsType { Form: TypeForm.Nullable });
    }

    [Fact]
    public async Task Attached_directives_survive_detachment_and_scalar_edits_with_crlf()
    {
        const string source = "class C { int M() => (\r\n#if true\r\n1\r\n#else\r\n2\r\n#endif\r\n); }\r\n";
        var parsed = Cs.Parse(source);
        var literal = parsed.Root.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Literal, Text: "1" });
        var changed = parsed.Root.Replace(literal.Path, (CsExpression)literal.Node with { Text = "3" });
        string rendered = changed.Render();
        Assert.Equal(source.Replace("\r\n1\r\n", "\r\n3\r\n"), rendered);
        Assert.Empty(changed.Validate());
        var detached = (CsDocument)changed.Detach();
        string formatted = detached.Render();
        Assert.Contains("#if true", formatted);
        Assert.Contains("#else", formatted);
        Assert.Empty(Cs.Parse(formatted).Diagnostics);
        await CompilerFixture.Run(formatted + "class Program { static void Main() {} }", "15.0");
    }

    [Fact]
    public async Task Directives_can_be_constructed_on_semantic_operands_without_source_tokens()
    {
        var value = Cs.Literal(0) with
        {
            SyntaxTrivia = new CsSyntaxTrivia[] {
            new(new CsDirective(DirectiveKind.If) { Condition = Cs.Literal(true) }),
            new(new CsDirective(DirectiveKind.EndIf), SyntaxTriviaPlacement.Trailing) }
        };
        var document = new CsDocument
        {
            Types = new[] { new CsTypeDeclaration(TypeKind.Class, "Program") { Members = new CsMember[] {
            new CsMethod(Cs.Int, "Main") { Modifiers = new[] { "static" }, Body = CsBody.FromExpression(value) } } } }
        };
        Assert.Empty(document.Validate());
        string source = document.Render();
        Assert.Empty(Cs.Parse(source).Diagnostics);
        await CompilerFixture.Run(source, "15.0");
    }

    [Fact]
    public async Task Common_symbol_tree_preserves_embedded_directives_and_comments_on_expression_statements()
    {
        const string source = "Call( /* arguments */\n#if true\n1\n#else\n2\n#endif\n);";
        var parsed = Cs.ParseTree(source, CsLanguageVersion.CSharp14);
        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(source, parsed.Root.Render());
        string formatted = parsed.Root.Reformat().Render();
        Assert.Contains("#if true", formatted);
        Assert.Contains("/* arguments */", formatted);
        Assert.Empty(Cs.ParseTree(formatted, CsLanguageVersion.CSharp14).Diagnostics);
        var detached = (CsTree)parsed.Root.Detach();
        Assert.Empty(detached.Validate());
        Assert.Equal(formatted, detached.Render());
        await CompilerFixture.Run("class Program { static void Call(int x) {} static void Main() {" + formatted + "} }", "14.0");
    }

    [Fact]
    public async Task Concurrent_rendering_is_deterministic_and_keeps_scoped_trivia_handles_private()
    {
        var document = Cs.Parse("class C { int M() => (\n#if true\n1\n#else\ninvalid \" text\n#endif\n); }").Root;
        string expected = document.Reformat().Render();
        var rendered = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => document.Reformat().Render())));
        Assert.All(rendered, text => { Assert.Equal(expected, text); Assert.DoesNotContain('\u0001', text); Assert.DoesNotContain('\u0002', text); });
    }

    [Fact]
    public void New_leading_and_trailing_directives_can_surround_existing_parsed_trivia()
    {
        var document = Cs.Parse("class C { int M() => (\n#if true\n1\n#else\n2\n#endif\n); }").Root;
        var owner = document.Descendants().Single(n => n.Node is CsExpression { Kind: ExpressionKind.Parenthesized });
        var replacement = owner.Node with
        {
            SyntaxTrivia = new(new[] {
            new CsSyntaxTrivia(new CsDirective(DirectiveKind.If) { Condition = Cs.Literal(true) }) }.Concat(owner.Node.SyntaxTrivia).Append(
            new CsSyntaxTrivia(new CsDirective(DirectiveKind.EndIf), SyntaxTriviaPlacement.Trailing)))
        };
        var changed = document.Replace(owner.Path, replacement);
        Assert.Empty(changed.Validate());
        string source = changed.Reformat().Render();
        Assert.Empty(Cs.Parse(source).Diagnostics);
        Assert.Empty(Cs.Parse(source).Root.Validate());
    }

    [Theory]
    [InlineData("// marker\n#if true\nclass C {}\n#endif")]
    [InlineData("#if true // marker\nclass C {}\n#endif")]
    [InlineData("class C { int M() => (\n// marker\n#if true\n1\n#else\n2\n#endif\n); }")]
    [InlineData("class C { int M() => (\n#if true // marker\n1\n#else\n2\n#endif\n); }")]
    public void Directive_comments_have_one_owner_and_format_idempotently(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Empty(parsed.Diagnostics);
        string formatted = parsed.Root.Reformat().Render();
        Assert.Equal(1, formatted.Split("// marker", StringSplitOptions.None).Length - 1);
        var reparsed = Cs.Parse(formatted);
        Assert.Empty(reparsed.Diagnostics);
        Assert.Equal(formatted, reparsed.Root.Reformat().Render());
    }

    [Fact]
    public void Invalid_directives_inside_interpolation_are_diagnosed_without_discarding_source()
    {
        const string source = "class C { string M() => $\"{\n#if true\n1\n#else\n2\n#endif\n}\"; }";
        var parsed = Cs.Parse(source);
        Assert.NotEmpty(parsed.Diagnostics);
        Assert.NotEqual(ParseCoverage.Typed, parsed.Coverage);
        Assert.Equal(source, parsed.Root.Render());
    }

    [Fact]
    public void Syntax_trivia_validates_content_placement_and_required_collections()
    {
        var document = new CsDocument { SyntaxTrivia = new CsSyntaxTrivia[] { new(Cs.Literal(1)) } };
        Assert.Contains(document.Validate(), d => d.Code == "GEN075");
        Assert.Contains((document with { SyntaxTrivia = new CsSyntaxTrivia[] { new(new CsDirective(DirectiveKind.EndIf), (SyntaxTriviaPlacement)99) } }).Validate(), d => d.Code == "GEN075");
        Assert.Contains((document with { SyntaxTrivia = null! }).Validate(), d => d.Code == "GEN002");
    }
}
