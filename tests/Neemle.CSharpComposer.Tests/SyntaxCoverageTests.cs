using System.Text.Json;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class SyntaxCoverageTests
{
    static JsonDocument Resource(string name) => JsonDocument.Parse(typeof(SyntaxCoverageTests).Assembly.GetManifestResourceStream(name)!);

    public static IEnumerable<object[]> Corpus()
    {
        using var document = Resource("SyntaxCorpus.json");
        foreach (var entry in document.RootElement.EnumerateArray())
            yield return new object[] { entry.GetProperty("id").GetString()!, entry.GetProperty("mode").GetString()!,
                entry.GetProperty("version").GetInt32(), entry.GetProperty("source").GetString()! };
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Grammar_families_use_typed_nodes_and_survive_reformatting(string id, string mode, int version, string source)
    {
        var language = (CsLanguageVersion)version;
        if (mode == "statements")
        {
            var parsed = Cs.ParseTree(source, language);
            Assert.Empty(parsed.Diagnostics);
            Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
            Assert.Empty(parsed.Root.Validate());
            Assert.Equal(source, parsed.Root.Render());
            AssertTyped(parsed.Root.Descendants().Select(n => n.Node));
            var formatted = parsed.Root.Reformat().Render();
            var reparsed = Cs.ParseTree(formatted, language);
            Assert.Empty(reparsed.Diagnostics);
            AssertTyped(reparsed.Root.Descendants().Select(n => n.Node));
            Assert.Equal(formatted, reparsed.Root.Reformat().Render());
        }
        else
        {
            var parsed = Cs.Parse(source, new() { LanguageVersion = language });
            Assert.Empty(parsed.Diagnostics);
            Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
            Assert.Empty(parsed.Root.Validate());
            Assert.Equal(source, parsed.Root.Render());
            AssertTyped(parsed.Root.Descendants().Select(n => n.Node));
            var formatted = parsed.Root.Reformat().Render();
            var reparsed = Cs.Parse(formatted, new() { LanguageVersion = language });
            Assert.Empty(reparsed.Diagnostics);
            Assert.Empty(reparsed.Root.Validate());
            AssertTyped(reparsed.Root.Descendants().Select(n => n.Node));
            Assert.Equal(formatted, reparsed.Root.Reformat().Render());
        }
    }

    static void AssertTyped(IEnumerable<CsNode> nodes) => Assert.DoesNotContain(nodes, node => node is CsRaw or CsRawMember ||
        node is CsExpression { Kind: ExpressionKind.Raw } || node is CsStatement { Kind: StatementKind.Raw } || node is CsType { Form: TypeForm.Raw });

    [Fact]
    public void Every_reference_grammar_form_has_an_explicit_model_and_exercised_fixture()
    {
        using var manifest = Resource("SyntaxCoverage.json");
        var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(manifest.RootElement.GetProperty("syntax_forms").GetInt32(), entries.Length);
        Assert.Equal(250, entries.Length);
        Assert.Equal(entries.Length, entries.Select(e => e.GetProperty("syntax").GetString()).Distinct().Count());
        var fixtures = Corpus().Select(c => (string)c[0]).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            string status = entry.GetProperty("status").GetString()!;
            Assert.Contains(status, new[] { "typed", "invalid_source_recovery" });
            if (status == "invalid_source_recovery") { Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("reason").GetString())); continue; }
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("model").GetString()));
            Assert.Contains(entry.GetProperty("fixture").GetString()!, fixtures);
        }
    }
}
