using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class DomTests
{
    internal static CsDocument Dto() => Cs.File("User.g.cs").Namespace("Example")
        .Class("User").Public().Partial()
        .Property<int>("Id").Public().Get().Init().End()
        .Property<string>("Name").Public().Required().Get().Init().End().End().Build();
    [Fact] public void Fluent_and_object_model_have_equal_structure_and_hash()
    {
        var dom = new CsDocument("User.g.cs") { Namespace="Example", Types=new[] { new CsTypeDeclaration(TypeKind.Class,"User") { Modifiers=new[]{"public","partial"}, Members=new CsMember[] {
            new CsProperty(CsType.Int32,"Id") { Modifiers=new[]{"public"}, Accessors=new[]{new CsAccessor("get"),new CsAccessor("init")} },
            new CsProperty(CsType.String,"Name") { Modifiers=new[]{"public","required"}, Accessors=new[]{new CsAccessor("get"),new CsAccessor("init")} }
        } } } };
        Assert.Equal(dom,Dto()); Assert.Equal(dom.GetHashCode(),Dto().GetHashCode());
        Assert.Equal("int",dom.FindType("User")!.FindProperty("Id")!.Type.Name);
        Assert.Null(dom.FindType("Missing")); Assert.Null(dom.Types[0].FindProperty("Missing"));
        Assert.NotEqual(dom,dom with { FileName="Other.g.cs" });
    }
    [Fact(DisplayName =
        "[SEC][UC-001] " +
        "Neemle.CSharpComposer.Tests.DomTests." +
        "Built_documents_do_not_track_builder_or_supplied_array_mutations")]
    public void Built_documents_do_not_track_builder_or_supplied_array_mutations()
    {
        var source=new CsMember[]{new CsField(Cs.Int,"A")};
        var fragment=new CsTypeDeclaration(TypeKind.Class,"C") { Members=source };
        var builder=Cs.File("x.cs").Add(fragment); var before=builder.Build();
        source[0]=new CsField(Cs.Int,"Changed"); builder.Class("D").End();
        Assert.Single(before.Types); Assert.Equal("A",before.Types[0].Members[0].Name);
        Assert.Equal(2,builder.Build().Types.Count);
        var child=Cs.File("child.cs").Class("Child"); var one=child.Build(); child.Property<int>("P").Get().End();
        Assert.Empty(one.Members); Assert.Single(child.Build().Members);
    }
    [Fact(DisplayName =
        "[SEC][UC-001] " +
        "Neemle.CSharpComposer.Tests.DomTests." +
        "Immutable_rename_has_one_name_and_changes_output_and_equality")]
    public void Immutable_rename_has_one_name_and_changes_output_and_equality()
    {
        var p=new CsProperty(Cs.Int,"Before") { Accessors=new[]{new CsAccessor("get")} };
        var renamed=p with { Name="After" };
        Assert.Equal("Before",p.Name); Assert.NotEqual(p,renamed);
        var doc=new CsDocument("x.cs") { Types=new[]{new CsTypeDeclaration(TypeKind.Class,"C") { Members=new CsMember[]{renamed} }} };
        Assert.Contains("int After",doc.Render()); Assert.DoesNotContain("Before",doc.Render());
        Assert.Equal(renamed,new CsProperty(Cs.Int,"After") { Accessors=new[]{new CsAccessor("get")} });
    }
    [Fact] public void Collection_condition_and_reusable_fragments_compose_without_reordering()
    {
        var fields=new[]{"Z","A"}.Select(n=>(CsMember)new CsField(Cs.Int,n) { Modifiers=new[]{"static"}, Initializer=Cs.Literal(1) });
        var reusable=new CsProperty(Cs.Int,"Value") { Accessors=new[]{new CsAccessor("get")} };
        var doc=Cs.File("x.cs").When(true,f=>f.Using("System")).When(false,f=>f.Using("Missing"))
            .Class("Outer").AddRange(fields).Add(reusable).When(true,t=>t.Partial()).Class("Inner").Add(reusable).End().End().Build();
        Assert.Equal(new[]{"Z","A","Value","Inner"},doc.Types[0].Members.Select(m=>m.Name));
        Assert.Equal(reusable,doc.Types[0].FindType("Inner")!.FindProperty("Value"));
        Assert.Null(doc.Types[0].FindType("Missing")); Assert.Single(doc.Usings);
    }
    [Fact] public void Type_factories_preserve_generic_nested_array_nullable_tuple_pointer_intent()
    {
        Assert.Equal(Cs.Type("global::System.Collections.Generic.List").Generic(Cs.Int),Cs.Type<List<int>>());
        Assert.Equal(Cs.Int.Nullable(),Cs.Type<int?>());
        Assert.Equal(Cs.Int.Array(2),Cs.Type<int[,]>());
        Assert.Equal(Cs.Int.Pointer(),Cs.Type(typeof(int).MakePointerType()));
        Assert.Equal(Cs.Int.Nullable(),Cs.Int.Nullable().Nullable());
        var parameter=typeof(List<>).GetGenericArguments()[0]; Assert.Equal(Cs.TypeParameter("T"),Cs.Type(parameter));
        Assert.Throws<ArgumentException>(()=>Cs.Type(typeof(int).MakeByRefType()));
        Assert.Equal(TypeForm.Nested,Cs.Type<Outer<int>.Inner<string>>().Form);
    }
    public sealed class Outer<T> { public sealed class Inner<U> { } }
    [Fact] public void Value_collections_are_ordered_and_support_nongeneric_enumeration()
    {
        var values=new Values<int>(1,2); Assert.Equal(new Values<int>(new List<int>{1,2}),values);
        Assert.NotEqual(values,new Values<int>(2,1)); Assert.False(values.Equals(null)); Assert.False(values.Equals("x"));
        Assert.Equal(new[]{1,2},((System.Collections.IEnumerable)values).Cast<int>());
        Assert.Throws<ArgumentNullException>(()=>new Values<int>((int[])null!));
        Assert.Throws<ArgumentNullException>(()=>new Values<int>((IEnumerable<int>)null!));
        Assert.Equal(new Values<string?>(new string?[]{null}).GetHashCode(),new Values<string?>(new string?[]{null}).GetHashCode());
    }
}
