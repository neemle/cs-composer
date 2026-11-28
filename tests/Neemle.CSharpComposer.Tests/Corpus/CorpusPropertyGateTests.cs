using Neemle.CSharpComposer.Model;
using Xunit;
using Xunit.Abstractions;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297 v2 section F, stages F1, F4 and F6: the corpus gate (P3, OWN, IDEM). This class is the only one in
// the "Corpus" category. CI runs it as its own step without the coverage collector, so the 100% Unit and Integration gates
// never depend on corpus content. The gate test writes corpus-gate.json and fails on infrastructure errors and, where
// CORPUS_GATE_ENFORCE=all (the CI step and the full run since F6), on any violation of the untouched render, of any edit
// or of OWN-a; the other tests pin the gate's own comparer, comment view, list detection and tokenizer.
[Trait("Category", "Corpus")]
public sealed class CorpusPropertyGateTests(ITestOutputHelper output)
{
    [Fact]
    public void Corpus_gate_reports_every_file_and_sampled_edits_and_enforces_what_it_is_asked_to()
    {
        var settings = CorpusGate.Settings.FromEnvironment();
        var gate = new CorpusGate(settings);
        Assert.True(gate.CorpusSize >= 220, "Corpus has only " + gate.CorpusSize + " files.");
        var report = gate.Run(output.WriteLine);
        CorpusGate.Write(report, settings.ReportPath);
        output.WriteLine("Report: " + settings.ReportPath);
        output.WriteLine("Timing: " + report["timing"]!.ToJsonString());
        output.WriteLine("OWN-a: " + report["ownParse"]!["violatingFiles"] + " of " + report["ownParse"]!["files"] + " files");
        foreach (var group in new[] { "untouched", "variants" })
            foreach (var (name, row) in report[group]!.AsObject())
                output.WriteLine(group + " " + name + ": checked " + row!["checked"] + ", rejected " + row["rejectedEdits"] + ", render errors " +
                    row["renderErrors"] + " (EMIT001 " + row["EMIT001"] + "), P3 " + row["P3"]!["violations"] + ", OWN-b " + row["OWN-b"]!["violations"] + ", OWN-c " +
                    row["OWN-c"]!["violations"] + ", IDEM " + row["IDEM"]!["violations"]);
        Assert.True(File.Exists(settings.ReportPath));
        Assert.True(gate.GateErrors.Count == 0, "Corpus gate errors:\n" + string.Join("\n", gate.GateErrors.Take(20)));
        Assert.True(report["corpus"]!["parsed"]!.GetValue<int>() > 0, "The corpus gate checked no files.");
        var enforced = gate.Enforcement();
        Assert.True(enforced.Count == 0, "Corpus gate enforcement failed: " + string.Join("; ", enforced));
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
    public void Guard_equivalence_compares_comment_text_after_dedent()
    {
        Assert.Null(GuardEquivalence.Compare(Parse("class C\n{\n    /* a\n       b */\n    int A;\n}\n"), Parse("class C\n{\n/* a\n b */\nint A;\n}\n")));
        Assert.NotNull(GuardEquivalence.Compare(Parse("class C\n{\n    /* a b */\n    int A;\n}\n"), Parse("class C\n{\n    /* a  c */\n    int A;\n}\n")));
    }

    [Fact]
    public void Comment_view_names_owners_structurally_and_counts_opaque_and_source_comments()
    {
        const string source = "// file\nclass C\n{\n    // lead\n    int A; // trail\n    /// <summary>x</summary>\n    void M() { }\n#if NEVER // why\n    // inactive\n#endif\n#region R // name\n#endregion\n}\n";
        var root = Parse(source);
        var entries = CorpusComments.Entries(root);
        var lead = Assert.Single(entries, e => e.Text == "// lead");
        Assert.EndsWith("CsField#0", lead.Owner);
        Assert.Equal("Line Leading", lead.Label);
        var trail = Assert.Single(entries, e => e.Text == "// trail");
        Assert.Equal("Line Trailing", trail.Label);
        Assert.Contains(entries, e => e.Text == "// inactive" && e.Label == "Raw");
        Assert.Contains(entries, e => e.Text == "// why" && e.Label == "Line Trailing" && e.OwnerKind == "CsDirective.If");
        Assert.DoesNotContain(entries, e => e.Text.Contains("name", StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Label.StartsWith("Documentation", StringComparison.Ordinal));
        var lines = CorpusComments.Bag(CorpusComments.SourceLines(source));
        var model = CorpusComments.Bag(entries.Where(e => !e.HiddenFromSource).SelectMany(e => e.Text.Split('\n')));
        Assert.Empty(CorpusComments.Difference(lines, model));
        Assert.Equal("/* a\nb */", CorpusComments.Normalize("/* a\r\n     b */  "));
    }

    [Fact]
    public void List_detection_finds_list_items_and_the_index_of_the_lists_first_item()
    {
        var root = Parse("class C\n{\n    int A;\n    int B;\n}\n");
        var type = root.Items[0];
        var members = ((CsTypeDeclaration)type).Members;
        Assert.Equal(type.Children.ToList().IndexOf(members[0]), CorpusGate.ListFirst(type, members[1]));
        Assert.Equal(0, CorpusGate.ListFirst(root, type));
        var field = (CsField)members[0];
        Assert.Equal(-1, CorpusGate.ListFirst(field, field.Children.First()));
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
}
