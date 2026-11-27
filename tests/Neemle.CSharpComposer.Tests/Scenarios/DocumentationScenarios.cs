using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Structured documentation comments, cref references and nullable type bindings.
public static class DocumentationScenarios
{
    static CsDocument Doc(CsXmlNode xml) => new()
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Comments = new[] { new CsComment("", CommentKind.Documentation) { Documentation = xml } } } }
    };

    static CsXmlNode Element(string name, params CsXmlNode[] content) => new(XmlNodeKind.Element, name) { Content = content };

    static CsXmlNode Text(string value) => new(XmlNodeKind.Text, null, value);

    static CsDocument See(CsCref cref) => Doc(Element("summary", new CsXmlNode(XmlNodeKind.EmptyElement, "see") { Attributes = new[] { new CsXmlAttribute("cref", null, cref) } }));

    static CsCref Named(string type) => new(CrefKind.Name) { Type = Cs.Type(type) };

    static CsDocument Nullable(CsType element, CsLanguageVersion version, params CsMember[] extra) =>
        new CsDocument(version) { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsField(element.Nullable(), "F") }.Concat(extra).ToArray() } } };

    public static IEnumerable<Scenario> All()
    {
        // Documentation payloads.
        yield return new("doc.parsed", () => Src("/// <summary>A <see cref=\"List{T}.Add(T)\"/> and <see cref=\"operator +(C, C)\"/>, <see cref=\"explicit operator int(C)\"/>, <see cref=\"this[int]\"/>.</summary>\n/// <remarks><![CDATA[x]]><!-- c --><?pi data?></remarks>\nclass C { }\n"), "/// <summary>A <see cref=\"List{T}.Add(T)\" /> and <see cref=\"operator +(C, C)\" />, <see cref=\"explicit operator int(C)\" />, <see cref=\"this[int]\" />.</summary>\n/// <remarks><![CDATA[x]]><!-- c --><?pi data?></remarks>\nclass C\n{\n}\n");
        yield return new("doc.payload-on-line-comment", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Comments = new[] { new CsComment("// x") { Documentation = Text("x") } } } } }, "invalid: GEN075 Comment nodes are leaves.");
        yield return new("doc.block-flag-on-line-comment", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Comments = new[] { new CsComment("// x") { BlockDocumentation = true } } } } }, "invalid: GEN065 XML payload belongs to documentation comments.");
        yield return new("doc.unknown-kind", () => Doc(new CsXmlNode((XmlNodeKind)99)), "invalid: GEN092 Unknown XML node kind.");
        yield return new("doc.element-bad-name", () => Doc(Element("1bad")), "invalid: GEN092 Invalid XML name.");
        yield return new("doc.element-null-name", () => Doc(new CsXmlNode(XmlNodeKind.Element)), "invalid: GEN092 Invalid XML name.");
        yield return new("doc.processing-xml", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.ProcessingInstruction, "XML", "v"))), "invalid: GEN092 Processing instruction target cannot be xml.");
        yield return new("doc.text-null", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.Text))), "invalid: GEN002 Required node or collection is null.");
        yield return new("doc.name-on-text", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.Text, "n", "v"))), "invalid: GEN065 Payload belongs to another XML kind.");
        yield return new("doc.value-on-element", () => Doc(new CsXmlNode(XmlNodeKind.Element, "summary", "v")), "invalid: GEN065 Payload belongs to another XML kind.");
        yield return new("doc.content-on-empty", () => Doc(new CsXmlNode(XmlNodeKind.EmptyElement, "br") { Content = new[] { Text("x") } }), "invalid: GEN065 Payload belongs to another XML kind.");
        yield return new("doc.attributes-on-fragment", () => Doc(new CsXmlNode(XmlNodeKind.Fragment) { Attributes = new[] { new CsXmlAttribute("a", "b") }, Content = new[] { Element("summary") } }), "invalid: GEN065 Payload belongs to another XML kind.");
        yield return new("doc.cdata-end", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.CData, null, "a]]>b"))), "invalid: GEN092 XML value contains its closing delimiter.");
        yield return new("doc.comment-dashes", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.Comment, null, "a--b"))), "invalid: GEN092 XML value contains its closing delimiter.");
        yield return new("doc.comment-trailing-dash", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.Comment, null, "a-"))), "invalid: GEN092 XML value contains its closing delimiter.");
        yield return new("doc.processing-end", () => Doc(Element("summary", new CsXmlNode(XmlNodeKind.ProcessingInstruction, "pi", "a?>b"))), "invalid: GEN092 XML value contains its closing delimiter.");
        yield return new("doc.nodes", () => Doc(new CsXmlNode(XmlNodeKind.Fragment) { Content = new[] { Element("summary", Text("a"), new CsXmlNode(XmlNodeKind.CData, null, "b"), new CsXmlNode(XmlNodeKind.Comment, null, "c"), new CsXmlNode(XmlNodeKind.ProcessingInstruction, "pi", "d"), new CsXmlNode(XmlNodeKind.ProcessingInstruction, "pj", "")) } }), "/// <summary>a<![CDATA[b]]><!--c--><?pi d?><?pj?></summary>\nclass C\n{\n}\n");
        yield return new("doc.duplicate-attributes", () => Doc(new CsXmlNode(XmlNodeKind.Element, "param") { Attributes = new[] { new CsXmlAttribute("name", "a"), new CsXmlAttribute("name", "b") } }), "invalid: GEN092 Duplicate XML attributes.");
        yield return new("doc.attribute-bad-name", () => Doc(new CsXmlNode(XmlNodeKind.Element, "param") { Attributes = new[] { new CsXmlAttribute("1name", "a") } }), "invalid: GEN092 Invalid XML name.");
        yield return new("doc.attribute-empty", () => Doc(new CsXmlNode(XmlNodeKind.Element, "param") { Attributes = new[] { new CsXmlAttribute("name") } }), "invalid: GEN065 Attribute needs text or a cref reference.");
        yield return new("doc.attribute-both", () => Doc(new CsXmlNode(XmlNodeKind.Element, "see") { Attributes = new[] { new CsXmlAttribute("cref", "x", Named("C")) } }), "invalid: GEN065 Attribute needs text or a cref reference.");
        yield return new("doc.attribute-reference-not-cref", () => Doc(new CsXmlNode(XmlNodeKind.Element, "see") { Attributes = new[] { new CsXmlAttribute("href", null, Named("C")) } }), "invalid: GEN065 Attribute needs text or a cref reference.");
        yield return new("doc.depth", () =>
        {
            var node = Text("x");
            for (int i = 0; i < 135; i++) node = Element("p", node);
            return Doc(node);
        }, "invalid: GEN001 Node graph exceeds128 or contains a cycle.");

        // Cref references.
        yield return new("cref.name-qualified", () => See(Named("C") with { Qualifier = Cs.Type("N") }), "invalid: GEN065 Cref payload belongs to another reference kind.");
        yield return new("cref.checked-on-name", () => See(Named("C") with { IsChecked = true }), "invalid: GEN065 Cref payload belongs to another reference kind.");
        yield return new("cref.operator-on-name", () => See(Named("C") with { Operator = OverloadableOperator.Minus }), "invalid: GEN065 Cref payload belongs to another reference kind.");
        yield return new("cref.unknown-operator", () => See(new CsCref(CrefKind.Operator) { Operator = (OverloadableOperator)999 }), "invalid: GEN092 Unknown cref operator.");
        yield return new("cref.unknown-conversion-operator", () => See(new CsCref(CrefKind.Conversion) { Operator = (OverloadableOperator)999, Type = Cs.Int }), "invalid: GEN092 Unknown cref operator. | GEN092 Conversion cref requires implicit or explicit.");
        yield return new("cref.unknown-kind", () => See(new CsCref((CrefKind)99)), "invalid: GEN092 Unknown cref kind.");
        yield return new("cref.type-on-indexer", () => See(new CsCref(CrefKind.Indexer) { Type = Cs.Int, Parameters = new[] { new CsFunctionPointerParameter(Cs.Int) } }), "invalid: GEN065 Type belongs to a named or conversion cref.");
        yield return new("cref.qualified-operator", () => See(new CsCref(CrefKind.Operator) { Qualifier = Cs.Type("C"), Operator = OverloadableOperator.Plus, IsChecked = true, Parameters = new[] { new CsFunctionPointerParameter(Cs.Type("C"), "in"), new CsFunctionPointerParameter(Cs.Type("C"), "ref readonly") } }), "/// <summary><see cref=\"C.operator checked +(in C, ref readonly C)\" /></summary>\nclass C\n{\n}\n");
        yield return new("cref.parameter-modifier", () => See(new CsCref(CrefKind.Indexer) { Parameters = new[] { new CsFunctionPointerParameter(Cs.Int, "params") } }), "invalid: GEN016 Invalid cref parameter modifier.");
        yield return new("cref.checked-old", () => See(new CsCref(CrefKind.Operator) { Operator = OverloadableOperator.Plus, IsChecked = true }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Checked operator cref requires C# 11.");
        yield return new("cref.extension", () => See(new CsCref(CrefKind.Extension) { Qualifier = Cs.Type("E"), TypeArguments = new[] { Cs.Type("T") }, Parameters = new[] { new CsFunctionPointerParameter(Cs.Type("T")) }, Member = Named("M") }), "/// <summary><see cref=\"E.extension{T}(T).M\" /></summary>\nclass C\n{\n}\n");
        yield return new("cref.extension-old", () => See(new CsCref(CrefKind.Extension) { Parameters = new[] { new CsFunctionPointerParameter(Cs.Int) }, Member = Named("M") }).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Extension member cref requires C# 14.");
        yield return new("cref.extension-missing", () => See(new CsCref(CrefKind.Extension)), "invalid: GEN002 Required node or collection is null.");
        yield return new("cref.member-on-name", () => See(Named("C") with { Member = Named("M") }), "invalid: GEN065 Nested cref belongs to an extension reference.");
        yield return new("cref.type-arguments-on-name", () => See(Named("C") with { TypeArguments = new[] { Cs.Int } }), "invalid: GEN065 Nested cref belongs to an extension reference.");
        yield return new("cref.conversion-plus", () => See(new CsCref(CrefKind.Conversion) { Type = Cs.Int }), "invalid: GEN092 Conversion cref requires implicit or explicit.");
        yield return new("cref.conversion", () => See(new CsCref(CrefKind.Conversion) { Type = Cs.Int, Operator = OverloadableOperator.Explicit, IsChecked = true, Parameters = new[] { new CsFunctionPointerParameter(Cs.Type("C")) } }), "/// <summary><see cref=\"explicit operator checked int(C)\" /></summary>\nclass C\n{\n}\n");
        yield return new("cref.implicit", () => See(new CsCref(CrefKind.Conversion) { Type = Cs.Int, Operator = OverloadableOperator.Implicit }), "/// <summary><see cref=\"implicit operator int\" /></summary>\nclass C\n{\n}\n");

        // Nullable annotations need C# 8, unconstrained T? needs C# 9.
        yield return new("nullable.reference-old", () => Nullable(Cs.Type("string"), CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.object-old", () => Nullable(Cs.Type("object"), CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.value-old", () => Nullable(Cs.Int, CsLanguageVersion.CSharp7_3), "class C\n{\n    int? F;\n}\n");
        yield return new("nullable.native-old", () => Nullable(Cs.Type("nint"), CsLanguageVersion.CSharp9), "class C\n{\n    nint? F;\n}\n");
        yield return new("nullable.array-old", () => Nullable(Cs.Int.Array(), CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.tuple-old", () => Nullable(Cs.Tuple(new TupleElement(Cs.Int), new TupleElement(Cs.Int)), CsLanguageVersion.CSharp7_3), "class C\n{\n    (int, int)? F;\n}\n");
        yield return new("nullable.pointer-old", () => Nullable(Cs.Int.Pointer(), CsLanguageVersion.CSharp7_3), "class C\n{\n    int*? F;\n}\n");
        yield return new("nullable.generic-system-value", () => Nullable(Cs.Type("System.ValueTuple").Generic(Cs.Int, Cs.Int), CsLanguageVersion.CSharp7_3), "class C\n{\n    System.ValueTuple<int, int>? F;\n}\n");
        yield return new("nullable.generic-system-reference", () => Nullable(Cs.Type("global::System.Collections.Generic.List").Generic(Cs.Int), CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.generic-unknown-system", () => Nullable(Cs.Type("System.Nope").Generic(Cs.Int), CsLanguageVersion.CSharp7_3), "class C\n{\n    System.Nope<int>? F;\n}\n");
        yield return new("nullable.generic-declared", () => Nullable(Cs.Type("C").Generic(Cs.Int), CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.generic-null-element", () => Nullable(new CsType("") { Form = TypeForm.Generic, Arguments = new[] { Cs.Int } }, CsLanguageVersion.CSharp7_3), "invalid: GEN002 Required node or collection is null.");
        yield return new("nullable.generic-null-arguments", () => Nullable(Cs.Type("C").Generic(Cs.Int) with { Arguments = null! }, CsLanguageVersion.CSharp7_3), "invalid: GEN002 Required node or collection is null.");
        yield return new("nullable.nested-declared", () => Nullable(new CsType("N") { Form = TypeForm.Nested, Element = Cs.Type("C") }, CsLanguageVersion.CSharp7_3, new CsTypeDeclaration(TypeKind.Class, "N")), "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.nested-struct", () => Nullable(Cs.Type("S"), CsLanguageVersion.CSharp7_3, new CsTypeDeclaration(TypeKind.Struct, "S")), "class C\n{\n    S? F;\n\n    struct S\n    {\n    }\n}\n");
        yield return new("nullable.runtime-value", () => Nullable(Cs.Type("global::System.DateTime"), CsLanguageVersion.CSharp7_3), "class C\n{\n    global::System.DateTime? F;\n}\n");
        yield return new("nullable.runtime-reference", () => Nullable(Cs.Type("System.Uri"), CsLanguageVersion.CSharp7_3), "class C\n{\n    System.Uri? F;\n}\n");
        yield return new("nullable.runtime-unknown", () => Nullable(Cs.Type("System.Nope"), CsLanguageVersion.CSharp7_3), "class C\n{\n    System.Nope? F;\n}\n");
        yield return new("nullable.unknown", () => Nullable(Cs.Type("Unknown"), CsLanguageVersion.CSharp7_3), "class C\n{\n    Unknown? F;\n}\n");
        yield return new("nullable.namespace-declared", () => new CsDocument(CsLanguageVersion.CSharp7_3)
        {
            Items = new CsNode[] { new CsNamespace("A", new CsNode[] { new CsNamespace("B", new CsNode[] { new CsTypeDeclaration(TypeKind.Class, "D"), new CsDelegate(Cs.Void, "Handler"), new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsField(Cs.Type("A.B.D").Nullable(), "F"), new CsField(Cs.Type("Handler").Nullable(), "G"), new CsField(Cs.Type("B.D").Nullable(), "H") } } }) }) }
        }, "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.document-namespace", () => new CsDocument(CsLanguageVersion.CSharp7_3) { Namespace = "N", Types = new[] { new CsTypeDeclaration(TypeKind.Struct, "S"), new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsField(Cs.Type("N.S").Nullable(), "F") } } } }, "invalid: GEN073 File-scoped namespace requires C# 10.");
        yield return new("nullable.top-delegate", () => new CsDocument(CsLanguageVersion.CSharp7_3) { Items = new CsNode[] { new CsDelegate(Cs.Void, "D"), new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsField(Cs.Type("D").Nullable(), "F") } } } }, "invalid: GEN073 Nullable annotation requires C# 8.");
        yield return new("nullable.type-parameter-forms", () => new CsDocument(CsLanguageVersion.CSharp8)
        {
            Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { TypeParameters = new[] { new TypeParameter("A") { Constraints = new[] { Cs.Constraint("struct") } }, new TypeParameter("B") { Constraints = new[] { Cs.Constraint("class") } }, new TypeParameter("D") { Constraints = new[] { Cs.Constraint(Cs.Type("I")) } }, new TypeParameter("E") },
                Members = new CsMember[] { new CsField(Cs.TypeParameter("A").Nullable(), "F"), new CsField(Cs.TypeParameter("B").Nullable(), "G"), new CsField(Cs.TypeParameter("D").Nullable(), "H"), new CsField(Cs.TypeParameter("E").Nullable(), "I") } } }
        }, "invalid: GEN073 Unconstrained nullable type parameter requires C# 9.");
        yield return new("nullable.type-parameter-null-constraints", () => new CsDocument(CsLanguageVersion.CSharp8) { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { TypeParameters = new[] { new TypeParameter("T") { Constraints = null! } } } } }, "invalid: GEN002 Required node or collection is null.");
        yield return new("nullable.method-type-parameter", () => new CsDocument(CsLanguageVersion.CSharp8) { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsMethod(Cs.TypeParameter("T").Nullable(), "M") { TypeParameters = new[] { new TypeParameter("T") { Constraints = new CsConstraint[] { null!, Cs.Constraint("unmanaged") } } }, Body = Cs.Body(Cs.Return(Cs.Literal(null))) } } } } }, "invalid: GEN002 Required node or collection is null.");
        yield return new("nullable.delegate-type-parameter", () => new CsDocument(CsLanguageVersion.CSharp8) { Items = new CsNode[] { new CsDelegate(Cs.TypeParameter("T").Nullable(), "D") { TypeParameters = new[] { new TypeParameter("T") { Constraints = new[] { Cs.Constraint("class?") } } } } } }, "delegate T? D<T>()\n    where T : class?\n;\n");
        yield return new("nullable.extension-type-parameter", () => new CsDocument(CsLanguageVersion.CSharp14) { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "E") { Modifiers = new[] { "static" }, Members = new CsMember[] { new CsExtension(Cs.TypeParameter("T").Nullable(), "x") { TypeParameters = new[] { new TypeParameter("T") } } } } } }, "static class E\n{\n    extension<T>(T? x)\n    {\n    }\n}\n");
        yield return new("nullable.null-type-parameter", () => new CsDocument(CsLanguageVersion.CSharp8) { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { TypeParameters = new TypeParameter[] { null! } } } }, "invalid: GEN002 Required node or collection is null.");
        yield return new("nullable.nested-types-null-members", () => new CsDocument(CsLanguageVersion.CSharp8) { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = null! } } }, "invalid: GEN002 Required node or collection is null.");
        yield return new("nullable.deep", () =>
        {
            CsType type = Cs.Type("C");
            for (int i = 0; i < 135; i++) type = type.Generic(Cs.Int);
            return Nullable(type, CsLanguageVersion.CSharp7_3);
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        yield return new("nullable.deep-nested", () =>
        {
            CsType type = Cs.Type("C");
            for (int i = 0; i < 135; i++) type = new CsType("N") { Form = TypeForm.Nested, Element = type };
            return Nullable(type, CsLanguageVersion.CSharp7_3);
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");
    }
}
