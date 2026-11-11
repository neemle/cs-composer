using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using System.Reflection;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

public sealed class FeatureContextTests
{
    static CsDocument InInterface(CsMember member) => new CsDocument("X", CsLanguageVersion.CSharp7_3)
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Interface, "C") { Members = new[] { member } } }
    };

    [Fact]
    public void Older_target_builders_retain_ordinary_fields_events_and_parameterized_struct_constructors()
    {
        var type = Cs.File("X", CsLanguageVersion.CSharp7_3).Struct("C");
        type.Field<int>("Count").Public().ReadOnly().End();
        type.Event(Cs.Type<Action>(), "Changed").Public().End();
        type.Constructor().Public().Parameter(Cs.Int, "count").Body().End();
        var document = type.End().Build();
        Assert.DoesNotContain(document.Validate(), error => error.Code == "GEN073");
        Assert.Contains("public C(int count)", document.Render());
    }

    [Fact(DisplayName = "[SEC][UC-003] Internal builder ownership cannot silently select a fallback language")]
    public void Builder_owner_context_invariant_fails_with_an_actionable_error()
    {
        var builder = Cs.File("X").Class("C").Field<int>("Count");
        var method = builder.GetType().BaseType!.GetMethod("Context", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(builder, new[] { new object() }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("owning document/type version context", error.InnerException!.Message);
    }

    [Fact]
    public void Partial_method_feature_guards_distinguish_legacy_raw_and_enhanced_signatures()
    {
        var method = new CsMethod(Cs.Void, "Run") { Modifiers = new[] { "partial" } };
        var legacy = new[] { method, method with { ReturnType = Cs.RawType("void") },
            method with { Parameters = new[] { new CsParameter(Cs.Int, "x") { Modifier = "ref" } } },
            method with { ReturnType = null!, Parameters = null! },
            method with { Parameters = new CsParameter[] { null! } } };
        foreach (var item in legacy)
            Assert.DoesNotContain(Document(item).Validate(), error => error.Code == "GEN073");
        var enhanced = new[] { method with { ReturnType = Cs.Int },
            method with { Parameters = new[] { new CsParameter(Cs.Int, "x") { Modifier = "out" } } } };
        foreach (var item in enhanced)
            Assert.Contains(Document(item).Validate(), error => error.Code == "GEN073");
    }

    static CsDocument Document(CsMethod method) => new CsDocument("X", CsLanguageVersion.CSharp8)
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new[] { method } } }
    };

    [Fact(DisplayName = "[SEC][UC-003] Interface feature checks inspect accessor bodies without dereferencing nulls")]
    public void Interface_property_and_event_shapes_have_contextual_feature_guards()
    {
        var property = new CsProperty(Cs.Int, "Number");
        var malformed = new[] { property with { Accessors = null! },
            property with { Accessors = new CsAccessor[] { null! } } };
        foreach (var item in malformed)
            Assert.Contains(InInterface(item).Validate(), error => error.Code == "GEN002");
        var implemented = property with { Accessors = new[] { new CsAccessor("get") { Body = Cs.Body() } } };
        Assert.Contains(InInterface(implemented).Validate(), error => error.Code == "GEN073");
        var missing = new CsEvent(Cs.Type<Action>(), "Changed") { Accessors = null! };
        Assert.Contains(InInterface(missing).Validate(), error => error.Code == "GEN002");
    }

    [Fact]
    public void Typed_catch_without_variable_and_recursive_else_depth_are_supported_safely()
    {
        var handler = Cs.Catch(Cs.Type<Exception>(), Cs.Body(Cs.Return()));
        Assert.Contains("catch (global::System.Exception)",
            StructuredLogicTests.Document(Cs.Try(Cs.Body(), new[] { handler })).Render());
        CsStatement nested = Cs.Block();
        for (int i = 0; i < 150; i++) nested = Cs.If(Cs.Id("condition"), Cs.Body(), nested);
        Assert.Contains(StructuredLogicTests.Document(nested).Validate(), error => error.Code == "GEN001");
        var error = Assert.Throws<GenValidationException>(() => Cs.File("X", (CsLanguageVersion)999));
        Assert.Contains("7.3", error.Message);
        Assert.Contains("14", error.Message);
    }

    [Fact(DisplayName = "[SEC][UC-003] Feature contexts preserve null diagnostics and record-struct initialization")]
    public void Incomplete_struct_signatures_and_ref_bases_remain_diagnostic_inputs()
    {
        var constructor = new CsConstructor("C") { Parameters = null!, Body = Cs.Body() };
        var structure = new CsTypeDeclaration(TypeKind.Struct, "C") { Members = new CsMember[] { constructor } };
        var document = new CsDocument("X") { Types = new[] { structure } };
        Assert.Contains(document.Validate(), error => error.Code == "GEN002");
        var invalid = structure with { Modifiers = new[] { "ref" }, BaseTypes = null! };
        Assert.Contains((document with { Types = new[] { invalid } }).Validate(), error => error.Code == "GEN002");
        var record = new CsTypeDeclaration(TypeKind.RecordStruct, "C")
        { Members = new CsMember[] { new CsField(Cs.Int, "number") { Initializer = Cs.Literal(1) } } };
        var unavailable = document with { LanguageVersion = CsLanguageVersion.CSharp9, Types = new[] { record } };
        Assert.Contains(unavailable.Validate(), error => error.Code == "GEN073" && error.Path.EndsWith(".Initializer"));
    }
}
