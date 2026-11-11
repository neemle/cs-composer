using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer;
using Xunit;
using System.Globalization;
namespace Neemle.CSharpComposer.Tests;
public sealed class RendererTests
{
    public const string Golden="namespace Example;\n\npublic partial class User\n{\n    public int Id { get; init; }\n\n    public required string Name { get; init; }\n}\n";
    [Fact] public void Dto_default_is_exact_and_source_text_matches_encoding()
    {
        var doc=DomTests.Dto(); Assert.Equal(Golden,doc.Render());
        Assert.Equal("\n",new CsDocument().Render());
    }
    [Fact] public void Options_normalize_usings_namespace_nullable_and_indent()
    {
        var doc=Cs.Document().Namespace("class.名前").Using("Z").Using("System").Using("Z").Using("System.Math",isStatic:true).Using("System.String",alias:"string").Class("event").Public().End().Build();
        var source=doc.Render(new RenderOptions { Indent="\t",NamespaceStyle=NamespaceStyle.BlockScoped,Nullable=NullableMode.Enable });
        Assert.Equal("#nullable enable\n\nusing @string = System.String;\nusing System;\nusing Z;\nusing static System.Math;\n\nnamespace @class.名前\n{\n\tpublic class @event\n\t{\n\t}\n}\n",source);
        Assert.StartsWith("using Z;\nusing System;",doc.Render(new RenderOptions { SortUsings=false }));
        Assert.StartsWith("#nullable disable\n",doc.Render(new RenderOptions { Nullable=NullableMode.Disable }));
        Assert.StartsWith("#nullable restore\n",doc.Render(new RenderOptions { Nullable=NullableMode.Restore }));
        Assert.Throws<ArgumentException>(()=>doc.Render(new RenderOptions { Indent=";" }));
    }
    [Fact(DisplayName =
        "[SEC][UC-002] " +
        "Neemle.CSharpComposer.Tests.RendererTests." +
        "Rendering_is_nonmutating_parallel_and_culture_invariant")]
    public void Rendering_is_nonmutating_parallel_and_culture_invariant()
    {
        var doc=DomTests.Dto(); int hash=doc.GetHashCode();
        Parallel.For(0,24,_=>Assert.Equal(Golden,doc.Render()));
        var old=CultureInfo.CurrentCulture;
        try { foreach(var culture in new[]{"tr-TR","fr-FR","uk-UA"}) { CultureInfo.CurrentCulture=new CultureInfo(culture); Assert.Equal(Golden,doc.Render()); Assert.Equal("1.25M",Cs.Literal(1.25m).Text); } }
        finally { CultureInfo.CurrentCulture=old; }
        Assert.Equal(hash,doc.GetHashCode()); Assert.Equal(DomTests.Dto(),doc);
    }
    [Fact] public void Modifiers_and_constraints_normalize_without_reordering_members()
    {
        var doc=Cs.Document().Class("C").Partial().Public().Abstract().Generic("T",Cs.Constraint("new()"),Cs.Constraint(Cs.Type("System.IDisposable")),Cs.Constraint("class"))
            .Field<int>("Z").Static().Public().Initialize(Cs.Literal(1)).End().Field<int>("A").Static().Public().Initialize(Cs.Id("Z")).End().End().Build();
        var s=doc.Render(); Assert.StartsWith("public abstract partial class C<T>\n    where T : class, System.IDisposable, new()",s);
        Assert.True(s.IndexOf("int Z",StringComparison.Ordinal)<s.IndexOf("int A",StringComparison.Ordinal));
    }
    [Theory(DisplayName =
        "[SEC][UC-002] " +
        "Neemle.CSharpComposer.Tests.RendererTests." +
        "String_literals_escape_control_characters")]

    [InlineData("\"\\\n\r\t\0\u2028\u2029\u0001", "\"\\\"\\\\\\n\\r\\t\\0\\u2028\\u2029\\u0001\"")]
    [InlineData("Привіт 世界 '", "\"Привіт 世界 '\"")]
    public void String_literals_escape_control_characters(string value,string expected) => Assert.Equal(expected,Cs.String(value).Text);
    [Fact(DisplayName =
        "[SEC][UC-002] " +
        "Neemle.CSharpComposer.Tests.RendererTests." +
        "Numeric_and_character_literals_are_explicit_and_invariant")]
    public void Numeric_and_character_literals_are_explicit_and_invariant()
    {
        var values=new object?[]{null,true,false,'\'', '"','\\','\n',1.5f,2.5d,3.5m,4UL,5L,6U,(byte)7,(sbyte)-8,(short)-9,(ushort)10,11};
        var expected=new[]{"null","true","false","'\\''","'\"'","'\\\\'","'\\n'","1.5F","2.5D","3.5M","4UL","5L","6U","7","-8","-9","10","11"};
        Assert.Equal(expected,values.Select(v=>Cs.Literal(v).Text));
        Assert.Equal("\"\\ud800\"",Cs.Literal("\ud800").Text);
        Assert.Throws<ArgumentException>(()=>Cs.Literal(double.NaN)); Assert.Throws<ArgumentException>(()=>Cs.Literal(float.PositiveInfinity)); Assert.Throws<ArgumentException>(()=>Cs.Literal(DateTime.Now));
    }
    [Fact(DisplayName =
        "[SEC][UC-002] " +
        "Neemle.CSharpComposer.Tests.RendererTests." +
        "Raw_escape_positions_normalize_lines_but_remain_source_data")]
    public void Raw_escape_positions_normalize_lines_but_remain_source_data()
    {
        var doc=Cs.Document().Class("C")
            .Add(Cs.RawMember("// raw member\r\npublic int A;\r\n"))
            .Field(Cs.RawType("System.Int32"),"B").Initialize(Cs.RawExpression("1 + 2")).End()
            .Method(Cs.Void,"M").Body(Cs.RawStatement("// raw statement\r\nB++;"),Cs.Return()).End()
            .Method<int>("N").RawBody("return B;\r\n").End().End().Build();
        var s=doc.Render(); Assert.DoesNotContain("\r",s); Assert.Contains("public int A;\n",s); Assert.Contains("System.Int32 B = 1 + 2;",s); Assert.Contains("B++;\n        return;",s); Assert.Contains("        return B;",s);
    }
}
