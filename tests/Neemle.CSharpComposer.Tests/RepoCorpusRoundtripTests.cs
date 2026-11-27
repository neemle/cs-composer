using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-18 AC-6: every *.cs under src/ and tests/ is embedded into this assembly at build time (see the csproj),
// parsed from bytes and emitted untouched with default validation.
[Trait("Category", "Integration")]
public sealed class RepoCorpusRoundtripTests
{
    const string Prefix = "Corpus/";

    // Files kept partly as raw source (PARSE002 Warning, Partial coverage); none since GENE-19 AD D10.
    static readonly string[] PartialFiles = [];

    // GENE-19 AD D10: the fluent builder chains of about 60 calls in these files are one nesting level, so the
    // files parse typed again with no diagnostics (they were Partial while chains counted one level per link).
    static readonly string[] ChainFiles =
    [
        "tests/Neemle.CSharpComposer.Tests/EngineFeatureTests.cs",
        "tests/Neemle.CSharpComposer.Tests/InterfaceValidationTests.cs",
    ];

    static readonly string[] Gene18Files =
    [
        "src/Neemle.CSharpComposer/Cs.cs",
        "src/Neemle.CSharpComposer/Model/Values.cs",
        "src/Neemle.CSharpComposer/Rendering/IdentifierText.cs",
        "src/Neemle.CSharpComposer/Parsing/DocumentParser.Strings.cs",
        "tests/Neemle.CSharpComposer.Tests/DomTests.cs",
    ];

    static readonly IReadOnlyDictionary<string, string> Resources = typeof(RepoCorpusRoundtripTests).Assembly
        .GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
        .ToDictionary(name => name[Prefix.Length..].Replace('\\', '/'), name => name);

    public static TheoryData<string> Files() => new(Resources.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public void Corpus_holds_every_source_and_test_file()
    {
        Assert.True(Resources.Count >= 220, "Corpus has only " + Resources.Count + " files.");
        Assert.All(Gene18Files.Concat(PartialFiles).Concat(ChainFiles), file => Assert.Contains(file, Resources.Keys));
        Assert.Contains("tests/Neemle.CSharpComposer.Tests/RepoCorpusRoundtripTests.cs", Resources.Keys);
        Assert.All(Resources.Keys, file => Assert.Matches("^(src|tests)/.+\\.cs$", file));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Untouched_file_round_trips_byte_identically(string file)
    {
        byte[] source = Read(file);
        var parsed = Cs.Parse(source);
        ParenthesizedCastTests.AssertNoInvalidTupleShapes(parsed.Root);
        if (PartialFiles.Contains(file))
        {
            Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
            Assert.All(parsed.Diagnostics, d => Assert.Equal(("PARSE002", DiagnosticSeverity.Warning, "Nesting exceeds 128."), (d.Code, d.Severity, d.Message)));
        }
        if (ChainFiles.Contains(file))
        {
            Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
            Assert.Empty(parsed.Diagnostics);
        }
        Assert.DoesNotContain(parsed.Root.Validate(), d => d.Code == "GEN001");
        Assert.Equal(source, parsed.Root.ToBytes());
    }

    static byte[] Read(string file)
    {
        using var stream = typeof(RepoCorpusRoundtripTests).Assembly.GetManifestResourceStream(Resources[file])!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
