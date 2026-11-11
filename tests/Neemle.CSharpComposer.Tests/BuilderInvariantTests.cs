using Neemle.CSharpComposer.Builders;
using Neemle.CSharpComposer;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class BuilderInvariantTests
{
    [Fact(DisplayName = "[SEC][UC-001] Incorrect fluent builder self type fails with an actionable invariant")]
    public void Incorrect_self_type_throws_an_actionable_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new Wrong().Public());
        Assert.Contains("Wrong", error.Message);
        Assert.Contains("Other", error.Message);
        Assert.Contains("TSelf", error.Message);
        Assert.Contains("must use its own type", error.Message);
    }

    sealed class Other : MemberBuilder<Other> { }
    sealed class Wrong : MemberBuilder<Other> { }
}
