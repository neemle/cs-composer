using Neemle.CSharpComposer.Validation;
using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer;
using Xunit;
namespace Neemle.CSharpComposer.Tests;
public sealed class BoundaryTests
{
    static CsDocument Document(CsMember m)=>new CsDocument("x.cs"){Types=new[]{new CsTypeDeclaration(TypeKind.Class,"C"){Members=new[]{m}}}};
    sealed record ExtensionMember() : CsMember("Extension");
    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.BoundaryTests." +
        "Unknown_extension_nodes_and_unchecked_discriminants_fail_explicitly")]
    public void Unknown_extension_nodes_and_unchecked_discriminants_fail_explicitly()
    {
        var unknown=Document(new ExtensionMember());
        Assert.Contains(unknown.Validate(),d=>d.Code=="GEN055"); Assert.Throws<ArgumentException>(()=>unknown.Render(new RenderOptions{Validate=false}));
        var type=Document(new CsField(new CsType("T"){Form=(TypeForm)99},"F"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>type.Render(new RenderOptions{Validate=false}));
        var expr=Document(new CsField(Cs.Int,"F"){Initializer=new CsExpression((ExpressionKind)99,"",Values<CsExpression>.Empty)});
        Assert.Throws<ArgumentOutOfRangeException>(()=>expr.Render(new RenderOptions{Validate=false}));
        var modifier=Document(new CsField(Cs.Int,"F"){Modifiers=new[]{"unknown"}});
        Assert.Contains("unknown int F",modifier.Render(new RenderOptions{Validate=false}));
    }
    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.BoundaryTests." +
        "Malformed_collections_report_null_paths_instead_of_runtime_failures")]
    public void Malformed_collections_report_null_paths_instead_of_runtime_failures()
    {
        var cases=new CsMember[]{
            new CsTypeDeclaration(TypeKind.Class,"T"){PrimaryParameters=null!,TypeParameters=null!,BaseTypes=null!,Members=null!},
            new CsProperty(Cs.Int,"P"){Accessors=null!,Modifiers=null!},
            new CsProperty(Cs.Int,"P"){Accessors=null!,Expression=Cs.Literal(1),Initializer=Cs.Literal(2)},
            new CsProperty(Cs.Int,"P"){Accessors=null!,Modifiers=new[]{"required"}},
            new CsConstructor("C"){Parameters=null!,Modifiers=new[]{"static"}},
            new CsEvent("System.Action","E"){Accessors=null!,Initializer=Cs.Literal(null)},
            new CsField(new CsType(""){Form=TypeForm.Tuple,Elements=null!},"F"),
            new CsField(Cs.Int,"F"){Initializer=new CsExpression(ExpressionKind.Identifier,"x",null!)},
            new CsMethod(Cs.Void,"M"){Body=null!,Attributes=null!}
        };
        foreach(var item in cases) { var doc=Document(item); Assert.Contains(doc.Validate(),d=>d.Code=="GEN002"); Assert.Throws<GenValidationException>(()=>doc.Render()); }
        var empty=new CsDocument("x.cs"){Usings=null!,Types=null!,Attributes=null!}; Assert.All(empty.Validate(),d=>Assert.Equal("GEN002",d.Code));
        var nullMember=Document(null!); Assert.Contains(nullMember.Validate(),d=>d.Code=="GEN002");
    }
    [Theory(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.BoundaryTests." +
        "Malformed_expression_operand_counts_are_diagnostics")]

    [InlineData(ExpressionKind.Identifier,1)] [InlineData(ExpressionKind.Literal,1)] [InlineData(ExpressionKind.Raw,1)]
    [InlineData(ExpressionKind.Member,0)] [InlineData(ExpressionKind.Assignment,1)] [InlineData(ExpressionKind.Invocation,0)]
    public void Malformed_expression_operand_counts_are_diagnostics(ExpressionKind kind,int count)
    {
        var expr=new CsExpression(kind,"value",new Values<CsExpression>(Enumerable.Repeat(Cs.Literal(1),count)));
        Assert.Contains(Document(new CsField(Cs.Int,"F"){Initializer=expr}).Validate(),d=>d.Code=="GEN013");
    }
    [Fact] public void Rich_accessors_global_usings_named_attributes_and_constrained_expression_body_compile()
    {
        var accessor=new CsAccessor("get"){Attributes=new[]{Cs.Attribute("System.Runtime.CompilerServices.CompilerGenerated")}};
        var doc=Cs.File("x.cs").Using("System",isGlobal:true).Class("C").Public()
            .Property<int>("P").Accessor(accessor).Init().Initialize(Cs.Literal(1)).End()
            .Method(Cs.TypeParameter("T"),"Make").Public().Generic("T",Cs.Constraint("new()")).Expression(Cs.RawExpression("new T()")).End()
            .Field<int>("Family").Protected().Internal().End()
            .Field<int>("PrivateFamily").Private().Protected().End()
            .Method<int>("Old").Attribute(Cs.Attribute("System.Obsolete",new AttributeArgument(Cs.String("message"),"message",true))).Expression(Cs.Literal(1)).End()
            .Field(Cs.Tuple(new TupleElement(Cs.Int),new TupleElement(Cs.Bool)),"Pair").End()
            .Field(Cs.Type<Environment.SpecialFolder>(),"Folder").End()
            .Add(Cs.RawMember(""))
            .End().Build();
Assert.StartsWith("global using System;",doc.Render());
        Assert.Contains("message: \"message\"",doc.Render()); Assert.Contains("where T : new()\n    => new T();",doc.Render());
        Assert.True(new Values<int>(1).Equals((object)new Values<int>(1)));
        Assert.Equal("global::GlobalFixture",Cs.Type<GlobalFixture>().Name);
    }
    [Fact] public void Receiver_parentheses_preserve_await_and_assignment_precedence()
    {
        var doc=Cs.File("x.cs").Class("C")
            .Method(CsType.String,"A").Expression(Cs.Await(Cs.Id("Fetch").Call()).Member("ToString").Call()).End()
            .Method(Cs.Int,"B").Expression(Cs.Assign(Cs.Id("left"),Cs.Id("right")).Member("Value")).End().End().Build();
        Assert.Contains("(await Fetch()).ToString()",doc.Render()); Assert.Contains("(left = right).Value",doc.Render());
    }
    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.BoundaryTests." +
        "Missing_operands_nullable_elements_and_partial_methods_have_defined_boundaries")]
    public void Missing_operands_nullable_elements_and_partial_methods_have_defined_boundaries()
    {
        var invalid = new CsMember[] {
            new CsField(new CsType(""),"F"),
            new CsField(new CsType(""){Form=TypeForm.Nullable},"F"),
            new CsMethod(Cs.Void,"M"){Body=Cs.Body(Cs.Statement(null!))},
            new CsConstructor("C"){Body=null!},
            new CsTypeDeclaration(TypeKind.Enum,"E"){TypeParameters=null!},
            new CsProperty(Cs.Int,"P"){Modifiers=new[]{"required"},Expression=Cs.Literal(1)}
        };
        foreach (var member in invalid) Assert.Throws<GenValidationException>(()=>Document(member).Render());
        var partial=Cs.File("partial.cs").Class("C").Partial().Method(Cs.Void,"Hook").Partial().End().End().Build();
        var required=Cs.File("required.cs").Class("Mutable").Public().Property<int>("Value").Public().Required().Get().Set().End().End().Build();
    }
    [Fact(DisplayName =
        "[SEC][UC-003] " +
        "Neemle.CSharpComposer.Tests.BoundaryTests." +
        "Depth_limits_cover_declaration_body_and_expression_graphs")]
    public void Depth_limits_cover_declaration_body_and_expression_graphs()
    {
        CsExpression expression=Cs.Id("x"); for(int i=0;i<150;i++) expression=expression.Member("Next");
        Assert.Contains(Document(new CsField(Cs.Int,"F"){Initializer=expression}).Validate(),d=>d.Code=="GEN001");
        CsMember member=new CsMethod(Cs.Void,"M"){Body=Cs.Body()};
        for(int i=0;i<127;i++) member=new CsTypeDeclaration(TypeKind.Class,"Nested"){Members=new[]{member}};
        Assert.Contains(Document(member).Validate(),d=>d.Code=="GEN001" && d.Path.EndsWith(".Body",StringComparison.Ordinal));
        for(int i=0;i<3;i++) member=new CsTypeDeclaration(TypeKind.Class,"Nested"){Members=new[]{member}};
        Assert.Contains(Document(member).Validate(),d=>d.Code=="GEN001" && !d.Path.EndsWith(".Body",StringComparison.Ordinal));
    }
}
