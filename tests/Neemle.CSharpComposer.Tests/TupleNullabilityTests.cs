using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class TupleNullabilityTests
{
    static CsDocument Document(CsType type) => new CsDocument("Tuple.g.cs", CsLanguageVersion.CSharp7_3)
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C")
        { Members = new CsMember[] { new CsField(type, "Pair") } } }
    };

    [Fact]
    public void Direct_tuple_value_nullability_does_not_require_reference_annotations()
    {
        var tuple = new CsType("")
        {
            Form = TypeForm.Tuple,
            Elements = new[] { new TupleElement(new CsType("int")), new TupleElement(new CsType("bool")) }
        };
        var document = Document(new CsType("") { Form = TypeForm.Nullable, Element = tuple });
        Assert.Empty(document.Validate());
        Assert.Contains("(int, bool)? Pair;", document.Render());
        Assert.Equal(document.Render(), document.Render(new RenderOptions { Validate = false }));
    }

    [Fact]
    public void Factory_and_fluent_tuple_fragments_remain_available_at_7_3()
    {
        var fragment = Cs.Nullable(Cs.Tuple(new TupleElement(Cs.Int), new TupleElement(Cs.Bool)));
        var direct = Document(fragment);
        var fluent = Cs.File("Tuple.g.cs", CsLanguageVersion.CSharp7_3).Class("C")
            .Field(fragment, "Pair").End().End().Build();
        Assert.Empty(fluent.Validate());
        Assert.Equal(direct, fluent);
        Assert.Contains("(int, bool)? Pair;", fluent.Render(new RenderOptions { Validate = false }));
    }

    [Theory(DisplayName = "[SEC][UC-003] Reference and array annotation guards remain mandatory below C#8")]
    [InlineData(false)]
    [InlineData(true)]
    public void Tuple_fix_does_not_open_a_reference_annotation_bypass(bool array)
    {
        var reference = array ? Cs.Int.Array() : Cs.Type<string>();
        var document = Document(reference.Nullable());
        Assert.Contains(document.Validate(), error => error.Code == "GEN073");
        Assert.Throws<GenValidationException>(() => document.Render());
        Assert.Throws<GenValidationException>(() => document.Render(new RenderOptions { Validate = false }));
        Assert.Throws<GenValidationException>(() => Cs.File("X", CsLanguageVersion.CSharp7_3)
            .Class("C").Field(reference.Nullable(), "Reference").Build());
    }

    [Fact(DisplayName = "[SEC][UC-003] Tuple value nullability preserves nested reference annotation guards")]
    public void Tuple_elements_still_receive_recursive_reference_feature_checks()
    {
        var tuple = Cs.Tuple(new TupleElement(Cs.Type<string>().Nullable()), new TupleElement(Cs.Bool));
        var document = Document(tuple.Nullable());
        Assert.Contains(document.Validate(), error => error.Code == "GEN073" && error.Path.Contains("Elements[0]"));
        Assert.Throws<GenValidationException>(() => document.Render(new RenderOptions { Validate = false }));
    }
}
