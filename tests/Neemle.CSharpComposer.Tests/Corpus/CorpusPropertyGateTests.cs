using Neemle.CSharpComposer.Model;
using Xunit;
using Xunit.Abstractions;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297 I5/I6, stage S0: the corpus property gate, report-only. This class is the only one in the
// "Corpus" category. CI runs it as its own step without the coverage collector, so the 100% Unit and Integration gates
// never depend on corpus content. The gate test writes corpus-gate.json and fails only on infrastructure errors; the
// other tests pin the gate's own comparer, locality check and tokenizer.
[Trait("Category", "Corpus")]
public sealed class CorpusPropertyGateTests(ITestOutputHelper output)
{
    [Fact]
    public void Corpus_gate_reports_every_node_without_failing_on_violations()
    {
        var settings = CorpusGate.Settings.FromEnvironment();
        var gate = new CorpusGate(settings);
        Assert.True(gate.CorpusSize >= 220, "Corpus has only " + gate.CorpusSize + " files.");
        var report = gate.Run(output.WriteLine);
        CorpusGate.Write(report, settings.ReportPath);
        output.WriteLine("Report: " + settings.ReportPath);
        output.WriteLine("Timing: " + report["timing"]!["wallSeconds"] + "s wall, p3 " + report["p3Mode"]);
        output.WriteLine("P1: " + report["P1"]!["violations"] + " of " + report["P1"]!["checked"]);
        foreach (var (name, variant) in report["variants"]!.AsObject())
            output.WriteLine(name + ": " + variant!["timing"]!["wallSeconds"] + "s wall, totals " + variant["totals"]!.ToJsonString());
        output.WriteLine("Token faithfulness: " + report["tokenFaithfulness"]!["codeFaithfulFileRate"] + "% code, " +
            report["tokenFaithfulness"]!["allFaithfulFileRate"] + "% all");
        Assert.True(File.Exists(settings.ReportPath));
        Assert.True(gate.GateErrors.Count == 0, "Corpus gate errors:\n" + string.Join("\n", gate.GateErrors.Take(20)));
        Assert.True(report["corpus"]!["nodes"]!.GetValue<int>() > 0, "The corpus gate checked no nodes.");
    }

    static CsDocument Parse(string source) => Cs.Parse(source).Root;

    [Fact]
    public void Guard_equivalence_accepts_equal_trees_and_reports_the_first_divergence()
    {
        Assert.Null(GuardEquivalence.Compare(Parse("class C { int M() => (a + b) * c; }"), Parse("class C\n{\n    int M() => (a + b) * c;\n}\n")));
        var divergence = GuardEquivalence.Compare(Parse("class C { int M() => a + b; }"), Parse("class C { int M() => a - b; }"));
        Assert.NotNull(divergence);
        Assert.Contains("CsExpression", divergence.Pattern);
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C { int A; }"), Parse("class C { int B; }")));
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C { int A; }"), Parse("class C { int A; int B; }")));
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C\n{\n    // one\n    int A;\n}\n"), Parse("class C\n{\n    int A;\n}\n")));
    }

    [Fact]
    public void Guard_equivalence_treats_only_added_parentheses_and_else_braces_as_transparent()
    {
        Assert.Null(GuardEquivalence.Compare(Parse("class C { void M() { x = a + b; } }"), Parse("class C { void M() { x = (a + b); } }")));
        Assert.Null(GuardEquivalence.Compare(Parse("class C { int M() => a * (b + c); }"), Parse("class C { int M() => a * ((b + c)); }")));
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C { void M() { x = (a + b); } }"), Parse("class C { void M() { x = a + b; } }")));
        Assert.Null(GuardEquivalence.Compare(Parse("class C { void M() { if (a) x(); else y(); } }"), Parse("class C { void M() { if (a) x(); else { y(); } } }")));
        Assert.Null(GuardEquivalence.Compare(Parse("class C { void M() { if (a) x(); } }"), Parse("class C { void M() { if (a) { x(); } } }")));
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C { void M() { if (a) { x(); y(); } } }"), Parse("class C { void M() { if (a) x(); } }")));
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C { void M() { x(); } }"), Parse("class C { void M() { { x(); } } }")));
    }

    [Fact]
    public void Locality_allows_changes_inside_the_envelope_and_its_boundary_whitespace_only()
    {
        const string source = "a;\n    bb;\nc;\n";
        int start = source.IndexOf("bb", StringComparison.Ordinal), end = start + 4;
        Assert.Null(CorpusGate.Locality(source, source, start, end));
        Assert.Null(CorpusGate.Locality(source, "a;\nbb ;\nc;\n", start, end));
        Assert.Null(CorpusGate.Locality(source, "a;\n  bb;\r\nc;\n", start, end));
        Assert.Equal("before text", CorpusGate.Locality(source, "x;\n    bb;\nc;\n", start, end)!.Cause);
        Assert.Equal("after text", CorpusGate.Locality(source, "a;\n    bb;\nd;\n", start, end)!.Cause);
        // P2 constrains only the text outside [a, b): one more line break after the envelope's own line end is inside it.
        Assert.Null(CorpusGate.Locality(source, "a;\n    bb;\n\nc;\n", start, end));
        Assert.Equal("after whitespace-only", CorpusGate.Locality(source, "a;\n    bb;\nc;", start, end)!.Cause);
        Assert.Equal("before whitespace-only", CorpusGate.Locality(source, "a; \n    bb;\nc;\n", start, end)!.Cause);
    }

    [Fact]
    public void Tokenizer_keeps_literals_whole_and_marks_comments_and_directives_as_trivia()
    {
        const string source = "#if DEBUG\nvar s = $\"x{y:N2}{{\" + @\"q\"\"r\" + \"\"\"raw \"q\" \"\"\" + $$\"\"\"{{z}}\"\"\" + \"a\"u8; // end\n" +
            "char c = '\\''; /* block */ double d = 1.5e+3 + .5 + 0x1F;\n";
        var tokens = CorpusTokens.Scan(source);
        Assert.Equal(["#if DEBUG", "var", "s", "=", "$\"x{y:N2}{{\"", "+", "@\"q\"\"r\"", "+", "\"\"\"raw \"q\" \"\"\"", "+",
            "$$\"\"\"{{z}}\"\"\"", "+", "\"a\"u8", ";", "// end", "char", "c", "=", "'\\''", ";", "/* block */", "double", "d", "=",
            "1.5e+3", "+", ".5", "+", "0x1F", ";"], tokens.Select(t => t.Text));
        Assert.Equal(["#if DEBUG", "// end", "/* block */"], tokens.Where(t => t.Trivia).Select(t => t.Text));
    }

    [Fact]
    public void Token_edit_distance_counts_insertions_and_deletions_up_to_a_limit()
    {
        Assert.Equal(0, CorpusTokens.EditDistance(["a", "b"], ["a", "b"], 4));
        Assert.Equal(2, CorpusTokens.EditDistance(["a", "b", "c"], ["a", "x", "c"], 4));
        Assert.Equal(1, CorpusTokens.EditDistance(["a", "b", "c"], ["a", "c"], 4));
        Assert.Equal(-1, CorpusTokens.EditDistance(["a", "b", "c", "d"], ["w", "x", "y", "z"], 4));
    }
}
