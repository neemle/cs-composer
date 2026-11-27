using Xunit;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-25: the scenario rows checked in memory.
[Trait("Category", "Unit")]
public sealed class ScenarioUnitTests
{
    public static TheoryData<string> Ids() => new(ScenarioCatalog.Ids);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Outcome_matches_in_memory(string id)
    {
        var scenario = ScenarioCatalog.Get(id);
        Assert.Equal(scenario.Expected, Outcome.Of(scenario.Act, !Outcome.NotFixedPoints.ContainsKey(id)));
    }
}

// GENE-25: the same rows end to end; each produced document goes through WriteTo and is parsed back from the stream.
[Trait("Category", "Integration")]
public sealed class ScenarioIntegrationTests
{
    public static TheoryData<string> Ids() => new(ScenarioCatalog.Ids);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Outcome_matches_through_a_stream_round_trip(string id)
    {
        var scenario = ScenarioCatalog.Get(id);
        Assert.Equal(scenario.Expected, Outcome.ThroughStream(scenario.Act, !Outcome.NotFixedPoints.ContainsKey(id)));
    }
}
