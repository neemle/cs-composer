using Neemle.CSharpComposer;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class EngineFeatureTests
{
    internal static CsDocument Features()
    {
        var doc=Cs.File("Features.g.cs").Namespace("Features").Using("System").Using("System.Threading.Tasks")
            .Attribute(Cs.Attribute("System.Reflection.AssemblyMetadata",Cs.Arg(Cs.String("Key")),Cs.Arg(Cs.String("Value"))) with { Target="assembly" });
        var constraints=new[]{Cs.Constraint("new()"),Cs.Constraint("class"),Cs.Constraint(Cs.Type("System.IDisposable"))};
        doc.Class("Base").Public().Partial().Abstract().Generic("T",constraints)
            .Field<int>("_value").Protected().ReadOnly().Initialize(Cs.Literal(1)).End()
            .Constructor().Protected().Parameter(Cs.Int,"value").Body(Cs.Statement(Cs.Assign(Cs.Id("_value"),Cs.Id("value")))).End()
            .Constructor().Static().Body().End()
            .Property<int>("Value").Public().Abstract().Get().End()
            .Event("System.Action","Changed").Public().Abstract().End()
            .Method(Cs.TypeParameter("T"),"Create").Public().Abstract().Generic("U",Cs.Constraint("notnull")).Parameter(Cs.TypeParameter("U"),"value").End()
            .End();
        doc.Class("Derived").Public().Sealed().Base(Cs.Type("Base").Generic(Cs.TypeParameter("T"))).Generic("T",constraints)
            .Attribute(Cs.Attribute("System.Obsolete",Cs.Arg(Cs.String("Use another type")),Cs.Arg(Cs.String("GEN001"),"DiagnosticId")))
            .Constructor().Public().Parameter(Cs.Int,"value").Base(Cs.Id("value")).Body().End()
            .Constructor().Public().This(Cs.Literal(1)).Body().End()
            .Property<int>("Value").Public().Override().Expression(Cs.Id("_value")).End()
            .Event("System.Action","Changed").Public().Override().End()
            .Property<string>("Name").Public().Required().Get().Init().Initialize(Cs.String("名前")).End()
            .Field<int>("_current").Private().End()
            .Property<int>("Current").Public().Get(body:Cs.Body(Cs.Return(Cs.Id("_current")))).Set("private",Cs.Body(Cs.Statement(Cs.Assign(Cs.Id("_current"),Cs.Id("value"))))).End()
            .Method(Cs.TypeParameter("T"),"Create").Public().Override().Sealed().Generic("U").Parameter(Cs.TypeParameter("U"),"value").Expression(Cs.RawExpression("new T()")).End()
            .Method(Cs.Type("Task").Generic(Cs.Int),"Fetch").Public().Async().Body(Cs.Return(Cs.Await(Cs.Id("Task").Member("FromResult").Call(Cs.Literal(7))))).End()
            .Event("System.Action","Custom").Public().Add(Cs.Body()).Remove(Cs.RawBody("// no listeners" )).End()
            .Method(Cs.Void,"Raise").Public().RawBody("Changed?.Invoke();").End()
            .Class("Nested").Public().Partial().End()
            .End();
        doc.Struct("Value").Public().ReadOnly().Field<int>("Number").Public().ReadOnly().End().Method<int>("Read").Public().ReadOnly().Expression(Cs.Id("Number")).End().End();
        doc.Struct("Buffer").Public().Ref().End();
        doc.Record("RecordDefault").Public().Parameter(Cs.Int,"Id").End();
        doc.RecordClass("Person").Public().Parameter(Cs.Int,"Id").Parameter(CsType.String,"Name").End();
        doc.RecordStruct("Point").Public().ReadOnly().Parameter(Cs.Int,"X").Parameter(Cs.Int,"Y").End();
        doc.Interface("ITransform").Public().Generic(new TypeParameter("TIn"){Variance="in"}).Generic(new TypeParameter("TOut"){Variance="out"})
            .Method(Cs.TypeParameter("TOut"),"Convert").Parameter(Cs.TypeParameter("TIn"),"value").End().End();
        doc.Enum("Status").Public().Attribute(Cs.Attribute("System.Flags")).Base(Cs.Int).EnumMember("None",Cs.Literal(0)).EnumMember("class",Cs.Literal(1)).End();
        doc.Class("Types").Public().Unsafe()
            .Field(Cs.Int.Pointer(),"Pointer").Public().End()
            .Field(Cs.Tuple(new TupleElement(Cs.Int,"Id"),new TupleElement(CsType.String,"Name")),"Pair").Public().End()
            .Field(Cs.Type<DomTests.Outer<int>.Inner<string>>(),"NestedGeneric").Public().End()
            .Field(Cs.Type<List<string?[]>>().Nullable(),"Items").Public().End()
            .Field(Cs.Int.Array(2),"Matrix").Public().End()
            .Field(Cs.Int.Nullable(),"Optional").Public().End()
            .Field<int>("Max").Public().Const().Initialize(Cs.Literal(10)).End()
            .Field<int>("State").Private().Volatile().End()
            .Method(Cs.Void,"Take").Public().Static().Parameter(Cs.Int.Array(),"items","params").Body().End()
            .Method<int>("Default").Public().Parameter(Cs.Int,"value",@default:Cs.Literal(42)).Expression(Cs.Id("value")).End()
            .Method(Cs.Void,"Ref").Public().Parameter(Cs.Int,"value","ref").Body(Cs.Statement(Cs.Assign(Cs.Id("value"),Cs.Literal(2)))).End()
            .Method(Cs.Void,"Annotation").Public().Parameter(new CsParameter(Cs.Int,"value") {Attributes=new[]{Cs.Attribute("System.Runtime.InteropServices.In")} }).Body().End()
            .Method<int>("Raw").Public().Expression(Cs.RawExpression("1 + 2")).End()
            .Method(Cs.Void,"RawStatement").Public().Body(Cs.RawStatement("State++;"),Cs.Return()).End()
            .Add(Cs.RawMember("public string RawMember => \"raw\";"))
            .Field(Cs.RawType("System.Int32"),"RawType").Public().End()
            .End();
        doc.Class("Constraints").Public()
            .Method(Cs.Void,"NullableClass").Public().Generic("T",Cs.Constraint("class?")).Body().End()
            .Method(Cs.Void,"Structure").Public().Generic("T",Cs.Constraint("struct")).Body().End()
            .Method(Cs.Void,"Unmanaged").Public().Generic("T",Cs.Constraint("unmanaged")).Body().End()
            .Method(Cs.Void,"NotNull").Public().Generic("T",Cs.Constraint("notnull")).Body().End()
            .Method(Cs.Void,"Dependent").Public().Generic("T").Generic("U",Cs.Constraint(Cs.TypeParameter("T"))).Body().End()
            .End();
        return doc.Build();
    }

[Fact] public void Complete_feature_fixture_preserves_DOM_and_source() { var doc=Features(); Assert.Empty(doc.Validate()); var source=doc.Render(new RenderOptions{Nullable=NullableMode.Enable}); Assert.Contains("return await Task.FromResult(7);",source); Assert.Contains("public sealed override T Create<U>",source); Assert.Contains("[Obsolete",source.Replace("System.Obsolete","Obsolete")); }
}
