using System.Diagnostics;
using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-19 AD D10: chain spines (left-deep binary operators, member access, invocation and element access) are one
// nesting level and every pass walks them with a loop or an explicit stack. Each chain runs on a thread with a
// 1 MiB stack, where one frame per link would overflow long before 10 000 links.
[Trait("Category", "Unit")]
public sealed class DeepChainTests
{
    // GENE-29 F2: these chains are far wider than a line; width breaking is off where a test reads them on one line.
    static readonly Rendering.RenderOptions Unbroken = new() { MaxLineLength = 0 };

    static string Repeat(string text, int count, string separator = "") => string.Join(separator, Enumerable.Repeat(text, count));

    // Each shape holds `n` links; `Text` is the chain alone, as it appears in the source.
    sealed record Shape(Func<int, string> Text, Func<string, string> Source, ExpressionKind Kind);

    static readonly Dictionary<string, Shape> Shapes = new()
    {
        ["binary"] = new(n => "a" + Repeat(" + a", n), chain => "class C\n{\n    int M(int a) => " + chain + ";\n}\n", ExpressionKind.Binary),
        ["member"] = new(n => "a" + Repeat(".b", n), chain => "class C\n{\n    C b;\n    object M(C a) => " + chain + ";\n}\n", ExpressionKind.Member),
        ["invocation"] = new(n => "a" + Repeat("(1)", n), chain => "class C\n{\n    object M(D a)\n    {\n        return " + chain + ";\n    }\n}\n", ExpressionKind.Invocation),
        ["element-access"] = new(n => "a" + Repeat("[0]", n), chain => "class C\n{\n    object M(D a) => " + chain + ";\n}\n", ExpressionKind.Index),
        ["conditional-access"] = new(n => "a" + Repeat("?.b", n), chain => "class C\n{\n    object M(C a) => " + chain + ";\n}\n", ExpressionKind.Member),
        ["mixed"] = new(n => "a" + Repeat(".b(1)[2] * x?.c", n / 3), chain => "class C\n{\n    object M(C a, C x) => " + chain + ";\n}\n", ExpressionKind.Binary),
        ["fluent"] = new(n => "b" + Repeat("\n            .Add(1, \"x\") // step\n", n / 2) + "            .Build()",
            chain => "class C\n{\n    void M(B b)\n    {\n        // build\n        " + chain + ";\n    }\n}\n", ExpressionKind.Member),
    };

    public static TheoryData<string, int> Chains() => new()
    {
        { "binary", 10_000 }, { "binary", 100_000 }, { "member", 10_000 }, { "member", 100_000 },
        { "invocation", 10_000 }, { "invocation", 100_000 }, { "element-access", 10_000 }, { "element-access", 100_000 },
        { "conditional-access", 10_000 }, { "mixed", 10_000 }, { "fluent", 10_000 },
    };

    internal static void OnSmallStack(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }, 1 << 20);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Xunit.Sdk.XunitException("Failed on a 1 MiB stack: " + failure);
    }

    [Theory]
    [MemberData(nameof(Chains))]
    public void Chain_parses_typed_validates_renders_and_reformats(string name, int links)
    {
        var shape = Shapes[name];
        string source = shape.Source(shape.Text(links));
        OnSmallStack(() =>
        {
            var parsed = Cs.Parse(Encoding.UTF8.GetBytes(source));
            Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
            Assert.Empty(parsed.Diagnostics);
            Assert.Empty(parsed.Root.Validate());
            Assert.Equal(source, parsed.Root.Render());
            Assert.Equal(Encoding.UTF8.GetBytes(source), parsed.Root.ToBytes());
            Assert.True(NestingLimitTests.Depth(parsed.Root) <= 128);
            Assert.True(parsed.Root.ToString().Length > 0);

            // Reformat without width breaking writes the chain on one line; it re-parses typed and is stable.
            string reformatted = parsed.Root.Reformat(Unbroken).Render();
            var again = Cs.Parse(reformatted);
            Assert.Equal(ParseCoverage.Typed, again.Coverage);
            Assert.Equal(reformatted, again.Root.Reformat(Unbroken).Render());
            if (name != "fluent") Assert.Contains(shape.Text(links), reformatted, StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(Chains))]
    public void Chain_compares_finds_and_edits_a_link_deep_inside(string name, int links)
    {
        var shape = Shapes[name];
        string source = shape.Source(shape.Text(links));
        OnSmallStack(() =>
        {
            var first = Cs.Parse(source).Root;
            var second = Cs.Parse(source).Root;
            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal(first, first.Detach());

            // Find: the link halfway down the chain, by path, through Descendants.
            var chainLinks = first.Descendants().Where(x => x.Node is CsExpression e && e.Kind == shape.Kind).ToList();
            Assert.True(chainLinks.Count >= links / 3);
            var (path, node) = chainLinks[chainLinks.Count / 2];
            Assert.True(path.Segments.Count > 128);
            Assert.Same(node, first.At(path));

            // Replace a link deep inside: only its operator, name or argument changes in the output.
            var link = (CsExpression)node;
            var (replacement, before, after) = link.Kind switch
            {
                // GENE-29 F3: an operator of the same precedence, so the edit keeps the chain's grouping (a `-` inside a
                // `*` chain would need parentheses the operator-only edit does not write, which the meaning guard refuses).
                ExpressionKind.Binary => link.Operator == CsOperator.Multiply
                    ? (link with { Operator = CsOperator.Divide }, "*", "/") : (link with { Operator = CsOperator.Subtract }, "+", "-"),
                ExpressionKind.Member => (link with { Text = "renamed" }, link.Text, "renamed"),
                _ => (link with { Arguments = new(link.Arguments.Take(1).Append(link.Arguments[1] with { Text = "7" })) }, link.Arguments[1].Text, "7")
            };
            var edited = first.Replace(path, replacement);
            string rendered = edited.Render();
            int at = FirstDifference(source, rendered);
            Assert.True(at > 0 && at < source.Length);
            Assert.Equal(source.Remove(at, before.Length).Insert(at, after), rendered);
            Assert.Same(replacement, edited.At(new NodePath(path.Segments)));
            Assert.NotEqual(first, edited);
            Assert.Equal(ParseCoverage.Typed, Cs.Parse(rendered).Coverage);
        });
    }

    static int FirstDifference(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return i;
    }

    // Top-level statements become the symbol model (Symbol -> Options -> receiver per link); Cs.ParseTree too.
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void Top_level_and_tree_chains_convert_to_the_symbol_model_iteratively(int links)
    {
        string source = "var x = a" + Repeat(".B(1)", links) + ";\nx = x" + Repeat(" + 1", links) + ";\n";
        OnSmallStack(() =>
        {
            // Rendering validates first (symbol shape, lowering and the semantic walk), so it covers Validate.
            var parsed = Cs.Parse(source);
            Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
            Assert.Equal(source, parsed.Root.Render());
            Assert.True(parsed.Root.Descendants().Count() > 2 * links);
            var tree = Cs.ParseTree(source);
            Assert.Equal(ParseCoverage.Typed, tree.Coverage);
            Assert.Equal(source, tree.Root.Render());
            var deep = tree.Root.Descendants().Where(x => x.Node is CsSymbol { Name: "B" }).Skip(links / 2).First();
            Assert.Same(deep.Node, tree.Root.At(deep.Path));
            if (links > 10_000) return; // the other passes repeat the same walks; 10 000 links keep the suite fast

            Assert.Empty(parsed.Root.Validate());
            Assert.Empty(tree.Root.Validate());
            Assert.Equal(parsed.Root, Cs.Parse(source).Root);
            Assert.Equal(parsed.Root.GetHashCode(), Cs.Parse(source).Root.GetHashCode());
            Assert.Equal(tree.Root, Cs.ParseTree(source).Root);
            Assert.True(parsed.Root.ToString().Length > 0);
            Assert.True(tree.Root.ToString().Length > 0);
            Assert.Contains(".B(1).B(1)", parsed.Root.Reformat(Unbroken).Render(), StringComparison.Ordinal);
        });
    }

    // A constructed chain of any length passes GEN001, renders and compares without a frame per link.
    [Fact]
    public void Built_chains_validate_render_and_compare_iteratively()
    {
        OnSmallStack(() =>
        {
            CsExpression member = Cs.Id("a"), sum = Cs.Id("a");
            for (int i = 0; i < 100_000; i++)
            {
                member = member.Member("b").Call(Cs.Literal(i % 10));
                sum = Cs.Binary(CsOperator.Add, sum, Cs.Literal(1));
            }
            var document = InMethod(Cs.Return(member), Cs.Return(sum));
            Assert.Empty(document.Validate());
            string text = document.Render(Unbroken);
            Assert.Contains("a" + string.Concat(Enumerable.Range(0, 100_000).Select(i => ".b(" + (i % 10) + ")")) + ";", text, StringComparison.Ordinal);
            Assert.Contains("a" + Repeat(" + 1", 100_000) + ";", text, StringComparison.Ordinal);
            var copy = InMethod(Cs.Return(member with { }), Cs.Return(sum with { }));
            Assert.Equal(document, copy);
            Assert.Equal(document.GetHashCode(), copy.GetHashCode());
            Assert.NotEqual(document, InMethod(Cs.Return(member), Cs.Return(sum with { Operator = CsOperator.Subtract })));
        });
    }

    static CsDocument InMethod(params CsStatement[] statements) => new()
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsMethod(Cs.Type("object"), "M") { Body = Cs.Body(statements) } } } }
    };

    const string Times = "\u00D7";

    // AD D10 supervisor ruling 1: a run of more than 128 receiver steps is written once in the path.
    [Fact]
    public void Diagnostic_paths_compress_only_runs_longer_than_the_limit()
    {
        static IReadOnlyList<GenDiagnostic> Errors(int links)
        {
            CsExpression chain = Cs.Id("1bad");
            for (int i = 0; i < links; i++) chain = chain.Member("b");
            return InMethod(Cs.Return(chain)).Validate();
        }
        string prefix = "Types[0].Members[0].Body.Statements[0].Expression";
        // 129 links are 128 receiver steps from the top link to the last one, then one step to the head.
        Assert.Equal(prefix + Repeat(".Arguments[0]", 129) + ".Text", Assert.Single(Errors(129)).Path);
        Assert.Equal(prefix + ".Arguments[0]" + Times + "129.Arguments[0].Text", Assert.Single(Errors(130)).Path);
        Assert.Equal(prefix + ".Arguments[0]" + Times + "99999.Arguments[0].Text", Assert.Single(Errors(100_000)).Path);

        // The graph walk writes the same run in its own path, so a broken node deep in a long chain is located.
        static GenDiagnostic Graph(int links)
        {
            CsExpression broken = Cs.Id("a") with { Comments = new[] { new CsComment("// c") { Comments = new[] { new CsComment("// d") } } } };
            for (int i = 0; i < links; i++) broken = broken.Member("b");
            var error = Assert.Single(InMethod(Cs.Return(broken)).Validate());
            Assert.Equal("GEN075", error.Code);
            Assert.StartsWith("Root.", error.Path, StringComparison.Ordinal);
            return error;
        }
        Assert.Contains(".Arguments[0]" + Times + "999.Children[", Graph(1000).Path, StringComparison.Ordinal);
        Assert.Contains(".Arguments[0]" + Times + "129.Children[", Graph(130).Path, StringComparison.Ordinal);
        string uncompressed = Graph(129).Path;
        Assert.DoesNotContain(Times, uncompressed, StringComparison.Ordinal);
        Assert.Equal(128, uncompressed.Split(".Children[").Length - Graph(1).Path.Split(".Children[").Length);
    }

    // AD D10 supervisor ruling 2: the 128 cap counts a run of spine steps as one segment.
    [Fact]
    public void Path_cap_counts_a_spine_run_as_one_segment()
    {
        var parsed = Cs.Parse(Shapes["member"].Source(Shapes["member"].Text(1000))).Root;
        var deepest = parsed.Descendants().Last(x => x.Node is CsExpression { Kind: ExpressionKind.Identifier });
        Assert.True(deepest.Path.Segments.Count > 1000);
        Assert.Same(deepest.Node, parsed.At(deepest.Path));
        var tooLong = Assert.Throws<ArgumentException>(() => parsed.At(new NodePath(Enumerable.Repeat(0, 200).ToArray())));
        Assert.StartsWith("Path exceeds128.", tooLong.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => parsed.At(new NodePath(deepest.Path.Segments.Append(0).ToArray())));
    }

    // True nesting keeps the limit: a chain inside 120 parentheses is typed, 130 parentheses are not.
    [Theory]
    [InlineData(120, ParseCoverage.Typed)]
    [InlineData(130, ParseCoverage.Partial)]
    public void True_nesting_around_a_long_chain_keeps_the_limit(int parentheses, ParseCoverage coverage)
    {
        string source = "class C { object M(C a) => " + Repeat("(", parentheses) + "a" + Repeat(".b", 10_000) + Repeat(")", parentheses) + "; }";
        OnSmallStack(() =>
        {
            var parsed = Cs.Parse(source);
            Assert.Equal(coverage, parsed.Coverage);
            if (coverage != ParseCoverage.Typed)
                Assert.Equal(("PARSE002", "Nesting exceeds 128."), (Assert.Single(parsed.Diagnostics).Code, parsed.Diagnostics[0].Message));
            Assert.Equal(source, parsed.Root.Render());
            Assert.DoesNotContain(parsed.Root.Validate(), d => d.Code == "GEN001");
        });
        foreach (string nested in new[] { Repeat("() => ", 70) + "a" + Repeat(".b", 1000), Repeat("x => { return ", 50) + "a" + Repeat(".b", 1000) + Repeat("; }", 50) })
            Assert.Equal(ParseCoverage.Partial, Cs.Parse("class C { object M(C a) => " + nested + "; }").Coverage);
    }

    // A link's conditional access is part of link equality: `a?.b` and `a.b` differ, at the top of a chain and deep
    // inside one, and an edit that toggles it renders the toggled operator instead of the unchanged original.
    [Theory]
    [InlineData(1)]
    [InlineData(1_000)]
    public void Conditional_access_is_part_of_link_equality_and_rendering(int links)
    {
        static string Source(string chain) => "class C\n{\n    object M(C a) => " + chain + ";\n}\n";
        string prefix = "a" + Repeat(".b", links), suffix = Repeat(".b", links);
        string plain = Source(prefix + ".c" + suffix), conditional = Source(prefix + "?.c" + suffix);
        OnSmallStack(() =>
        {
            var left = Cs.Parse(plain).Root;
            var right = Cs.Parse(conditional).Root;
            var (path, node) = left.Descendants().Single(x => x.Node is CsExpression { Kind: ExpressionKind.Member, Text: "c" });
            var link = (CsExpression)node;
            Assert.False(link.ConditionalAccess);
            var toggledOn = left.Replace(path, link with { ConditionalAccess = true });
            Assert.Equal(conditional, toggledOn.Render());
            Assert.Equal(right, toggledOn);

            var (back, on) = right.Descendants().Single(x => x.Node is CsExpression { Kind: ExpressionKind.Member, Text: "c" });
            Assert.True(((CsExpression)on).ConditionalAccess);
            var toggledOff = right.Replace(back, (CsExpression)on with { ConditionalAccess = false });
            Assert.Equal(plain, toggledOff.Render());
            Assert.Equal(left, toggledOff);

            Assert.NotEqual(left, right);
            Assert.NotEqual(right, left);
            Assert.False(Cs.Id("a").Member("c").Equals(Cs.Id("a").Member("c") with { ConditionalAccess = true }));
        });
    }

    // Reformat(path) on a link rewrites that link's subtree only; the links above it keep their source text.
    [Theory]
    [InlineData(3)]
    [InlineData(10_000)]
    public void Reformat_of_a_deep_link_rewrites_only_its_subtree(int links)
    {
        static string Source(string chain) => "class C\n{\n    int M(int a) => " + chain + ";\n}\n";
        string source = Source("a" + Repeat("  +  a", links));
        int above = links / 2;
        OnSmallStack(() =>
        {
            var parsed = Cs.Parse(source).Root;
            var deep = parsed.Descendants().Where(x => x.Node is CsExpression { Kind: ExpressionKind.Binary }).Skip(above).First();
            Assert.Equal(Source("a" + Repeat(" + a", links - above) + Repeat("  +  a", above)), parsed.Reformat(deep.Path).Render());
            Assert.Equal(source, parsed.Render());
        });
    }

    // Render after Reformat(path) on a link deep inside a chain: whether each link holds formatting is decided
    // bottom-up once, so the links above it do not each walk the subtree below them (AD D10: N links cost O(N)).
    internal static TimeSpan ReformatDeepLinkAndRender(string name, int links)
    {
        var shape = Shapes[name];
        string source = shape.Source(shape.Text(links));
        var elapsed = TimeSpan.Zero;
        OnSmallStack(() =>
        {
            var parsed = Cs.Parse(source).Root;
            var deep = parsed.Descendants().Where(x => x.Node is CsExpression e && e.Kind == shape.Kind).Skip(links / 2).First();
            var watch = Stopwatch.StartNew();
            var reformatted = parsed.Reformat(deep.Path, Unbroken);
            string rendered = reformatted.Render();
            elapsed = watch.Elapsed;
            Assert.Equal(source, rendered);
        });
        return elapsed;
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("member")]
    [InlineData("invocation")]
    [InlineData("element-access")]
    public void Reformat_of_a_deep_link_renders_in_linear_time(string name)
    {
        ReformatDeepLinkAndRender(name, 1_000); // warm-up
        var small = ReformatDeepLinkAndRender(name, 10_000);
        var large = ReformatDeepLinkAndRender(name, 100_000);
        // Replace and Render each validate the whole document first, which is most of the time; quadratic took minutes.
        Assert.True(large < TimeSpan.FromSeconds(10), "100 000 links took " + large);
        // Ten times the links: linear is about 10x, quadratic about 100x. The floor keeps timer noise out.
        Assert.True(large.TotalMilliseconds < 30 * Math.Max(small.TotalMilliseconds, 20), "10 000 links took " + small + ", 100 000 took " + large);
    }

    // Perf sanity: a 100 000-link chain parses, validates and renders in seconds, in linear time.
    [Fact]
    public void Hundred_thousand_link_chain_takes_seconds()
    {
        string source = Shapes["binary"].Source(Shapes["binary"].Text(100_000));
        var watch = Stopwatch.StartNew();
        OnSmallStack(() =>
        {
            var parsed = Cs.Parse(source);
            Assert.Empty(parsed.Root.Validate());
            Assert.Equal(source, parsed.Root.Render());
        });
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "100 000 links took " + watch.Elapsed);
    }
}
