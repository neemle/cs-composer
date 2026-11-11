using Neemle.CSharpComposer.Validation;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class InterfaceValidationTests
{
    static CsDocument Interface(CsMember member) => new CsDocument("Interface.g.cs")
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Interface,"I") { Members = new[] { member } } }
    };
    public static IEnumerable<object[]> InvalidInterfaceMembers()
    {
        yield return new object[] { new CsField(Cs.Int,"Value"), "GEN056" };
        yield return new object[] { new CsConstructor("I"), "GEN057" };
        yield return new object[] { new CsProperty(Cs.Int,"Value") { Accessors=new[]{new CsAccessor("get"),new CsAccessor("set")}, Initializer=Cs.Literal(1) }, "GEN058" };
        yield return new object[] { new CsEvent("System.Action","Changed") { Initializer=Cs.RawExpression("delegate { }") }, "GEN059" };
        yield return new object[] { new CsMethod(Cs.Void,"Hidden") { Modifiers=new[]{"private"} }, "GEN060" };
        yield return new object[] { new CsMethod(Cs.Void,"StaticMissing") { Modifiers=new[]{"static"} }, "GEN061" };
    }
    [Theory(DisplayName="[SEC][UC-003] Illegal interface members return stable diagnostics")]
    [MemberData(nameof(InvalidInterfaceMembers))]
    public void Invalid_interface_member_has_diagnostics_and_default_validation_exception(CsMember member,string code)
    {
        var document=Interface(member);
        var errors=document.Validate();
        Assert.Contains(errors,e=>e.Code==code && e.Path=="Types[0].Members[0]" && e.Message.Contains("Interface",StringComparison.Ordinal));
        Assert.Equal(errors,document.Validate());
        Assert.Equal(errors,Assert.Throws<GenValidationException>(()=>document.Render()).Diagnostics);
    }
    [Fact(DisplayName="[SEC][UC-003] Null required accessors stay inside the diagnostic contract")]
    public void Null_accessor_elements_return_diagnostics_even_with_valid_setter_after_them()
    {
        foreach(var accessors in new[]{new CsAccessor[]{null!},new CsAccessor[]{null!,new CsAccessor("set")},new CsAccessor[]{new CsAccessor("get"),null!,new CsAccessor("init")}})
        {
            var property=new CsProperty(Cs.Int,"P") {Modifiers=new[]{"public","required"},Accessors=accessors};
            var type=new CsTypeDeclaration(TypeKind.Class,"C") {Members=new CsMember[]{property}};
            var document=new CsDocument("Required.g.cs") {Types=new[]{type}};
            var errors=document.Validate();
            Assert.Contains(errors,d=>d.Code=="GEN002" && d.Path.Contains(".Accessors[",StringComparison.Ordinal));
            Assert.Equal(errors,document.Validate());
            Assert.Equal(errors,Assert.Throws<GenValidationException>(()=>document.Render()).Diagnostics);
        }
    }
    [Fact(DisplayName="[SEC][UC-003] Valid C# 11 interface state and implementations compile")]
    public void Static_state_constructors_and_default_implementations_are_preserved()
    {
        var document=Cs.File("ValidInterface.g.cs").Interface("I").Public()
            .Field<int>("Constant").Const().Initialize(Cs.Literal(1)).End()
            .Field<int>("Value").Static().Initialize(Cs.Literal(2)).End()
            .Field<int>("Immutable").Static().ReadOnly().Initialize(Cs.Literal(3)).End()
            .Constructor().Static().Body(Cs.Statement(Cs.Assign(Cs.Id("Value"),Cs.Literal(4)))).End()
            .Property<int>("StaticProperty").Static().Get().Set().Initialize(Cs.Literal(5)).End()
            .Property<int>("DefaultProperty").Expression(Cs.Literal(6)).End()
            .Property<int>("AbstractProperty").Get().Set().End()
            .Property<int>("StaticAbstractProperty").Static().Abstract().Get().End()
            .Event("System.Action","StaticChanged").Static().Initialize(Cs.RawExpression("delegate { }")).End()
            .Event("System.Action","Changed").End()
            .Event("System.Action","DefaultChanged").Add(Cs.Body()).Remove(Cs.Body()).End()
            .Method(Cs.Void,"Abstract").End()
            .Method<int>("Default").Body(Cs.Return(Cs.Literal(7))).End()
            .Method<int>("Hidden").Private().Expression(Cs.Literal(8)).End()
            .Method<int>("StaticImplemented").Static().Expression(Cs.Literal(9)).End()
            .Method<int>("StaticAbstract").Static().Abstract().End()
            .Method<int>("StaticVirtual").Static().Virtual().Expression(Cs.Literal(10)).End()
            .Method(Cs.Void,"Native").Private().Static().Extern().Attribute(Cs.Attribute("System.Runtime.InteropServices.DllImport",Cs.Arg(Cs.String("example")))).End()
            .End().Build();
        Assert.Empty(document.Validate());
        Assert.Contains("static I()",document.Render());
        Assert.Contains("static abstract int StaticAbstract();",document.Render());
    }
    [Theory(DisplayName="[SEC][UC-003] Declaration context rejects top-level private and sealed structs")]
    [InlineData(TypeKind.Class,"private","GEN062")]
    [InlineData(TypeKind.Class,"protected","GEN062")]
    [InlineData(TypeKind.Class,"new","GEN062")]
    [InlineData(TypeKind.Struct,"sealed","GEN063")]
    [InlineData(TypeKind.RecordStruct,"abstract","GEN063")]
    public void Invalid_type_modifiers_are_confirmed_by_the_compiler(TypeKind kind,string modifier,string code)
    {
        var document=new CsDocument("InvalidType.g.cs") {Types=new[]{new CsTypeDeclaration(kind,"C"){Modifiers=new[]{modifier}}}};
        Assert.Contains(document.Validate(),d=>d.Code==code && d.Path=="Types[0]");
        Assert.Throws<GenValidationException>(()=>document.Render());
    }
    [Fact(DisplayName="[SEC][UC-003] Nested private and public sealed class modifiers stay valid")]
    public void Valid_type_modifier_contexts_compile()
    {
        var document=Cs.File("ValidType.g.cs").Class("C").Public().Sealed().Class("Nested").Private().End().End()
            .Struct("S").Internal().ReadOnly().End().Build();
        Assert.Empty(document.Validate());     }
}
