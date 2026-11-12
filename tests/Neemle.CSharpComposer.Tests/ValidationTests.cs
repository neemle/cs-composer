using Neemle.CSharpComposer.Validation;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer;
using Xunit;
namespace Neemle.CSharpComposer.Tests;

public sealed class ValidationTests
{
    static CsDocument With(CsMember member, TypeKind parent = TypeKind.Class) => new CsDocument() { Types = new[] { new CsTypeDeclaration(parent, "C") { Members = new[] { member } } } };

    public static IEnumerable<object[]> InvalidMembers()
    {
        yield return new object[] { new CsMethod(Cs.Void, "M") { Modifiers = new[] { "abstract" }, Body = Cs.Body() }, "GEN049" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(), TypeParameters = new[] { new TypeParameter("T"), new TypeParameter("T") } }, "GEN017" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get"), new CsAccessor("get") } }, "GEN024" };
        yield return new object[] { new CsConstructor("C") { Modifiers = new[] { "public", "static" }, Parameters = new[] { new CsParameter(Cs.Int, "x") }, InitializerKind = "base" }, "GEN053" };
        yield return new object[] { new CsField(Cs.Int, "x; injected"), "GEN003" };
        yield return new object[] { new CsField(Cs.Int, "x") { Modifiers = new[] { "public", "private" } }, "GEN033" };
        yield return new object[] { new CsField(Cs.Int, "x") { Modifiers = new[] { "async" } }, "GEN031" };
        yield return new object[] { new CsField(Cs.Int, "x") { Modifiers = new[] { "static", "static" } }, "GEN032" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Modifiers = new[] { "static", "virtual" }, Accessors = new[] { new CsAccessor("get") } }, "GEN034" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Modifiers = new[] { "sealed" }, Body = Cs.Body() }, "GEN035" };
        yield return new object[] { new CsProperty(Cs.Int, "P"), "GEN045" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Expression = Cs.Literal(1), Accessors = new[] { new CsAccessor("get") } }, "GEN044" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required", "static" }, Accessors = new[] { new CsAccessor("get") } }, "GEN047" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Modifiers = new[] { "abstract" }, Expression = Cs.Literal(1) }, "GEN046" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("set"), new CsAccessor("init") } }, "GEN029" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("bad") } }, "GEN025" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get") { Accessibility = "public" } } }, "GEN026" };
        yield return new object[] { new CsProperty(Cs.Int, "P") { Modifiers = new[] { "abstract" }, Accessors = new[] { new CsAccessor("get") { Body = Cs.Body() } } }, "GEN027" };
        yield return new object[] { new CsEvent("System.Action", "E") { Accessors = new[] { new CsAccessor("add") } }, "GEN028" };
        yield return new object[] { new CsEvent("System.Action", "E") { Accessors = new[] { new CsAccessor("add") { Body = Cs.Body() } } }, "GEN030" };
        yield return new object[] { new CsEvent("System.Action", "E") { Modifiers = new[] { "abstract" }, Initializer = Cs.Literal(null) }, "GEN054" };
        yield return new object[] { new CsField(Cs.Int, "F") { Modifiers = new[] { "const" } }, "GEN048" };
        yield return new object[] { new CsMethod(Cs.Void, "M"), "GEN050" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(), Parameters = new[] { new CsParameter(Cs.Int, "x"), new CsParameter(Cs.Int, "x") { Modifier = "invalid" } } }, "GEN015" };
        yield return new object[] { new CsConstructor("Wrong"), "GEN051" };
        yield return new object[] { new CsConstructor("C") { InitializerKind = "invalid" }, "GEN052" };
        yield return new object[] { new CsEnumMember("Outside"), "GEN037" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(), TypeParameters = new[] { new TypeParameter("T") { Constraints = new[] { Cs.Constraint("struct"), Cs.Constraint("new()") } } } }, "GEN021" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(), TypeParameters = new[] { new TypeParameter("T") { Variance = "out" } } }, "GEN018" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(), TypeParameters = new[] { new TypeParameter("T") { Constraints = new[] { Cs.Constraint("unknown") } } } }, "GEN020" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(), TypeParameters = new[] { new TypeParameter("T") { Constraints = new[] { Cs.Constraint("class"), Cs.Constraint("class") } } } }, "GEN019" };
        yield return new object[] { new CsField(Cs.Int.Array(0), "F"), "GEN009" };
        yield return new object[] { new CsField(new CsType("") { Form = TypeForm.Nullable, Element = Cs.Int.Nullable() }, "F"), "GEN010" };
        yield return new object[] { new CsField(Cs.Tuple(new TupleElement(Cs.Int)), "F"), "GEN011" };
        yield return new object[] { new CsField(Cs.RawType(""), "F"), "GEN007" };
        yield return new object[] { new CsField(Cs.Type("List").Generic(), "F"), "GEN008" };
        yield return new object[] { new CsField(new CsType("X") { Form = (TypeForm)99 }, "F"), "GEN012" };
        yield return new object[] { new CsField(Cs.Int, "F") { Initializer = new CsExpression(ExpressionKind.Await, "", Values<CsExpression>.Empty) }, "GEN013" };
        yield return new object[] { new CsField(Cs.Int, "F") { Initializer = new CsExpression((ExpressionKind)99, "", Values<CsExpression>.Empty) }, "GEN014" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = new CsBody(Cs.Return()) { Raw = "return;" } }, "GEN022" };
        yield return new object[] { new CsMethod(Cs.Void, "M") { Body = new CsBody(new CsStatement((StatementKind)99)) }, "GEN023" };
        yield return new object[] { new CsTypeDeclaration(TypeKind.Class, "N") { Modifiers = new[] { "readonly" } }, "GEN039" };
        yield return new object[] { new CsTypeDeclaration(TypeKind.Interface, "N") { Modifiers = new[] { "static" } }, "GEN040" };
        yield return new object[] { new CsTypeDeclaration(TypeKind.RecordStruct, "N") { Modifiers = new[] { "ref" } }, "GEN041" };
        yield return new object[] { new CsTypeDeclaration(TypeKind.Interface, "N") { PrimaryParameters = new[] { new CsParameter(Cs.Int, "x") } }, "GEN042" };
        yield return new object[] { new CsTypeDeclaration(TypeKind.Enum, "N") { TypeParameters = new[] { new TypeParameter("T") } }, "GEN043" };
        yield return new object[] { new CsTypeDeclaration((TypeKind)99, "N"), "GEN038" };
    }

    [Theory(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.ValidationTests." +
        "Invalid_structure_returns_stable_diagnostic_and_default_render_throws")]
    [MemberData(nameof(InvalidMembers))]
    public void Invalid_structure_returns_stable_diagnostic_and_default_render_throws(CsMember member, string code)
    {
        var doc = With(member); var errors = doc.Validate(); Assert.Contains(errors, e => e.Code == code && e.Path.StartsWith("Types[0].Members[0]", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(e.Message));
        Assert.Equal(errors, doc.Validate()); var exception = Assert.Throws<GenValidationException>(() => doc.Render()); Assert.Equal(errors, exception.Diagnostics); Assert.Contains(code, exception.Message);
    }

    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.ValidationTests." +
        "Surrounding_validation_is_not_suppressed_by_raw_nodes")]
    public void Surrounding_validation_is_not_suppressed_by_raw_nodes()
    {
        var doc = Cs.Document().Class("Bad;Name").Add(Cs.RawMember("public int X;")).End().Build();
        Assert.Contains(doc.Validate(), e => e.Code == "GEN003"); Assert.Throws<GenValidationException>(() => doc.Render());
        Assert.Contains("class Bad;Name", doc.Render(new RenderOptions { Validate = false }));
    }

    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.ValidationTests." +
        "Null_and_excessively_deep_graphs_fail_without_recursing_forever")]
    public void Null_and_excessively_deep_graphs_fail_without_recursing_forever()
    {
        var doc = With(new CsMethod(Cs.Void, "M") { Parameters = null!, Body = new CsBody { Statements = null! } });
        Assert.Contains(doc.Validate(), d => d.Code == "GEN002");
        Assert.Contains(With(new CsField(null!, "F")).Validate(), d => d.Code == "GEN002");
        CsType deep = Cs.Int; for (int i = 0; i < 150; i++) deep = deep.Array();
        Assert.Contains(With(new CsField(deep, "F")).Validate(), d => d.Code == "GEN001");
        // Public immutable nodes cannot create cycles without bypassing the type system.
        Assert.Throws<GenValidationException>(() => With(new CsField(deep, "F")).Render());
    }

    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.ValidationTests." +
        "File_using_attribute_and_parent_errors_are_actionable")]
    public void File_using_attribute_and_parent_errors_are_actionable()
    {
        var doc = new CsDocument() { Namespace = "a..b", Usings = new[] { new CsUsing("System", Alias: "x", IsStatic: true) }, Attributes = new[] { Cs.Attribute("X") with { Target = "invalid" } }, Types = new[] { new CsTypeDeclaration(TypeKind.Enum, "C") { Members = new CsMember[] { new CsField(Cs.Int, "F") } } } };
        var codes = doc.Validate().Select(d => d.Code).ToArray();
        Assert.Contains("GEN003", codes); Assert.Contains("GEN005", codes); Assert.Contains("GEN006", codes); Assert.Contains("GEN036", codes);
    }
}
