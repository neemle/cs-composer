namespace Neemle.CSharpComposer.Tests.Scenarios;

public static class ScenarioCatalog
{
    static readonly Func<IEnumerable<Scenario>>[] Tables =
    [
        ValidatorScenarios.All,
        MemberScenarios.All,
        ApiScenarios.All,
        StatementScenarios.All,
        DirectivePatternScenarios.All,
        OperationScenarios.All,
        DocumentationScenarios.All,
        FlowFeatureScenarios.All,
        EditScenarios.All,
    ];

    static readonly Lazy<IReadOnlyDictionary<string, Scenario>> Rows = new(() =>
    {
        var rows = new Dictionary<string, Scenario>(StringComparer.Ordinal);
        foreach (var scenario in Tables.SelectMany(table => table()))
            if (!rows.TryAdd(scenario.Id, scenario)) throw new InvalidOperationException("Duplicate scenario id " + scenario.Id + ".");
        return rows;
    });

    public static IEnumerable<string> Ids => Rows.Value.Keys.Order(StringComparer.Ordinal);

    public static Scenario Get(string id) => Rows.Value[id];
}
