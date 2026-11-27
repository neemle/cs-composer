using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// Rendering contracts that builder input and single-node edits reach: preprocessor conditions,
// string styles, operand parenthesization, name-only edits and per-node reformatting.
public sealed class RenderingContractTests
{
    static string Field(CsExpression initializer) =>
        new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsField(Cs.Type("object"), "F") { Initializer = initializer } } } } }
            .Render(new() { Validate = false }).Replace("\r\n", "\n");

    [Fact]
    public void Preprocessor_conditions_render_parentheses_negation_and_operators()
    {
        var condition = Cs.Binary(CsOperator.AndAlso, new CsExpression(ExpressionKind.Parenthesized, "", new[] { Cs.Id("A") }),
            Cs.Unary(CsOperator.Not, Cs.Binary(CsOperator.Equal, Cs.Id("B"), Cs.Literal(true))));
        var document = new CsDocument { Items = new CsNode[] { new CsDirective(DirectiveKind.If) { Condition = condition }, new CsDirective(DirectiveKind.EndIf) } };
        Assert.Empty(document.Validate());
        string rendered = document.Render();
        Assert.Contains("#if ((A) && !((B == true)))", rendered);
        var reparsed = Cs.Parse(rendered);
        Assert.Empty(reparsed.Diagnostics);
    }

    [Theory]
    [InlineData(CsStringStyle.Regular, "a\"b", "\"a\\\"b\"")]
    [InlineData(CsStringStyle.Verbatim, "a\"b", "@\"a\"\"b\"")]
    public void String_literal_styles_escape_their_own_delimiters(CsStringStyle style, string text, string expected)
    {
        var literal = new CsExpression(ExpressionKind.StringLiteral, text, Values<CsExpression>.Empty) { StringStyle = style };
        Assert.Contains("object F = " + expected + ";", Field(literal));
    }

    [Fact]
    public void Post_decrement_renders_after_its_operand()
    {
        Assert.Contains("object F = i--;", Field(Cs.PostDecrement(Cs.Id("i"))));
    }

    [Fact]
    public void Low_precedence_operands_are_parenthesized_under_member_access()
    {
        var conditional = new CsExpression(ExpressionKind.Conditional, "", new[] { Cs.Id("b"), Cs.Id("x"), Cs.Id("y") });
        Assert.Contains("object F = (b ? x : y).M;", Field(conditional.Member("M")));
        var range = new CsExpression(ExpressionKind.Range, "", new[] { Cs.Id("a"), Cs.Id("b") });
        Assert.Contains("object F = (a..b).M;", Field(range.Member("M")));
        var reference = new CsExpression(ExpressionKind.Ref, "", new[] { Cs.Id("a") });
        Assert.Contains("object F = (ref a).M;", Field(reference.Member("M")));
        Assert.Contains("object F = (throw e).M;", Field(Cs.ThrowExpression(Cs.Id("e")).Member("M")));
    }

    public static IEnumerable<object[]> NameEdits()
    {
        yield return new object[] { "class C { Old F; }", (Func<CsNode, bool>)(n => n is CsType { Name: "Old" }), (Func<CsNode, CsNode>)(n => ((CsType)n) with { Name = "New" }), "class C { New F; }" };
        yield return new object[] { "class C { void M(int old) { } }", (Func<CsNode, bool>)(n => n is CsParameter), (Func<CsNode, CsNode>)(n => ((CsParameter)n) with { Name = "renamed" }), "class C { void M(int renamed) { } }" };
        yield return new object[] { "class C<Old> { }", (Func<CsNode, bool>)(n => n is TypeParameter), (Func<CsNode, CsNode>)(n => ((TypeParameter)n) with { Name = "New" }), "class C<New> { }" };
        yield return new object[] { "using Old.Space;", (Func<CsNode, bool>)(n => n is CsUsing), (Func<CsNode, CsNode>)(n => ((CsUsing)n) with { Name = "New.Space" }), "using New.Space;" };
        yield return new object[] { "class C { bool M(object o) => o is int old; }", (Func<CsNode, bool>)(n => n is CsPattern { Name: "old" }), (Func<CsNode, CsNode>)(n => ((CsPattern)n) with { Name = "renamed" }), "class C { bool M(object o) => o is int renamed; }" };
    }

    [Theory]
    [MemberData(nameof(NameEdits))]
    public void Name_only_edits_rewrite_just_the_name_token(string source, Func<CsNode, bool> select, Func<CsNode, CsNode> edit, string expected)
    {
        var document = Cs.Parse(source).Root;
        var target = document.Descendants().First(n => select(n.Node));
        Assert.Equal(expected, document.Replace(target.Path, edit(target.Node)).Render());
    }

    [Fact]
    public void Name_only_symbol_edits_rewrite_just_the_name_token()
    {
        var tree = Cs.ParseTree("var old = 1; Use(old);").Root;
        var target = tree.Descendants().First(n => n.Node is CsSymbol { Name: "old" });
        Assert.Equal("var renamed = 1; Use(old);", tree.Replace(target.Path, ((CsSymbol)target.Node) with { Name = "renamed" }).Render());
    }

    const string Rich = """
        using System;
        using Alias = System.Collections.Generic.List<int>;
        namespace N
        {
            [Obsolete("x")]
            public class C<T> : IDisposable where T : class
            {
                (int A, string B) pair;
                public int P { get; private set; }
                public void Dispose() { }
                public int M(int a)
                {
        #if DEBUG
                    a++;
        #endif
                    var q = from x in new[] { a } where x > 0 select x;
                    Func<int, int> f = (int y) => y + 1;
                    for (int i = 0, j = 1; i < j; i++) { }
                    switch (a)
                    {
                        case > 1 when a < 9:
                            break;
                        default:
                            break;
                    }
                    try { return a switch { 1 => 2, _ => 3 }; }
                    catch (Exception) { throw; }
                }
            }
        }
        """;

    // Every node kind of Rich, CsAttribute and CsCatch included (GENE-24 pass 2 fixed both reformats).
    public static IEnumerable<object[]> RichNodeKinds() =>
        Cs.Parse(Rich).Root.Descendants().Select(n => n.Node.GetType().Name).Distinct().Select(k => new object[] { k });

    [Fact]
    public void Reformatting_documentation_nodes_keeps_the_reference_and_the_declaration()
    {
        const string source = "/// <summary>See <see cref=\"M(int)\"/>.</summary>\nclass C { void M(int a) { } }";
        var document = Cs.Parse(source).Root;
        var kinds = new[] { typeof(CsComment), typeof(CsXmlNode), typeof(CsXmlAttribute), typeof(CsCref) };
        var paths = document.Descendants().Where(n => kinds.Contains(n.Node.GetType())).Select(n => n.Path).ToArray();
        Assert.Equal(kinds.Length, paths.Select(p => document.At(p).GetType()).Distinct().Count());
        foreach (var path in paths)
        {
            string rendered = document.Reformat(path).Render();
            var reparsed = Cs.Parse(rendered);
            Assert.Empty(reparsed.Diagnostics);
            Assert.Contains("cref=\"M(int)\"", rendered);
            Assert.Equal(document.Types[0] with { Comments = Values<CsComment>.Empty }, reparsed.Root.Types[0] with { Comments = Values<CsComment>.Empty });
            var cref = reparsed.Root.Descendants().Select(n => n.Node).OfType<CsCref>().Single();
            Assert.Equal("int", Assert.Single(cref.Parameters!).Type.Name);
        }
    }

    [Theory]
    [MemberData(nameof(RichNodeKinds))]
    public void Reformatting_any_single_node_keeps_the_document_semantics(string kind)
    {
        var document = Cs.Parse(Rich).Root;
        Assert.Empty(document.Validate());
        foreach (var (path, _) in document.Descendants().Where(n => n.Node.GetType().Name == kind))
        {
            string rendered = document.Reformat(path).Render();
            var reparsed = Cs.Parse(rendered);
            Assert.True(!reparsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) && document.Equals(reparsed.Root),
                "path " + string.Join(",", path.Segments) + " rendered:\n" + rendered);
        }
    }
}
