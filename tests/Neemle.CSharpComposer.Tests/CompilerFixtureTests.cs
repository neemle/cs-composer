using Xunit;
using Xunit.Sdk;

namespace Neemle.CSharpComposer.Tests;

[Trait("Category", "Integration")]
public sealed class CompilerFixtureTests
{
    [Fact]
    public void FailedBuildWithoutParsedErrorsFailsVisibly()
    {
        var failure = Assert.ThrowsAny<XunitException>(() => CompilerFixture.Errors(1, "MSBUILD : error MSB1009"));
        Assert.Contains("exited with code 1", failure.Message);
        Assert.Contains("MSB1009", failure.Message);
    }

    [Fact]
    public void FailedBuildReturnsItsParsedErrors()
    {
        const string output = "/w/Program.cs(3,5): error CS0103: x\n/w/Program.cs(3,5): error CS0103: x\n";
        Assert.Equal(new[] { (3, "CS0103") }, CompilerFixture.Errors(1, output));
    }

    [Fact]
    public void CleanBuildReturnsNoErrors() => Assert.Empty(CompilerFixture.Errors(0, "Build succeeded."));

    [Fact]
    public async Task BuildRejectedBeforeCompilingFailsVisibly() =>
        await Assert.ThrowsAnyAsync<XunitException>(() =>
            CompilerFixture.Build("class Program { static void Main() { } }", "no-such-version"));
}
