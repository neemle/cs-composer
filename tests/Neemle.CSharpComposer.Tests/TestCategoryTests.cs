using System.Reflection;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-25: CI runs the suite twice, filtered by Category=Unit and Category=Integration, and gates each run at 100%
// coverage. A test class without exactly one class-level Category trait would run in neither filter and silently
// drop out of both gates, so this guard fails the build instead.
// GENE-29 S0: "Corpus" is a third known category for the corpus property gate. CI runs it as its own step without
// coverage, so it never carries the Unit or Integration gates; those two categories and their rules are unchanged.
[Trait("Category", "Unit")]
public sealed class TestCategoryTests
{
    static readonly string[] Categories = ["Unit", "Integration", "Corpus"];

    static IEnumerable<Type> TestClasses() => typeof(TestCategoryTests).Assembly.GetTypes()
        .Where(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any()));

    static string[] CategoryTraits(MemberInfo member) => member.GetCustomAttributesData()
        .Where(data => data.AttributeType == typeof(TraitAttribute) && (string?)data.ConstructorArguments[0].Value == "Category")
        .Select(data => (string)data.ConstructorArguments[1].Value!).ToArray();

    [Fact]
    public void Every_test_class_carries_exactly_one_known_category_at_class_level()
    {
        var classes = TestClasses().ToArray();
        Assert.True(classes.Length >= 80, "Found only " + classes.Length + " test classes.");
        var wrong = classes.Select(type => (type, traits: CategoryTraits(type)))
            .Where(x => x.traits.Length != 1 || !Categories.Contains(x.traits[0]))
            .Select(x => x.type.FullName + " [" + string.Join(",", x.traits) + "]").ToArray();
        Assert.True(wrong.Length == 0, "Test classes without exactly one Category trait (Unit, Integration or Corpus):\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Categories_are_set_per_class_never_per_method()
    {
        var methods = TestClasses().SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => CategoryTraits(method).Length != 0)
            .Select(method => method.DeclaringType!.FullName + "." + method.Name).ToArray();
        Assert.True(methods.Length == 0, "Category traits on methods:\n" + string.Join("\n", methods));
    }

    [Fact]
    public void Every_category_holds_tests()
    {
        var used = TestClasses().SelectMany(CategoryTraits).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Categories.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
    }
}
