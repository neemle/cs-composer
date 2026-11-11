using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class LanguageVersionTests
{
    static CsDocument With(CsTypeDeclaration type) => new CsDocument() { Types = new[] { type } };

    static CsDocument Member(CsMember member, TypeKind parent = TypeKind.Class) =>
        With(new CsTypeDeclaration(parent, "C") { Members = new[] { member } });

    static CsProperty Property() => new CsProperty(Cs.Int, "Number")
    {
        Accessors = new[] { new CsAccessor("get") }
    };

    static CsMethod Method() => new CsMethod(Cs.Void, "Run") { Body = Cs.Body() };

    static CsDocument Parameter(string modifier, CsType? type = null) => Member(Method() with
    {
        Parameters = new[] { new CsParameter(type ?? Cs.Int, "argument") { Modifier = modifier } }
    });

    public static IEnumerable<object[]> Features() =>
        LegacyFeatures().Concat(RecordFeatures()).Concat(NewerFeatures());

    static IEnumerable<object[]> LegacyFeatures()
    {
        yield return new object[] { new CsDocument() { NullableContext = NullableMode.Enable }, 800 };
        yield return new object[] { Member(Property() with { Type = Cs.Type<string>().Nullable() }), 800 };
        yield return new object[] { Member(Method() with { Modifiers = new[] { "readonly" } }, TypeKind.Struct), 800 };
        yield return new object[] { Member(Method(), TypeKind.Interface), 800 };
        yield return new object[]
            { Member(Method() with { Modifiers = new[] { "private" } }, TypeKind.Interface), 800 };
        yield return new object[] { Member(Method() with { Modifiers = new[] { "static" } }, TypeKind.Interface), 800 };
        foreach (string constraint in new[] { "class?", "notnull" })
            yield return new object[] { With(new CsTypeDeclaration(TypeKind.Class, "C")
            { TypeParameters = new[] { new TypeParameter("T")
                { Constraints = new[] { Cs.Constraint(constraint) } } } }), 800 };
    }

    static IEnumerable<object[]> RecordFeatures()
    {
        foreach (TypeKind kind in new[] { TypeKind.Record, TypeKind.RecordClass })
            yield return new object[] { With(new CsTypeDeclaration(kind, "C")), 900 };
        yield return new object[] { Member(Property() with { Accessors = new[] { new CsAccessor("init") } }), 900 };
        yield return new object[] { Member(Method() with { Modifiers = new[] { "public", "partial" } }), 900 };
        yield return new object[] { With(new CsTypeDeclaration(TypeKind.RecordStruct, "C")), 1000 };
        yield return new object[]
            { new CsDocument() { Usings = new[] { new CsUsing("System", IsGlobal: true) } }, 1000 };
        yield return new object[] { Member(new CsConstructor("C") { Body = Cs.Body() }, TypeKind.Struct), 1000 };
        yield return new object[]
            { Member(new CsField(Cs.Int, "count") { Initializer = Cs.Literal(1) }, TypeKind.Struct), 1000 };
        yield return new object[] { Member(Property() with { Initializer = Cs.Literal(1) }, TypeKind.Struct), 1000 };
    }

    static IEnumerable<object[]> NewerFeatures()
    {
        yield return new object[] { Member(Property() with { Modifiers = new[] { "required" } }), 1100 };
        yield return new object[] { Parameter("scoped ref"), 1100 };
        yield return new object[] { Parameter("scoped in"), 1100 };
        foreach (string modifier in new[] { "abstract", "virtual" })
            yield return new object[] { Member(Method() with { Modifiers = new[] { "static", modifier } },
                TypeKind.Interface), 1100 };
        yield return new object[] { Parameter("ref readonly"), 1200 };
        yield return new object[] { Parameter("params", Cs.Type<List<int>>()), 1300 };
        yield return new object[] { With(new CsTypeDeclaration(TypeKind.Struct, "C")
        { Modifiers = new[] { "ref" }, BaseTypes = new[] { Cs.Type<IDisposable>() } }), 1300 };
    }

    [Theory]
    [MemberData(nameof(Features))]
    public void Every_feature_is_rejected_below_and_available_at_its_minimum(CsDocument document, int minimum)
    {
        var below = minimum == 800 ? CsLanguageVersion.CSharp7_3 : (CsLanguageVersion)(minimum - 100);
        var unavailable = document with { LanguageVersion = below };
        var diagnostics = unavailable.Validate();
        Assert.Contains(diagnostics, error => error.Code == "GEN073");
        Assert.Equal(diagnostics, unavailable.Validate());
        Assert.Throws<GenValidationException>(() => unavailable.Render(new RenderOptions { Validate = false }));
        Assert.DoesNotContain((document with { LanguageVersion = (CsLanguageVersion)minimum }).Validate(),
            error => error.Code == "GEN073");
    }

    [Fact(DisplayName = "[SEC][UC-003] Construction and direct record edits cannot bypass version guards")]
    public void Versions_are_snapshot_state_and_unknown_values_never_fall_back()
    {
        var document = Cs.Document().Class("C").End().Build();
        Assert.Equal(CsLanguageVersion.CSharp14, document.LanguageVersion);
        Assert.NotEqual(document, document with { LanguageVersion = CsLanguageVersion.CSharp13 });
        Assert.Throws<GenValidationException>(() => Cs.Document((CsLanguageVersion)999));
        Assert.Throws<GenValidationException>(() => new CsDocument((CsLanguageVersion)999));
        var invalid = document with { LanguageVersion = (CsLanguageVersion)999 };
        Assert.Contains(invalid.Validate(), error => error.Code == "GEN072" && error.Path == "LanguageVersion");
        Assert.Throws<GenValidationException>(() => invalid.Render(new RenderOptions { Validate = false }));
    }

    [Fact]
    public void Known_builder_targets_guard_insertions_and_irreversible_feature_mutations()
    {
        Assert.Throws<GenValidationException>(() => Cs.Document(CsLanguageVersion.CSharp8).Record("R"));
        Assert.Throws<GenValidationException>(() => Cs.Document(CsLanguageVersion.CSharp9)
            .Using("System", isGlobal: true));
        Assert.Throws<GenValidationException>(() => Cs.Document(CsLanguageVersion.CSharp7_3)
            .Nullable(NullableMode.Enable));
        var property = Cs.Document(CsLanguageVersion.CSharp10).Class("C").Property<int>("Number");
        Assert.Throws<GenValidationException>(() => property.Required());
        Assert.DoesNotContain("required", property.Build().Modifiers);
        Assert.Throws<GenValidationException>(() => Cs.Document(CsLanguageVersion.CSharp8)
            .Class("C").Property<int>("Number").Init());
        Assert.Throws<GenValidationException>(() => Cs.Document(CsLanguageVersion.CSharp10)
            .Class("C").Method(Cs.Void, "Run").Parameter<int>("argument", "scoped ref"));
        var supplied = With(new CsTypeDeclaration(TypeKind.Record, "R")).Types;
        Assert.Throws<GenValidationException>(() => Cs.Document(CsLanguageVersion.CSharp8).AddRange(supplied));
    }

    [Fact]
    public void Render_options_are_gated_and_record_class_spelling_is_version_appropriate()
    {
        var old = With(new CsTypeDeclaration(TypeKind.Class, "C")) with
        { LanguageVersion = CsLanguageVersion.CSharp7_3, Namespace = "Example" };
        Assert.Throws<GenValidationException>(() => old.Render());
        Assert.Contains("namespace Example\n{", old.Render(new RenderOptions
            { NamespaceStyle = NamespaceStyle.BlockScoped }));
        Assert.Throws<GenValidationException>(() => (old with { Namespace = null }).Render(new RenderOptions
        { Nullable = NullableMode.Enable, Validate = false }));
        var record = With(new CsTypeDeclaration(TypeKind.RecordClass, "R"));
        Assert.Contains("record R", (record with { LanguageVersion = CsLanguageVersion.CSharp9 }).Render());
        Assert.Contains("record class R", record.Render());
        Assert.DoesNotContain((Member(Property() with { Type = Cs.Int.Nullable() }) with
        { LanguageVersion = CsLanguageVersion.CSharp7_3 }).Validate(), error => error.Code == "GEN073");
    }
}
