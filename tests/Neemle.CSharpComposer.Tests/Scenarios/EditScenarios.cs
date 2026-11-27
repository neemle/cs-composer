using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Path addressing and the insert, replace, remove, move and reformat edits on parsed documents and trees.
public static class EditScenarios
{
    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

    static NodePath Find(CsTree tree, Func<CsNode, bool> match) => tree.Descendants().First(n => match(n.Node)).Path;

    // Inserts `child` into the first node matching `parent`; a negative index counts from the end (-1 appends).
    static CsDocument Insert(string source, Func<CsNode, bool> parent, int index, CsNode child)
    {
        var document = Src(source);
        var path = Find(document, parent);
        return document.Insert(path, index < 0 ? document.At(path).Children.Count + 1 + index : index, child);
    }

    static CsTree InsertTree(string source, Func<CsNode, bool> parent, int index, CsNode child)
    {
        var tree = Cs.ParseTree(source).Root;
        var path = Find(tree, parent);
        return tree.Insert(path, index < 0 ? tree.At(path).Children.Count + 1 + index : index, child);
    }

    static CsSymbol Name(string name) => Cs.Symbol(name);

    static CsDocument Deep(int levels)
    {
        CsType type = Cs.Int;
        for (int i = 0; i < levels; i++) type = type.Array();
        return Field(type);
    }

    public static IEnumerable<Scenario> All()
    {
        // Insertion into every child slot.
        yield return new("insert.comment", () => Insert("class C { int F; }", n => n is CsField, 0, new CsComment("/* c */", CommentKind.Block)), "class C { /* c */\nint F; }");
        yield return new("insert.trivia", () => Insert("class C { int F; }", n => n is CsField, -1, new CsSyntaxTrivia(new CsDisabledText("x\n"), SyntaxTriviaPlacement.Trailing)), "class C { int F;\nx }");
        yield return new("insert.document", () => Insert("class C { }", n => n is CsDocument, -1, new CsTypeDeclaration(TypeKind.Class, "D")), "class C { }\nclass D\n{\n}\n");
        yield return new("insert.namespace", () => Insert("namespace N { class C { } }", n => n is CsNamespace, -1, new CsTypeDeclaration(TypeKind.Class, "D")), "namespace N { class C { }\n    class D\n    {\n    }\n }");
        yield return new("insert.xml-content", () => Insert("/// <summary>a</summary>\nclass C { }", n => n is CsXmlNode { Name: "summary" }, -1, new CsXmlNode(XmlNodeKind.Text, null, "b")), "/// <summary>ab</summary>\nclass C { }");
        yield return new("insert.xml-attribute", () => Insert("/// <summary>a</summary>\nclass C { }", n => n is CsXmlNode { Name: "summary" }, 0, new CsXmlAttribute("id", "x")), "/// <summary id=\"x\">a</summary>\nclass C { }");
        yield return new("insert.cref-parameter", () => Insert("/// <see cref=\"M(int)\"/>\nclass C { }", n => n is CsCref, -1, new CsFunctionPointerParameter(Cs.Int)), "/// <see cref=\"M(int, int)\" />\nclass C { }");
        yield return new("insert.cref-first-parameter", () => Insert("/// <see cref=\"M\"/>\nclass C { }", n => n is CsCref, -1, new CsFunctionPointerParameter(Cs.Int)), "/// <see cref=\"M(int)\" />\nclass C { }");
        yield return new("insert.extension-member", () => Insert("static class E { extension(string s) { } }", n => n is CsExtension, -1, new CsProperty(Cs.Int, "Length2") { Expression = Cs.Id("s").Member("Length") }), "static class E { extension(string s)\n{\n    int Length2 => s.Length;\n} }");
        yield return new("insert.extension-type-parameter", () => Insert("static class E { extension(string s) { } }", n => n is CsExtension, -1, new TypeParameter("T")), "static class E { extension<T>(string s)\n{\n} }");
        yield return new("insert.delegate-parameter", () => Insert("delegate void D(int a);", n => n is CsDelegate, -1, new CsParameter(Cs.Int, "b")), "delegate void D(int a, int b);");
        yield return new("insert.delegate-type-parameter", () => Insert("delegate void D(int a);", n => n is CsDelegate, 2, new TypeParameter("T")), "delegate void D<T>(int a);");
        yield return new("insert.operator-parameter", () => Insert("class C { public static C operator -(C a) => a; }", n => n is CsOperatorDeclaration, 2, new CsParameter(Cs.Type("C"), "b")), "class C { public static C operator -(C a, C b) => a; }");
        yield return new("insert.indexer-parameter", () => Insert("class C { int this[int i] => i; }", n => n is CsProperty, 2, new CsParameter(Cs.Int, "j")), "class C { int this[int i, int j] => i; }");
        yield return new("insert.function-pointer-parameter", () => Insert("unsafe class C { delegate*<void> F; }", n => n is CsType { Form: TypeForm.FunctionPointer }, 0, new CsFunctionPointerParameter(Cs.Int)), "unsafe class C { delegate*<int, void> F; }");
        yield return new("insert.query-clause", () => Insert("class C { object M(int[] a) => from x in a select x; }", n => n is CsExpression { Kind: ExpressionKind.Query }, 1, new CsQueryClause(QueryClauseKind.Where) { Value = Cs.Literal(true) }), "class C { object M(int[] a) => from x in a where true select x; }");
        yield return new("insert.continuation-clause", () => Insert("class C { object M(int[] a) => from x in a select x into y select y; }", n => n is CsQueryClause { Kind: QueryClauseKind.Into }, 0, new CsQueryClause(QueryClauseKind.Where) { Value = Cs.Literal(true) }), "class C { object M(int[] a) => from x in a select x into y where true select y; }");
        yield return new("insert.recursive-property", () => Insert("class C { bool M(object o) => o is { A: 1 }; }", n => n is CsPattern { Kind: PatternKind.Recursive }, -1, new CsPattern(PatternKind.Property) { Name = "B", Patterns = new[] { Cs.DiscardPattern() } }), "class C { bool M(object o) => o is { A: 1, B: _ }; }");
        yield return new("insert.positional-pattern", () => Insert("class C { bool M(object o) => o is R(1) { A: 1 }; }", n => n is CsPattern { Kind: PatternKind.Recursive }, 1, Cs.DiscardPattern()), "class C { bool M(object o) => o is R(_, 1) { A: 1 }; }");
        yield return new("insert.list-pattern", () => Insert("class C { bool M(int[] o) => o is [1]; }", n => n is CsPattern { Kind: PatternKind.List }, -1, Cs.DiscardPattern()), "class C { bool M(int[] o) => o is [1, _]; }");
        yield return new("insert.member", () => Insert("class C { int F; }", n => n is CsTypeDeclaration, -1, new CsField(Cs.Int, "G")), "class C { int F;\n    int G;\n }");
        yield return new("insert.switch-section", () => Insert("class C { void M(int a) { switch (a) { case 1: break; } } }", n => n is CsStatement { Kind: StatementKind.Switch }, -1, Cs.Section(Cs.Body(Cs.Break()), Cs.Default())), "class C { void M(int a) { switch (a) { case 1: break;\n    default:\n        break;\n } } }");
        yield return new("insert.switch-label", () => Insert("class C { void M(int a) { switch (a) { case 1: break; } } }", n => n is CsSwitchSection, 1, Cs.Case(Cs.Literal(2))), "class C { void M(int a) { switch (a) { case 1:\ncase 2:\n    break; } } }");
        yield return new("insert.switch-arm", () => Insert("class C { int M(int a) => a switch { 1 => 2, _ => 0 }; }", n => n is CsExpression { Kind: ExpressionKind.Switch }, 1, Cs.Arm(Cs.ConstantPattern(Cs.Literal(3)), Cs.Literal(4))), "class C { int M(int a) => a switch { 3 => 4, 1 => 2, _ => 0 }; }");
        yield return new("insert.for-variable", () => Insert("class C { void M() { for (int i = 0; i < 1; i++) { } } }", n => n is CsForInitializer, -1, Cs.Variable("j", Cs.Literal(1))), "class C { void M() { for (int i = 0, j = 1; i < 1; i++) { } } }");
        yield return new("insert.for-expression", () => Insert("class C { void M(int i) { for (i = 0; i < 1; i++) { } } }", n => n is CsForInitializer, -1, Cs.Assign(Cs.Id("i"), Cs.Literal(2))), "class C { void M(int i) { for (i = 0, i = 2; i < 1; i++) { } } }");
        yield return new("insert.for-iterator", () => Insert("class C { void M(int i, int j) { for (; i < 1; i++) { } } }", n => n is CsStatement { Kind: StatementKind.For }, 2, Cs.PostIncrement(Cs.Id("j"))), "class C { void M(int i, int j) { for (; i < 1; i++, j++)\n{\n} } }");
        yield return new("insert.for-iterator-before-condition", () => Insert("class C { void M(int i, int j) { for (; i < 1; i++) { } } }", n => n is CsStatement { Kind: StatementKind.For }, 0, Cs.PostIncrement(Cs.Id("j"))), "class C { void M(int i, int j) { for (; i < 1; j++, i++)\n{\n} } }");
        yield return new("insert.for-iterator-no-condition", () => Insert("class C { void M(int i, int j) { for (;; i++) { } } }", n => n is CsStatement { Kind: StatementKind.For }, 1, Cs.PostIncrement(Cs.Id("j"))), "class C { void M(int i, int j) { for (; ; i++, j++)\n{\n} } }");
        yield return new("insert.lambda-parameter", () => Insert("class C { System.Func<int, int, int> F = (a) => a; }", n => n is CsExpression { Kind: ExpressionKind.Lambda }, -1, Cs.LambdaParameter("b")), "class C { System.Func<int, int, int> F = (a, b) => a; }");
        yield return new("insert.anonymous-parameter", () => Insert("class C { System.Func<int, int> F = delegate (int a) { return a; }; }", n => n is CsExpression { Kind: ExpressionKind.AnonymousMethod }, -1, Cs.LambdaParameter("b", Cs.Int)), "class C { System.Func<int, int> F = delegate(int a, int b) {\n    return a;\n}; }");
        yield return new("insert.statement", () => Insert("class C { void M() { Run(); } }", n => n is CsBody, -1, Cs.Return()), "class C { void M() { Run();\n    return;\n } }");
        yield return new("insert.argument", () => Insert("class C { void M() { Run(1); } }", n => n is CsExpression { Kind: ExpressionKind.Invocation }, -1, Cs.Literal(2)), "class C { void M() { Run(1, 2); } }");
        yield return new("insert.method-parameter", () => Insert("class C { void M(int a) { } }", n => n is CsMethod, 2, new CsParameter(Cs.Int, "b")), "class C { void M(int a, int b)\n{\n} }");
        yield return new("insert.method-type-parameter", () => Insert("class C { void M(int a) { } }", n => n is CsMethod, 1, new TypeParameter("T")), "class C { void M<T>(int a)\n{\n} }");
        yield return new("insert.base-type", () => Insert("class C : A { }", n => n is CsTypeDeclaration, 1, Cs.Type("B")), "class C : A, B\n{\n}");
        yield return new("insert.type-parameter", () => Insert("class C { }", n => n is CsTypeDeclaration, 0, new TypeParameter("T")), "class C<T>\n{\n}");
        yield return new("insert.primary-parameter", () => Insert("class C(int a) { }", n => n is CsTypeDeclaration, 1, new CsParameter(Cs.Int, "b")), "class C(int a, int b)\n{\n}");
        yield return new("insert.accessor", () => Insert("class C { int P { get; } }", n => n is CsProperty, -1, new CsAccessor("set")), "class C { int P { get; set; } }");
        yield return new("insert.event-accessor", () => { var d = InClass(new CsEvent("System.Action", "E") { Accessors = new[] { new CsAccessor("add") { Body = Cs.Body() } } }); return d.Insert(Find(d, n => n is CsEvent), 2, new CsAccessor("remove") { Body = Cs.Body() }); }, "class C\n{\n    event System.Action E\n    {\n        add\n        {\n        }\n        remove\n        {\n        }\n    }\n}\n");
        yield return new("insert.base-after-union-cases", () => Insert("union U(int, string);", n => n is CsTypeDeclaration, -1, Cs.Type("I")), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'index')");
        yield return new("insert.unsupported", () => Insert("class C { int F; }", n => n is CsField, 0, Cs.Return()), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("insert.statement-into-if", () => Insert("class C { void M() { if (true) { } } }", n => n is CsStatement { Kind: StatementKind.If }, 0, Cs.Return()), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("insert.section-into-block", () => Insert("class C { void M() { { } } }", n => n is CsStatement { Kind: StatementKind.Block }, 0, Cs.Section(Cs.Body(Cs.Break()), Cs.Default())), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("insert.arm-into-call", () => Insert("class C { void M() { Run(); } }", n => n is CsExpression { Kind: ExpressionKind.Invocation }, 0, Cs.Arm(Cs.DiscardPattern(), Cs.Literal(1))), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("insert.parameter-into-call", () => Insert("class C { void M() { Run(); } }", n => n is CsExpression { Kind: ExpressionKind.Invocation }, 0, Cs.LambdaParameter("x")), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("insert.index-negative", () => Insert("class C { int F; }", n => n is CsTypeDeclaration, -9, new CsField(Cs.Int, "G")), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'index')");
        yield return new("insert.index-past-end", () => { var d = Src("class C { }"); return d.Insert(Find(d, n => n is CsTypeDeclaration), 5, new CsField(Cs.Int, "G")); }, "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'index')");
        yield return new("insert.null-child", () => { var d = Src("class C { }"); return d.Insert(Find(d, n => n is CsTypeDeclaration), 0, null!); }, "throws ArgumentNullException: Value cannot be null. (Parameter 'child')");
        yield return new("insert.invalid-result", () => Insert("class C { int F; }", n => n is CsTypeDeclaration, -1, new CsField(Cs.Int, "1G")), "throws GenValidationException: GEN003 Types[0].Members[1].Name: Invalid identifier.");

        // Insertion into symbol trees.
        yield return new("insert.tree-item", () => InsertTree("Run();", n => n is CsTree, -1, Cs.Compound(Name("x"))), "throws GenValidationException: SYM001 Items[1]: A statement compound requires declaration symbols of the same type.");
        yield return new("insert.tree-symbol-lambda-parameter", () => InsertTree("var f = (int a) => a;", n => n is CsSymbolOptions { Operation: SymbolOperation.Lambda }, 0, Cs.LambdaParameter("b", Cs.Int)), "throws GenValidationException: SYM001 Tree.Children[0].Children[0].Children[0]: Parameter symbols require a compound and cannot coexist with legacy parameters.");
        yield return new("insert.tree-symbol-attribute", () => InsertTree("var f = (int a) => a;", n => n is CsSymbolOptions { Operation: SymbolOperation.Lambda }, 0, Cs.Attribute("A")), "var f = [A] (int a) => a;");
        yield return new("insert.tree-symbol-query-clause", () => InsertTree("var q = from x in a select x;", n => n is CsSymbolOptions { Operation: SymbolOperation.Query }, 1, new CsQueryClause(QueryClauseKind.Where) { Value = Cs.Literal(true) }), "throws GenValidationException: SYM001 Tree.Children[0].Children[0].Children[0]: Choose one query clause representation.");
        yield return new("insert.tree-symbol-type-argument", () => InsertTree("F(a);", n => n is CsSymbol { Name: "F" }, 0, Cs.Int), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("insert.tree-symbol-type-argument-options", () => InsertTree("F(a);", n => n is CsSymbolOptions o && o.Operation == SymbolOperation.None && o.Arguments == null && o.Target == null, 0, Cs.Int), "F<int>(a);");
        yield return new("insert.tree-compound-part", () => InsertTree("F(a);", n => n is CsSymbolOptions { AsCompound: true }, -1, Name("b")), "F(a, b);");
        yield return new("insert.tree-tuple-part", () => InsertTree("var t = (a, b);", n => n is CsSymbolOptions { Operation: SymbolOperation.Tuple }, -1, Name("c")), "var t = (a, b, c);");
        yield return new("insert.tree-instruction-section", () => InsertTree("switch (a) { case 1: break; }", n => n is CsInstructionOptions o && o.Sections.Count > 0, -1, Cs.Section(Cs.Body(Cs.Break()), Cs.Default())), "switch (a) { case 1:\n    break;\n    default:\n        break;\n }");
        yield return new("insert.tree-instruction-catch", () => InsertTree("try { } catch (A) { }", n => n is CsInstructionOptions o && o.Catches.Count > 0, -1, Cs.CatchAll(Cs.Body())), "try\n{\n}\ncatch (A)\n{\n}\ncatch\n{\n}");
        yield return new("insert.tree-unsupported", () => InsertTree("Run();", n => n is CsSymbolOptions { Operation: SymbolOperation.None, AsCall: true }, 0, new CsField(Cs.Int, "F")), "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");

        // Replace, remove, move and reformat.
        yield return new("edit.replace", () => { var d = Src("class C { int F; }"); return d.Replace(Find(d, n => n is CsField), new CsField(Cs.Type("long"), "G")); }, "class C { \n    long G;\n }");
        yield return new("edit.replace-root", () => { var d = Src("class C { }"); return d.Replace(NodePath.Root, Src("class D { }")); }, "class D { }");
        yield return new("edit.replace-root-kind", () => { var d = Src("class C { }"); return d.Replace(NodePath.Root, Cs.Int); }, "throws ArgumentException: Root replacement must be a document.");
        yield return new("edit.replace-null", () => { var d = Src("class C { }"); return d.Replace(NodePath.Root, null!); }, "throws ArgumentNullException: Value cannot be null. (Parameter 'replacement')");
        yield return new("edit.replace-other-snapshot", () => { var d = Src("class C { }"); var other = Src("class D { }"); return d.Replace(Find(other, n => n is CsTypeDeclaration), Src("class E { }").Types[0]); }, "throws ArgumentException: This path belongs to a different document snapshot. (Parameter 'path')");
        yield return new("edit.replace-null-path", () => Src("class C { }").At(null!), "throws ArgumentNullException: Value cannot be null. (Parameter 'path')");
        yield return new("edit.remove", () => { var d = Src("class C { int F; int G; }"); return d.Remove(Find(d, n => n is CsField { Name: "F" })); }, "class C { int G; }");
        yield return new("edit.remove-root", () => Src("class C { }").Remove(NodePath.Root), "throws ArgumentException: Cannot remove the document root.");
        yield return new("edit.move-down", () => { var d = Src("class C { int F; int G; int H; }"); var type = Find(d, n => n is CsTypeDeclaration); return d.Move(type.Child(0), type, 3); }, "class C { int G; int H;int F; }");
        yield return new("edit.move-up", () => { var d = Src("class C { int F; int G; int H; }"); var type = Find(d, n => n is CsTypeDeclaration); return d.Move(type.Child(2), type, 0); }, "class C { int H;int F; int G; }");
        yield return new("edit.move-into-later-sibling", () => { var d = Src("class C { int F; } class D { }"); return d.Move(new NodePath(0, 0), new NodePath(1), 0); }, "class C {  } class D { \n    int F;\n}");
        yield return new("edit.move-into-earlier-sibling", () => { var d = Src("class C { } class D { int F; }"); return d.Move(new NodePath(1, 0), new NodePath(0), 0); }, "class C { \n    int F;\n} class D {  }");
        yield return new("edit.move-into-nested", () => { var d = Src("class C { int F; class N { } }"); return d.Move(new NodePath(0, 0), new NodePath(0, 1), 0); }, "class C { class N { \n    int F;\n} }");
        yield return new("edit.move-to-shallower", () => { var d = Src("class C { class N { int F; } }"); return d.Move(new NodePath(0, 0, 0), new NodePath(0), 0); }, "class C { \n    int F;\nclass N {  } }");
        yield return new("edit.move-into-self", () => { var d = Src("class C { class N { } }"); return d.Move(new NodePath(0), new NodePath(0, 0), 0); }, "throws ArgumentException: Cannot move a node into its own subtree.");
        yield return new("edit.move-root", () => { var d = Src("class C { }"); return d.Move(NodePath.Root, NodePath.Root, 0); }, "throws ArgumentException: Cannot move a node into its own subtree.");
        yield return new("edit.reformat-path", () => { var d = Src("class C { int  F ; void M( ) { Run( ); } }"); return d.Reformat(Find(d, n => n is CsMethod)); }, "class C { int  F ; void M()\n{\n    Run();\n} }");
        yield return new("edit.reformat-node", () => Src("class C{int F;}").Types[0].Reformat(), "CsTypeDeclaration(CsField(CsType))");
        yield return new("edit.find", () => { var d = Src("class C { int P { get; } class N { } }"); return d.FindType("C")!.FindProperty("P")!.Name + d.FindType("C")!.FindType("N")!.Name + d.FindType("X") + d.Types[0].Properties.Count() + d.Types[0].Types.Count(); }, "PN11");
        yield return new("edit.descendants", () => Src("class C { int F; }").Descendants(), "[]CsDocument [0]CsTypeDeclaration [0,0]CsField [0,0,0]CsType");
        yield return new("edit.document-null-attributes", () => new CsDocument { Attributes = null!, Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } }, "invalid: GEN002 Required node or collection is null.");
        yield return new("edit.document-null-attributes-get", () => (new CsDocument { Attributes = null! }.Attributes == null) + "/" + (new CsDocument { Types = null! }.Types == null) + "/" + (new CsDocument { Usings = null! }.Usings == null), "True/True/True");

        // Tree edits.
        yield return new("tree.at", () => { var t = Cs.ParseTree("Run(); Stop();").Root; return t.At(Find(t, n => n is CsSymbol { Name: "Stop" })); }, "CsSymbol(CsSymbolOptions)");
        yield return new("tree.at-other-snapshot", () => Cs.ParseTree("Run();").Root.At(Find(Cs.ParseTree("Stop();").Root, n => n is CsSymbol)), "throws ArgumentException: This path belongs to a different document snapshot. (Parameter 'path')");
        yield return new("tree.at-null", () => Cs.ParseTree("Run();").Root.At(null!), "throws ArgumentNullException: Value cannot be null. (Parameter 'path')");
        yield return new("tree.replace", () => { var t = Cs.ParseTree("Run();").Root; return t.Replace(Find(t, n => n is CsSymbol { Name: "Run" }), Name("Stop")); }, "Stop();");
        yield return new("tree.replace-null", () => Cs.ParseTree("Run();").Root.Replace(NodePath.Root, null!), "throws ArgumentNullException: Value cannot be null. (Parameter 'replacement')");
        yield return new("tree.replace-root-kind", () => Cs.ParseTree("Run();").Root.Replace(NodePath.Root, Cs.Int), "throws ArgumentException: Root replacement must be a document.");
        yield return new("tree.replace-invalid", () => { var t = Cs.ParseTree("Run();").Root; return t.Replace(Find(t, n => n is CsSymbol { Name: "Run" }), Name("1x")); }, "throws GenValidationException: SYM001 Tree.Children[0].Children[0].Children[0].Name: Invalid symbol name.");
        yield return new("tree.remove", () => { var t = Cs.ParseTree("Run(); Stop();").Root; return t.Remove(new NodePath(0)); }, "Stop();");
        yield return new("tree.remove-root", () => Cs.ParseTree("Run();").Root.Remove(NodePath.Root), "throws ArgumentException: Cannot remove the document root.");
        yield return new("tree.insert", () => { var t = Cs.ParseTree("Run();").Root; return t.Insert(NodePath.Root, 1, Cs.ParseTree("Stop();").Root.Items[0].Detach()); }, "Run();\nStop();\n");
        yield return new("tree.insert-null", () => Cs.ParseTree("Run();").Root.Insert(NodePath.Root, 0, null!), "throws ArgumentNullException: Value cannot be null. (Parameter 'child')");
        yield return new("tree.insert-out-of-range", () => Cs.ParseTree("Run();").Root.Insert(NodePath.Root, 3, Name("x")), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'index')");
        yield return new("tree.move-down", () => Cs.ParseTree("A(); B(); C();").Root.Move(new NodePath(0), NodePath.Root, 3), "B(); C();A();");
        yield return new("tree.move-up", () => Cs.ParseTree("A(); B(); C();").Root.Move(new NodePath(2), NodePath.Root, 0), "C();A(); B();");
        yield return new("tree.move-into-block", () => { var t = Cs.ParseTree("A(); { B(); }").Root; return t.Move(new NodePath(0), Find(t, n => n is CsInstructionOptions { Body: not null }), 0); }, "throws ArgumentException: This node kind cannot be inserted into the selected child slot.");
        yield return new("tree.move-across", () => { var t = Cs.ParseTree("{ A(); } { B(); }").Root; var a = Find(t, n => n is CsSymbol { Name: "A" }); var second = t.Descendants().Where(n => n.Node is CsTree).Select(n => n.Path).Last(); return t.Move(new NodePath(a.Segments.Take(a.Segments.Count - 2).ToArray()), second, 0); }, "{  } { \n    A();\nB(); }");
        yield return new("tree.move-into-self", () => Cs.ParseTree("{ A(); }").Root.Move(new NodePath(0), new NodePath(0, 0), 0), "throws ArgumentException: Cannot move a node into its own subtree.");
        yield return new("tree.move-root", () => Cs.ParseTree("A();").Root.Move(NodePath.Root, NodePath.Root, 0), "throws ArgumentException: Cannot move a node into its own subtree.");
        yield return new("tree.reformat-path", () => { var t = Cs.ParseTree("if(a){Run( );}").Root; return t.Reformat(new NodePath(0)); }, "if (a)\n{\n    Run();\n}");
        yield return new("tree.descendants", () => Cs.ParseTree("Run();").Root.Descendants(), "[]CsTree [0]CsSymbol [0,0]CsSymbolOptions [0,0,0]CsSymbol [0,0,0,0]CsSymbolOptions [0,0,1]CsSymbol [0,0,1,0]CsSymbolOptions");

        // Paths.
        yield return new("path.members", () =>
        {
            var root = NodePath.Root;
            var child = root.Child(1).Child(2);
            child.Deconstruct(out var segments);
            var same = new NodePath(1, 2);
            var renamed = same with { Segments = new Values<int>(new[] { 3 }) };
            return string.Join(",", segments) + " " + child.Equals(same) + " " + child.Equals((NodePath?)null) + " " + child!.Equals(child) + " " + (child.GetHashCode() == same.GetHashCode()) + " " + string.Join(",", renamed.Segments) + " " + same.Equals(new NodePath(1, 3));
        }, "1,2 True False True True 3 False");
        yield return new("path.null-segments", () => (new NodePath() { Segments = null! }).Child(4).Child(5), "path [4,5]");
        yield return new("path.owner-differs", () => { var d = Src("class C { }"); var a = d.Descendants().Skip(1).First().Path; var b = Src("class C { }").Descendants().Skip(1).First().Path; return a.Equals(b) + " " + a.Equals(new NodePath(0)); }, "True False");
        yield return new("path.out-of-range", () => Src("class C { }").At(new NodePath(5)), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'path')");
        yield return new("path.negative", () => Src("class C { }").At(new NodePath(-1)), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'path')");
        yield return new("path.too-long", () => Deep(150).At(new NodePath(Enumerable.Repeat(0, 153).ToArray())), "throws ArgumentException: Path exceeds128. (Parameter 'path')");
        yield return new("path.too-long-missing", () => Src("class C { }").At(new NodePath(Enumerable.Repeat(0, 140).ToArray())), "throws ArgumentException: Path exceeds128. (Parameter 'path')");
        yield return new("path.descendants-deep", () => Deep(150).Descendants().Count(), "throws ArgumentException: Node graph exceeds128 or contains a cycle.");
        yield return new("path.deep-ok", () => Deep(100).At(new NodePath(Enumerable.Repeat(0, 103).ToArray())), "CsType");
    }
}
