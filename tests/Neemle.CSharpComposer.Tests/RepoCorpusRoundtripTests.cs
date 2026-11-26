using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-18 AC-6: every *.cs under src/ and tests/ is embedded into this assembly at build time (see the csproj),
// parsed from bytes and emitted untouched with default validation.
public sealed class RepoCorpusRoundtripTests
{
    const string Prefix = "Corpus/";

    // GENE-19 (depth limit, GEN001). The list may only shrink: each entry is asserted to still fail, and
    // whichever of GENE-18 and GENE-19 merges second empties it.
    static readonly string[] KnownFailures =
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
        Assert.All(Gene18Files.Concat(KnownFailures), file => Assert.Contains(file, Resources.Keys));
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
        if (KnownFailures.Contains(file))
        {
            var failure = Record.Exception(() => parsed.Root.ToBytes());
            var thrown = Assert.IsType<GenValidationException>(failure);
            Assert.Contains(thrown.Diagnostics, d => d.Code == "GEN001");
            Assert.DoesNotContain(thrown.Diagnostics, d => d.Code == "GEN011");
            return;
        }
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
