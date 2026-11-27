using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-19: one nesting limit (128 Children edges from the root), enforced while parsing; a typed parse never
// throws on untouched emit; no input overflows the stack; parsing can be cancelled. Chain spines (left-deep
// binary operators, member access, invocation and element access) count as one level (AD D10); their
// any-length behaviour is covered in DeepChainTests.
public sealed class NestingLimitTests
{
    static string Repeat(string text, int count, string separator = "") => string.Join(separator, Enumerable.Repeat(text, count));

    static readonly Dictionary<string, Func<int, string>> Shapes = new()
    {
        ["binary"] = n => "class C { int M(int a) => " + Repeat("a", n, " + ") + "; }",
        ["binary-block"] = n => "class C { int M(int a) { return " + Repeat("a", n, " + ") + "; } }",
        ["member"] = n => "class C { object M(C a) => a" + Repeat(".b", n) + "; C b; }",
        ["invocation"] = n => "class C { object M(C a) => a" + Repeat("()", n) + "; }",
        ["indexer"] = n => "class C { object M(C a) => a" + Repeat("[0]", n) + "; }",
        ["unary"] = n => "class C { int M(int a) => " + Repeat("~", n) + "a; }",
        ["parens"] = n => "class C { int M(int a) => " + Repeat("(", n) + "a" + Repeat(")", n) + "; }",
        ["ternary"] = n => "class C { int M(bool b) => " + Repeat("b ? 1 : ", n) + "0; }",
        ["assignment"] = n => "class C { void M(int a) { " + Repeat("a = ", n) + "1; } }",
        ["expression-lambda"] = n => "class C { object M() => " + Repeat("() => ", n) + "1; }",
        ["block-lambda"] = n => "class C { object M() { return " + Repeat("() => { return ", n) + "1" + Repeat("; }", n) + "; } }",
        ["anonymous-method"] = n => "class C { object M() { return " + Repeat("delegate { return ", n) + "1" + Repeat("; }", n) + "; } }",
        ["lambda-argument"] = n => "class C { void M() { " + Repeat("F(() => { ", n) + Repeat("}); ", n) + "} }",
        ["local-function"] = n => "class C { void M() { " + Repeat("void L() { ", n) + Repeat("} ", n) + "} }",
        ["blocks"] = n => "class C { void M() { " + Repeat("{ ", n) + Repeat("} ", n) + "} }",
        ["if"] = n => "class C { void M(bool b) { " + Repeat("if (b) ", n) + "; } }",
        ["namespaces"] = n => Repeat("namespace N { ", n) + Repeat("} ", n),
        ["types"] = n => Repeat("class C { ", n) + Repeat("} ", n),
        ["generics"] = n => "class C { " + Repeat("G<", n) + "int" + Repeat(">", n) + " f; }",
        ["interpolation"] = n => "class C { string M() => " + Repeat("$\"{", n) + "1" + Repeat("}\"", n) + "; }",
        ["object-initializer"] = n => "class C { object M() => " + Repeat("new C { P = ", n) + "1" + Repeat(" }", n) + "; }",
        ["switch-expression"] = n => "class C { int M(int a) => " + Repeat("a switch { _ => ", n) + "1" + Repeat(" }", n) + "; }",
        ["top-level"] = n => "var x = a" + Repeat(".b", n) + ";",
        ["or-pattern"] = n => "class C { bool M(int a) => a is " + Repeat("1", n, " or ") + "; }",
        ["arrays"] = n => "class C { int" + Repeat("[]", n) + " f; }",
    };

    // Shapes whose growth is a chain spine: they stay typed at any length (AD D10).
    internal static readonly string[] ChainShapes = ["binary", "binary-block", "member", "invocation", "indexer", "top-level"];

    // Last count that is still typed, and the node depth that count reaches: the next level always exceeds 128.
    public static TheoryData<string, int, int> Boundaries() => new()
    {
        { "unary", 124, 128 }, { "parens", 124, 128 }, { "ternary", 124, 128 },
        { "assignment", 123, 128 }, { "expression-lambda", 62, 128 }, { "block-lambda", 41, 128 },
        { "anonymous-method", 41, 128 }, { "lambda-argument", 31, 127 }, { "local-function", 41, 126 },
        { "blocks", 62, 127 }, { "if", 62, 128 }, { "namespaces", 128, 128 }, { "types", 128, 128 },
        { "generics", 125, 128 }, { "interpolation", 62, 128 }, { "object-initializer", 41, 127 },
        { "switch-expression", 62, 128 }, { "or-pattern", 123, 128 }, { "arrays", 125, 128 },
    };

    static bool Link(CsNode? node) => node is CsExpression { Kind: ExpressionKind.Binary or ExpressionKind.Member or ExpressionKind.Invocation or ExpressionKind.Index };

    // The receiver a symbol link continues along, as the symbol model lowers it.
    static CsNode? SymbolReceiver(CsSymbolOptions o)
    {
        if (o.Operation != SymbolOperation.None || o.Await || o.AsCompound || o.Using || o.AsVar || o.Type != null) return null;
        if (o.AsIndex || o.AsCall) return o.Target;
        if (o.AsCast || o.AsTypeOf || o.AsDefault || o.AsIs || o.AsConditional || o.AsArray || o.AsInitializer || o.AsParenthesized || o.AsNew || o.AsAssignment) return null;
        if (o.Operator != CsOperator.None) return o.Parts.Count == 2 && o.Operator is not (CsOperator.Not or CsOperator.Complement) ? o.Parts[0] : null;
        return o.Value == null ? o.Target : null;
    }

    static bool SymbolLink(CsNode? node) => Link(node) || node is CsSymbol { Options: { } o } && SymbolReceiver(o) != null;

    // Whether the edge from `parent` to `child` is a chain spine edge (AD D10): it adds no level.
    internal static bool SpineEdge(CsNode parent, CsNode child) => parent switch
    {
        CsExpression e => Link(e) && e.Arguments.Count > 0 && ReferenceEquals(e.Arguments[0], child) && Link(child),
        CsSymbol s => ReferenceEquals(s.Options, child) && SymbolReceiver(s.Options) is { } receiver && SymbolLink(receiver),
        CsSymbolOptions o => SymbolReceiver(o) is { } receiver && ReferenceEquals(receiver, child) && SymbolLink(child),
        _ => false
    };

    internal static int Depth(CsNode root)
    {
        int max = 0;
        var stack = new Stack<(CsNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count != 0)
        {
            var (node, depth) = stack.Pop();
            max = Math.Max(max, depth);
            foreach (var child in node.Children) stack.Push((child, SpineEdge(node, child) ? depth : depth + 1));
        }
        return max;
    }

    // Nested interpolated strings beyond the lexer's own nesting limit (two levels per string) end in one
    // unterminated literal token, reported as malformed (PARSE001), as before GENE-19; they still round-trip.
    static void AssertHonest(string source, ParseResult<CsDocument> parsed, bool lexical = false)
    {
        Assert.Equal(Encoding.UTF8.GetBytes(source), parsed.Root.ToBytes());
        Assert.DoesNotContain(parsed.Root.Validate(), d => d.Code == "GEN001");
        Assert.True(Depth(parsed.Root) <= 128);
        if (parsed.Coverage == ParseCoverage.Typed) Assert.Empty(parsed.Diagnostics);
        else if (!lexical) Assert.All(parsed.Diagnostics, d => Assert.Equal(("PARSE002", DiagnosticSeverity.Warning), (d.Code, d.Severity)));
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void Parse_is_typed_exactly_up_to_the_limit_and_raw_one_level_past_it(string shape, int count, int depth)
    {
        string within = Shapes[shape](count), beyond = Shapes[shape](count + 1);
        var typed = Cs.Parse(within);
        Assert.Equal(ParseCoverage.Typed, typed.Coverage);
        Assert.Equal(depth, Depth(typed.Root));
        AssertHonest(within, typed);
        var partial = Cs.Parse(beyond);
        Assert.Equal(ParseCoverage.Partial, partial.Coverage);
        AssertHonest(beyond, partial);
        foreach (int n in new[] { count - 1, 130, 10_000 }) AssertHonest(Shapes[shape](n), Cs.Parse(Shapes[shape](n)), shape == "interpolation" && n > count);
    }

    public static TheoryData<string, int> ChainLengths() => new()
    {
        { "binary", 130 }, { "binary-block", 130 }, { "member", 130 }, { "invocation", 130 }, { "indexer", 130 }, { "top-level", 130 },
        { "binary", 10_000 }, { "binary-block", 10_000 }, { "member", 10_000 }, { "invocation", 10_000 }, { "indexer", 10_000 }, { "top-level", 10_000 },
    };

    // AD D10: the shapes that were raw one link past the limit are typed at any length.
    [Theory]
    [MemberData(nameof(ChainLengths))]
    public void Chain_shapes_stay_typed_past_the_limit(string shape, int count)
    {
        string source = Shapes[shape](count);
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        AssertHonest(source, parsed);
        Assert.Empty(parsed.Root.Validate());
    }

    [Theory]
    [InlineData("class C { string M(int a) => ", "; }", 130)]
    [InlineData("class C { string M(int a) => ", "; }", 100_000)]
    public void Over_deep_expression_body_keeps_its_member_raw(string head, string tail, int levels)
    {
        string source = head + Repeat("(", levels) + "a" + Repeat(")", levels) + tail;
        var parsed = Cs.Parse(source);
        var type = Assert.IsType<CsTypeDeclaration>(Assert.Single(parsed.Root.Items));
        var raw = Assert.IsType<CsRawMember>(Assert.Single(type.Members));
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal(("PARSE002", DiagnosticSeverity.Warning, "Nesting exceeds 128."), (diagnostic.Code, diagnostic.Severity, diagnostic.Message));
        Assert.Equal((10, source.Length - 12), (diagnostic.Offset, diagnostic.Length));
        Assert.Equal(source[10..^2], raw.Source);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        Assert.Equal(source, parsed.Root.Render());
    }

    public static TheoryData<int, string> DeepGenericCalls()
    {
        var rows = new TheoryData<int, string>();
        for (int levels = 124; levels <= 130; levels++) { rows.Add(levels, ""); rows.Add(levels, ".b"); }
        return rows;
    }

    // GENE-19 D10 regression: a generic argument list too deep to read as types is not re-read as a typed chain of
    // comparisons. Whether that reading parses and validation rejects it (`int`) or it fails to parse (`int?`), only
    // its member, statement or top-level statement is raw, with the nesting warning; siblings stay typed.
    [Theory]
    [MemberData(nameof(DeepGenericCalls))]
    public void Over_deep_generic_arguments_in_a_call_keep_only_their_member_raw(int levels, string tail)
    {
        foreach (string argument in new[] { "int", "int?" })
        {
            string member = "object M(C a) => a.F<" + Repeat("L<", levels) + argument + Repeat(">", levels) + ">(1)" + tail + ";";
            string source = "class C { int f; " + member + " void N() { } }";
            var parsed = Cs.Parse(source);
            Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
            var type = Assert.IsType<CsTypeDeclaration>(Assert.Single(parsed.Root.Items));
            Assert.Equal(new[] { typeof(CsField), typeof(CsRawMember), typeof(CsMethod) }, type.Members.Select(m => m.GetType()));
            Assert.Equal(member, ((CsRawMember)type.Members[1]).Source);
            var diagnostic = Assert.Single(parsed.Diagnostics);
            Assert.Equal(("PARSE002", DiagnosticSeverity.Warning, "Nesting exceeds 128."), (diagnostic.Code, diagnostic.Severity, diagnostic.Message));
            Assert.Equal((17, member.Length), (diagnostic.Offset, diagnostic.Length));
            Assert.Equal(source, parsed.Root.Render());
            Assert.Empty(parsed.Root.Validate());

            // In a block body only the statement is raw; at the top level only that statement is.
            string block = "class C { int f; object M(C a) { int b = 1; return a.F<" + Repeat("L<", levels) + argument + Repeat(">", levels) + ">(1)" + tail + "; } }";
            var inBlock = Cs.Parse(block);
            Assert.Equal(ParseCoverage.Partial, inBlock.Coverage);
            var body = ((CsMethod)((CsTypeDeclaration)inBlock.Root.Items[0]).Members[1]).Body!;
            Assert.Equal(new[] { StatementKind.Local, StatementKind.Raw }, body.Statements.Select(s => s.Kind));
            Assert.Equal(("PARSE002", "Nesting exceeds 128.", block.IndexOf("return", StringComparison.Ordinal)), (Assert.Single(inBlock.Diagnostics).Code, inBlock.Diagnostics[0].Message, inBlock.Diagnostics[0].Offset));
            Assert.Equal(block, inBlock.Root.Render());

            string program = "var x = 1;\nvar y = a.F<" + Repeat("L<", levels) + argument + Repeat(">", levels) + ">(1)" + tail + ";\n";
            var topLevel = Cs.Parse(program);
            Assert.Equal(ParseCoverage.Partial, topLevel.Coverage);
            Assert.IsType<CsGlobalStatement>(topLevel.Root.Items[0]);
            Assert.IsType<CsRaw>(topLevel.Root.Items[1]);
            Assert.Equal(("PARSE002", "Nesting exceeds 128.", program.IndexOf("var y", StringComparison.Ordinal)), (Assert.Single(topLevel.Diagnostics).Code, topLevel.Diagnostics[0].Message, topLevel.Diagnostics[0].Offset));
            Assert.Equal(program, topLevel.Root.Render());
        }
    }

    [Fact]
    public void Over_deep_statement_keeps_only_that_statement_raw_and_siblings_stay_editable()
    {
        string source = "class C\n{\n    int M(int a)\n    {\n        int b = 1;\n        return " + Repeat("(", 130) + "a" + Repeat(")", 130) +
            ";\n    }\n    int N() => 2;\n}\n";
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        var type = (CsTypeDeclaration)parsed.Root.Items[0];
        var method = Assert.IsType<CsMethod>(type.Members[0]);
        Assert.Equal(StatementKind.Local, method.Body!.Statements[0].Kind);
        Assert.Equal(StatementKind.Raw, method.Body.Statements[1].Kind);
        Assert.Equal(source.IndexOf("return", StringComparison.Ordinal), Assert.Single(parsed.Diagnostics).Offset);
        Assert.Equal(source, parsed.Root.Render());
        var sibling = parsed.Root.Descendants().Single(x => x.Node is CsMethod { Name: "N" });
        var edited = parsed.Root.Replace(sibling.Path, (CsMethod)sibling.Node with { Name = "Renamed" });
        Assert.Equal(source.Replace("int N()", "int Renamed()", StringComparison.Ordinal), edited.Render());
    }

    [Fact]
    public void Depth_fallback_reports_the_same_warning_at_every_site()
    {
        var namespaces = Cs.Parse(Shapes["namespaces"](10_000));
        var site = Assert.Single(namespaces.Diagnostics);
        Assert.Equal(("PARSE002", DiagnosticSeverity.Warning), (site.Code, site.Severity));
        Assert.True(site.Offset > 0);
        Assert.Contains(namespaces.Root.Descendants(), x => x.Node is CsRaw);

        string calls = Repeat("F(", 200) + "a" + Repeat(")", 200);
        foreach (var kind in new[] { ParseKind.Expression, ParseKind.Statements, ParseKind.Member })
        {
            string text = kind switch { ParseKind.Expression => calls, ParseKind.Statements => calls + ";", _ => "object M() => " + calls + ";" };
            var fragment = new CsRaw(text).Parse(new ParseContext(kind));
            Assert.Equal(ParseCoverage.Opaque, fragment.Coverage);
            var diagnostic = Assert.Single(fragment.Diagnostics);
            Assert.Equal(("PARSE002", DiagnosticSeverity.Warning, 0, text.Length), (diagnostic.Code, diagnostic.Severity, diagnostic.Offset, diagnostic.Length));
        }
        var generic = new CsRaw("F<" + Repeat("G<", 200) + "int" + Repeat(">", 200) + ">()").Parse(new ParseContext(ParseKind.Expression));
        Assert.Equal("PARSE002", Assert.Single(generic.Diagnostics).Code);

        // The symbol model is deeper than the statement model: nesting that fits as a statement is too deep as a tree.
        string statement = "var x = " + Repeat("F(", 60) + "a" + Repeat(")", 60) + ";";
        Assert.Equal(ParseCoverage.Typed, new CsRaw(statement).Parse(new ParseContext(ParseKind.Statements)).Coverage);
        var tree = Cs.ParseTree(statement);
        Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
        Assert.Equal(("PARSE002", DiagnosticSeverity.Warning), (Assert.Single(tree.Diagnostics).Code, tree.Diagnostics[0].Severity));
        Assert.Equal(statement, Assert.IsType<CsRaw>(Assert.Single(tree.Root.Items)).Text);
    }

    public static TheoryData<ParseKind, int, string, bool> DeepGenericFragments()
    {
        // Typed while the real reading fits: `int?` is one level deeper, and a statement or member one level deeper
        // than an expression; past that the fragment is raw, whatever reading of the tokens would still parse.
        var rows = new TheoryData<ParseKind, int, string, bool>();
        foreach (var kind in new[] { ParseKind.Expression, ParseKind.Statements, ParseKind.Member })
            foreach (int levels in new[] { 124, 125, 126, 128, 130, 1_000 })
                foreach (string argument in new[] { "int", "int?" })
                    rows.Add(kind, levels, argument, levels <= 124 || levels == 125 && (argument == "int" || kind == ParseKind.Expression) ||
                        levels == 126 && kind == ParseKind.Expression);
        return rows;
    }

    static IEnumerable<CsNode> Nodes(CsNode root)
    {
        var stack = new Stack<CsNode>([root]);
        while (stack.Count != 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children) stack.Push(child);
        }
    }

    // GENE-19 D10 regression: a fragment has no recovery site, so one whose generic argument list was too deep to read
    // as types is kept whole as raw source with the nesting warning; it is never a typed chain of comparisons in which
    // `int` is a name. The same holds for the symbol tree, which is parsed as a statements fragment.
    [Theory]
    [MemberData(nameof(DeepGenericFragments))]
    public void Over_deep_generic_arguments_in_a_fragment_keep_it_raw(ParseKind kind, int levels, string argument, bool typed)
    {
        string call = "a.F<" + Repeat("L<", levels) + argument + Repeat(">", levels) + ">(1)";
        string text = kind switch { ParseKind.Expression => call, ParseKind.Statements => call + ";", _ => "object M(C a) => " + call + ";" };
        var fragment = new CsRaw(text).Parse(new ParseContext(kind));
        if (typed)
        {
            Assert.Equal(ParseCoverage.Typed, fragment.Coverage);
            Assert.Empty(fragment.Diagnostics);
            Assert.Contains(Nodes(fragment.Root), n => n is CsType { Name: "int" });
            Assert.DoesNotContain(Nodes(fragment.Root), n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "int" });
            return;
        }
        Assert.Equal(ParseCoverage.Opaque, fragment.Coverage);
        Assert.Equal(text, Assert.IsType<CsRaw>(fragment.Root).Text);
        var diagnostic = Assert.Single(fragment.Diagnostics);
        Assert.Equal(("PARSE002", DiagnosticSeverity.Warning, "Nesting exceeds 128.", 0, text.Length),
            (diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Offset, diagnostic.Length));
        if (kind != ParseKind.Statements) return;
        var tree = Cs.ParseTree(text);
        Assert.Equal(ParseCoverage.Opaque, tree.Coverage);
        Assert.Equal(text, Assert.IsType<CsRaw>(Assert.Single(tree.Root.Items)).Text);
        var treeDiagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal(("PARSE002", "Nesting exceeds 128.", 0, text.Length), (treeDiagnostic.Code, treeDiagnostic.Message, treeDiagnostic.Offset, treeDiagnostic.Length));
    }

    [Fact]
    public void Embedded_directive_past_the_limit_falls_back_to_one_opaque_raw_item()
    {
        string source = "class C { int M(int a) => " + Repeat("(", 124) + "a\n#if X\n#endif\n" + Repeat(")", 124) + "; }";
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Opaque, parsed.Coverage);
        Assert.IsType<CsRaw>(Assert.Single(parsed.Root.Items));
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal(("PARSE002", DiagnosticSeverity.Warning, 0, source.Length), (diagnostic.Code, diagnostic.Severity, diagnostic.Offset, diagnostic.Length));
        Assert.Equal(source, parsed.Root.Render());
    }

    [Fact]
    public void Deep_preprocessor_condition_and_cref_do_not_escape_as_failures()
    {
        string source = "#if " + Repeat("(", 200) + "X" + Repeat(")", 200) + "\n#endif\n/// <see cref=\"" + Repeat("G{", 200) + "int" +
            Repeat("}", 200) + "\"/>\nclass C { }\n";
        var parsed = Cs.Parse(source);
        Assert.Equal(source, Encoding.UTF8.GetString(parsed.Root.ToBytes()));
    }

    // AC-6: every hostile input completes on a 1 MiB stack, with a result and untouched emit.
    public static TheoryData<string> Hostile() => new()
    {
        "parens 130", "parens 10000", "block-lambda 10000",
        "anonymous-method 10000", "lambda-argument 10000", "blocks 10000", "namespaces 10000", "expression-lambda 10000",
        "generics 10000", "interpolation 10000", "object-initializer 10000", "switch-expression 10000", "or-pattern 10000",
        "arrays 10000", "if 10000", "local-function 10000", "unary 10000", "ternary 10000", "assignment 10000",
        "types 10000",
    };

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Hostile_input_never_overflows_a_one_megabyte_stack(string input)
    {
        var parts = input.Split(' ');
        string source = Shapes[parts[0]](int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var parsed = Cs.Parse(source);
                Assert.Equal(source, parsed.Root.Render());
                Assert.NotEqual(ParseCoverage.Typed, parsed.Coverage);
                foreach (var kind in new[] { ParseKind.Expression, ParseKind.Statements, ParseKind.Member, ParseKind.Document })
                {
                    var raw = new CsRaw(source);
                    var fragment = raw.Parse(new ParseContext(kind));
                    Assert.NotNull(fragment.Root);
                }
                Assert.NotEqual(ParseCoverage.Typed, Cs.ParseTree(source).Coverage);
            }
            catch (Exception exception) { failure = exception; }
        }, 1 << 20);
        thread.Start();
        thread.Join();
        Assert.Null(failure);
    }

    // GEN088 and other validation rejections never parse typed (release invariant: Typed with no errors renders).
    [Theory]
    [InlineData("((int)x)++;")]
    [InlineData("((int)x) = 1;")]
    [InlineData("N()++;")]
    [InlineData("(a + b)++;")]
    [InlineData("++N();")]
    [InlineData("var y = o is (string s or null);")]
    [InlineData("var y = o is int?[] ? [] : new int[0];")]
    public void Invalid_statement_is_raw_and_its_siblings_stay_typed(string statement)
    {
        string source = "class C\n{\n    void M(object o, int x, int a, int b)\n    {\n        int before = 1;\n        " + statement +
            "\n        int after = 2;\n    }\n}\n";
        var parsed = Cs.Parse(source);
        Assert.Equal(source, parsed.Root.Render());
        Assert.Empty(parsed.Root.Validate());
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        var body = ((CsMethod)((CsTypeDeclaration)parsed.Root.Items[0]).Members[0]).Body!;
        Assert.Equal(new[] { StatementKind.Local, StatementKind.Raw, StatementKind.Local }, body.Statements.Select(s => s.Kind));
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal(("PARSE002", DiagnosticSeverity.Warning), (diagnostic.Code, diagnostic.Severity));
        Assert.Equal(source.IndexOf(statement, StringComparison.Ordinal), diagnostic.Offset);
    }

    [Fact]
    public void Validation_rejected_member_and_top_level_statement_are_raw()
    {
        string member = "class C { int P => N()++; int Q => 1; }";
        var parsed = Cs.Parse(member);
        var type = (CsTypeDeclaration)parsed.Root.Items[0];
        Assert.IsType<CsRawMember>(type.Members[0]);
        Assert.IsType<CsProperty>(type.Members[1]);
        Assert.Equal(member, parsed.Root.Render());

        string topLevel = "System.Console.WriteLine(1);\nclass C { }\nSystem.Console.WriteLine(2);\n";
        var program = Cs.Parse(topLevel);
        Assert.Equal(ParseCoverage.Partial, program.Coverage);
        Assert.IsType<CsGlobalStatement>(program.Root.Items[0]);
        Assert.IsType<CsRaw>(program.Root.Items[2]);
        Assert.Equal("PARSE002", Assert.Single(program.Diagnostics).Code);
        Assert.Equal(topLevel, program.Root.Render());
    }

    [Fact]
    public void Validation_error_without_a_recovery_unit_keeps_the_whole_source_opaque()
    {
        string source = "#endregion\nclass C { }\n";
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Opaque, parsed.Coverage);
        Assert.IsType<CsRaw>(Assert.Single(parsed.Root.Items));
        Assert.Equal("PARSE002", Assert.Single(parsed.Diagnostics).Code);
        Assert.Equal(source, parsed.Root.Render());
    }

    // The bare is-expression statement is recovered whole, not split before `r with { }`.
    [Fact]
    public void Rejected_is_expression_statement_is_one_raw_statement()
    {
        string source = "class C\n{\n    void M(object o)\n    {\n        o is R { A: 1 } r with { };\n        F();\n    }\n}\n";
        var parsed = Cs.Parse(source);
        var body = ((CsMethod)((CsTypeDeclaration)parsed.Root.Items[0]).Members[0]).Body!;
        Assert.Equal(new[] { StatementKind.Raw, StatementKind.Expression }, body.Statements.Select(s => s.Kind));
        Assert.Equal("o is R { A: 1 } r with { };", body.Statements[0].Text);
        Assert.Equal(source, parsed.Root.Render());
    }

    [Theory]
    [InlineData("interface I<T> where T : I<T> { static abstract T operator +(T a, T b); }\nclass C : I<C> { static C I<C>.operator +(C a, C b) => a; }\n")]
    [InlineData("class C { void M(System.IDisposable d) { using (d) { } } }\n")]
    public void Forms_on_rethreaded_paths_stay_typed(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Equal(source, parsed.Root.Render());
    }

    [Fact]
    public void Symbol_model_errors_of_a_top_level_statement_are_reported()
    {
        var document = new CsDocument { Items = new CsNode[] { new CsGlobalStatement(new CsRaw("x")) } };
        Assert.Contains(document.Validate(), d => d.Code == "SYM001");
    }

    [Fact]
    public void Pre_cancelled_token_stops_every_parse_entry_point()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var token = source.Token;
        Assert.Throws<OperationCanceledException>(() => Cs.Parse("class C { }", null, token));
        Assert.Throws<OperationCanceledException>(() => Cs.Parse(Encoding.UTF8.GetBytes("class C { }"), null, token));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("class C { }"));
        Assert.Throws<OperationCanceledException>(() => Cs.Parse(stream, null, token));
        Assert.True(stream.CanRead);
        Assert.Throws<OperationCanceledException>(() => Cs.ParseTree("int x = 1;", new ParseOptions(), token));
        foreach (var kind in new[] { ParseKind.Document, ParseKind.Expression, ParseKind.Statements, ParseKind.Member })
            Assert.Throws<OperationCanceledException>(() => new CsRaw("x").Parse(new ParseContext(kind), token));
        Assert.Equal(ParseCoverage.Typed, Cs.Parse("class C { }", null, CancellationToken.None).Coverage);
        Assert.Equal(ParseCoverage.Typed, Cs.ParseTree("int x = 1;", new ParseOptions(), CancellationToken.None).Coverage);
        Assert.Equal(ParseCoverage.Typed, new CsRaw("x").Parse(new ParseContext(ParseKind.Expression), CancellationToken.None).Coverage);
    }

    sealed class CancellingStream(byte[] bytes, CancellationTokenSource source) : MemoryStream(bytes)
    {
        public bool Closed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            source.Cancel();
            return read;
        }

        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    [Fact]
    public void Cancellation_during_parse_throws_and_leaves_the_stream_open()
    {
        using var source = new CancellationTokenSource();
        using var stream = new CancellingStream(Encoding.UTF8.GetBytes("class C { }"), source);
        Assert.Throws<OperationCanceledException>(() => Cs.Parse(stream, null, source.Token));
        Assert.False(stream.Closed);

        string large = "class C { " + string.Join(" ", Enumerable.Range(0, 20_000).Select(i => "int f" + i + ";")) + " }";
        using var timer = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => Cs.Parse(large, null, timer.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), watch.Elapsed.ToString());
    }
}
