using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Source-preserving rendering of edited parsed documents: renamed scalars, chain edits, per-node reformats, list
// edits with comments, and render options.
public static class PreservedScenarios
{
    static NodePath Find(CsDocument document, Func<CsNode, bool> match) => document.Descendants().First(n => match(n.Node)).Path;

    // Replaces the first node matching `match` with `change(node)` and renders through the public edit API.
    static CsDocument Edit(string source, Func<CsNode, bool> match, Func<CsNode, CsNode> change)
    {
        var document = Src(source);
        var path = Find(document, match);
        return document.Replace(path, change(document.At(path)));
    }

    static CsDocument Reformat(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Reformat(Find(document, match));
    }

    const string Rich = "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n";

    const string Chain = "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(3);\n    }\n}\n";

    public static IEnumerable<Scenario> All()
    {
        // Renamed scalars keep the surrounding source.
        yield return new("scalar.type-name", () => Edit("class C { Old F; }", n => n is CsType { Name: "Old" }, n => (CsType)n with { Name = "New" }), "class C { New F; }");
        yield return new("scalar.parameter-name", () => Edit("class C { void M(int a) { } }", n => n is CsParameter, n => (CsParameter)n with { Name = "b" }), "class C { void M(int b) { } }");
        yield return new("scalar.type-parameter-name", () => Edit("class C<T> { }", n => n is TypeParameter, n => (TypeParameter)n with { Name = "U" }), "class C<U> { }");
        yield return new("scalar.namespace-name", () => Edit("namespace N { class C { } }", n => n is CsNamespace, n => (CsNamespace)n with { Name = "M" }), "namespace M { class C { } }");
        yield return new("scalar.using-name", () => Edit("using System;\nclass C { }", n => n is CsUsing, n => (CsUsing)n with { Name = "System.IO" }), "using System.IO;\nclass C { }");
        yield return new("scalar.lambda-parameter-name", () => Edit("class C { System.Func<int, int> F = x => x; }", n => n is CsLambdaParameter, n => (CsLambdaParameter)n with { Name = "y" }), "class C { System.Func<int, int> F = y => x; }");
        yield return new("scalar.declarator-name", () => Edit("class C { int F, G; }", n => n is CsVariableDeclarator, n => (CsVariableDeclarator)n with { Name = "H" }), "class C { int F, H; }");
        yield return new("scalar.pattern-name", () => Edit("class C { bool M(object o) => o is int n; }", n => n is CsPattern { Kind: PatternKind.Declaration }, n => (CsPattern)n with { Name = "m" }), "class C { bool M(object o) => o is int m; }");
        yield return new("scalar.statement-text", () => Edit("class C { void M() { int x = 1; } }", n => n is CsStatement { Kind: StatementKind.Local }, n => (CsStatement)n with { Text = "y" }), "class C { void M() { int y = 1; } }");
        yield return new("scalar.expression-text", () => Edit("class C { int M() => a; }", n => n is CsExpression { Kind: ExpressionKind.Identifier }, n => (CsExpression)n with { Text = "b" }), "class C { int M() => b; }");
        yield return new("scalar.member-name", () => Edit("class C { void M() { } }", n => n is CsMethod, n => (CsMethod)n with { Name = "N" }), "class C { void N() { } }");
        yield return new("scalar.member-name-keyword", () => Edit("class C { void M() { } }", n => n is CsMethod, n => (CsMethod)n with { Name = "@class" }), "class C { void @class() { } }");
        yield return new("scalar.unbound-change", () => Edit("class C { void M(int a) { } }", n => n is CsParameter, n => (CsParameter)n with { Modifier = "in" }), "class C { void M(in int a) { } }");
        yield return new("scalar.name-and-unbound", () => Edit("class C { void M(int a) { } }", n => n is CsParameter, n => (CsParameter)n with { Name = "b", Modifier = "ref" }), "class C { void M(ref int b) { } }");
        yield return new("scalar.type-unbound", () => Edit("class C { Old F; }", n => n is CsType { Name: "Old" }, n => (CsType)n with { Name = "New", Form = TypeForm.Parameter }), "class C { New F; }");
        yield return new("scalar.child-count", () => Edit("class C { void M(int a) { } }", n => n is CsMethod, n => (CsMethod)n with { Parameters = Values<CsParameter>.Empty }), "class C { void M()\n{\n} }");
        yield return new("scalar.fresh-child", () => Edit("class C { void M(int a) { } }", n => n is CsMethod, n => (CsMethod)n with { ReturnType = Cs.Int, Body = CsBody.FromExpression(Cs.Literal(1)) }), "class C { int M(int a) => 1; }");
        yield return new("scalar.tree-symbol-name", () => { var t = Cs.ParseTree("Run();").Root; var path = t.Descendants().First(n => n.Node is CsSymbol { Name: "Run" }).Path; return t.Replace(path, (CsSymbol)t.At(path) with { Name = "Stop" }); }, "Stop();");
        yield return new("scalar.tree-symbol-keyword", () => { var t = Cs.ParseTree("Run();").Root; var path = t.Descendants().First(n => n.Node is CsSymbol { Name: "Run" }).Path; return t.Replace(path, (CsSymbol)t.At(path) with { Name = "@class" }); }, "@class();");
        yield return new("scalar.tree-symbol-unbound", () => { var t = Cs.ParseTree("var x = 1;").Root; var path = t.Descendants().First(n => n.Node is CsSymbol { Name: "x" }).Path; var s = (CsSymbol)t.At(path); return t.Replace(path, s with { Name = "y", Options = s.Options with { AsVar = true } }); }, "var y = 1;");

        // Chains render link by link.
        yield return new("chain.root-identifier", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "a" }, n => Cs.Id("x")), "class C\n{\n    void M()\n    {\n        x.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.middle-argument", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Literal, Text: "2" }, n => Cs.Literal(5)), "class C\n{\n    void M()\n    {\n        a.B(1).C(5).D(3);\n    }\n}\n");
        yield return new("chain.middle-member", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "C" }, n => (CsExpression)n with { Text = "Z" }), "class C\n{\n    void M()\n    {\n        a.B(1).Z(2).D(3);\n    }\n}\n");
        yield return new("chain.reformat-link", () => Reformat(Chain.Replace("C(2)", "C( 2 )"), n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 2 && e.Arguments[1].Text == "2"), "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.comment-on-link", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "C" }, n => n with { Comments = new[] { new CsComment("/* c */", CommentKind.Block) } }), "class C\n{\n    void M()\n    {\n        /* c */ a.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.top-replaced", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Invocation } e && e.Arguments.Count == 2 && e.Arguments[1].Text == "3", n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0], Cs.Literal(9) } }), "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(9);\n    }\n}\n");
        yield return new("chain.receiver-detached", () => Edit(Chain, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "D" }, n => ((CsExpression)n) with { Arguments = new[] { ((CsExpression)n).Arguments[0].Detach() as CsExpression ?? Cs.Id("q") } }), "class C\n{\n    void M()\n    {\n        a.B(1).C(2).D(3);\n    }\n}\n");
        yield return new("chain.receiver-swapped", () =>
        {
            var document = Src(Chain);
            var d = Find(document, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "D" });
            var c = Find(document, n => n is CsExpression { Kind: ExpressionKind.Member, Text: "C" });
            var cNode = (CsExpression)document.At(c);
            var dNode = (CsExpression)document.At(d);
            return document.Replace(d, dNode with { Arguments = new[] { (CsExpression)cNode.Arguments[0].Detach() } });
        }, "class C\n{\n    void M()\n    {\n        a.B(1).D(3);\n    }\n}\n");
        yield return new("chain.conditional", () => Edit("class C { object M(object a) => a?.B?.C?.D; }", n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "a" }, n => Cs.Id("x")), "class C { object M(object a) => x?.B?.C?.D; }");
        yield return new("chain.long-and-edit", () =>
        {
            string chain = "a" + string.Concat(Enumerable.Range(0, 40).Select(i => ".M" + i + "()"));
            return Edit("class C { void M() { " + chain + "; } }", n => n is CsExpression { Kind: ExpressionKind.Identifier, Text: "a" }, n => Cs.Id("b"));
        }, "class C { void M() { b.M0().M1().M2().M3().M4().M5().M6().M7().M8().M9().M10().M11().M12().M13().M14().M15().M16().M17().M18().M19().M20().M21().M22().M23().M24().M25().M26().M27().M28().M29().M30().M31().M32().M33().M34().M35().M36().M37().M38().M39(); } }");

        // Reformatting one node of each kind.
        yield return new("reformat.lambda-parameter", () => Reformat(Rich, n => n is CsLambdaParameter), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.declarator", () => Reformat(Rich, n => n is CsVariableDeclarator), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.switch-arm", () => Reformat(Rich, n => n is CsSwitchArm), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.switch-label", () => Reformat(Rich, n => n is CsSwitchLabel), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.parameter", () => Reformat(Rich, n => n is CsParameter), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.attribute", () => Reformat(Rich, n => n is CsAttribute), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.accessor", () => Reformat(Rich, n => n is CsAccessor), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add\n    {\n    } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.type-parameter", () => Reformat(Rich, n => n is TypeParameter), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.catch", () => Reformat(Rich, n => n is CsCatch), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null)\n        {\n        }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.constraint", () => Reformat(Rich, n => n is CsConstraint), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.tuple-element", () => Reformat(Rich, n => n is TupleElement), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.attribute-argument", () => Reformat(Rich, n => n is AttributeArgument), "using System;\n[A(1, Name = 2)]\nclass C<T> : B where T : class\n{\n    int F, G = 1;\n    event Action E { add { } remove { } }\n    int P { get; private set; }\n    (int A, string B) Pair;\n    void M(int a, params int[] rest)\n    {\n        try { } catch (Exception e) when (e != null) { }\n        switch (a) { case 1: break; default: break; }\n        var f = (int x) => x;\n        var s = a switch { 1 => 2, _ => 0 };\n        for (int i = 0, j = 1; i < j; i++) { }\n    }\n}\n");
        yield return new("reformat.xml", () => Reformat("/// <summary>a  b</summary>\nclass C { }\n", n => n is CsXmlNode { Name: "summary" }), "/// <summary>a  b</summary>\nclass C { }\n");
        yield return new("reformat.cref", () => Reformat("/// <see cref=\"M( int )\"/>\nclass C { }\n", n => n is CsCref), "/// <see cref=\"M(int)\" />\nclass C { }\n");
        yield return new("reformat.trivia", () => Reformat("class C { int M() => (\n#if true\n1\n#endif\n); }", n => n is CsSyntaxTrivia), "class C { int M() => (\n#if true\n1\n#endif\n); }");
        yield return new("reformat.statement-in-section", () => Reformat("class C { void M(int a) { switch (a) { case 1: Run( ); break; } } }", n => n is CsSwitchSection), "class C { void M(int a) { switch (a) { case 1:\n    Run();\n    break; } } }");
        yield return new("reformat.global-statement", () => Reformat("System.Console.WriteLine( 1 );\n", n => n is CsGlobalStatement), "System.Console.WriteLine(1);\n");
        yield return new("reformat.document", () => Src("namespace N { class C { int  F; } }").Reformat(), "namespace N\n{\n    class C\n    {\n        int F;\n    }\n}\n");
        yield return new("reformat.document-block-namespace", () => Src("namespace N { class C { int  F; } }").Reformat(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped }), "namespace N\n{\n    class C\n    {\n        int F;\n    }\n}\n");
        yield return new("reformat.document-file-namespace", () => Src("namespace N;\nclass C { int  F; }\n").Reformat(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped, Indent = "\t" }), "namespace N;\nclass C\n{\n\tint F;\n}\n");

        // List edits with comments.
        yield return new("list.container-comment-added", () => Edit("class C\n{\n    int F;\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Comments = new[] { new CsComment("// tail", CommentKind.Line, CommentPlacement.Container) }, Members = new(((CsTypeDeclaration)n).Members.Append(new CsField(Cs.Int, "G"))) }), "class C\n{\n    // tail\n    int F;\n    int G;\n}\n");
        yield return new("list.container-comment-kept", () => Edit("class C\n{\n    int F;\n    // tail\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new(((CsTypeDeclaration)n).Members.Append(new CsField(Cs.Int, "G"))) }), "class C\n{\n    int F;\n    int G;\n    // tail\n}\n");
        yield return new("list.container-comment-removed", () => Edit("class C\n{\n    int F;\n    // tail\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Comments = Values<CsComment>.Empty, Members = new(((CsTypeDeclaration)n).Members.Append(new CsField(Cs.Int, "G"))) }), "class C\n{\n    int F;\n    int G;\n}\n");
        yield return new("list.container-comment-changed", () => Edit("class C\n{\n    int F;\n    // tail\n}\n", n => n is CsTypeDeclaration, n => { var t = (CsTypeDeclaration)n; return t with { Comments = new(t.Comments.Select(c => c with { Text = "// changed" })), Members = new(t.Members.Append(new CsField(Cs.Int, "G"))) }; }), "class C\n{\n    int F;\n    int G;\n    // changed\n}\n");
        yield return new("list.inline-comment-removed", () => Edit("class C\n{\n    int F; // keep\n    /* gone */ int G;\n}\n", n => n is CsTypeDeclaration, n => { var t = (CsTypeDeclaration)n; return t with { Members = new(new CsMember[] { t.Members[1] with { Comments = Values<CsComment>.Empty }, t.Members[0] }) }; }), "class C\n{\n    int G;\n    int F; // keep\n}\n");
        yield return new("list.member-removed-with-comment", () => Edit("class C\n{\n    // about F\n    int F;\n    int G;\n}\n", n => n is CsTypeDeclaration, n => { var t = (CsTypeDeclaration)n; return t with { Members = new(new[] { t.Members[1] }) }; }), "class C\n{\n    int G;\n}\n");
        yield return new("list.empty-body-insert", () => Edit("class C { }\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new CsMember[] { new CsField(Cs.Int, "F") } }), "class C { \n    int F;\n}\n");
        yield return new("list.empty-multiline-body-insert", () => Edit("class C\n{\n}\n", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new CsMember[] { new CsField(Cs.Int, "F") } }), "class C\n{\n    int F;\n}\n");
        yield return new("list.document-items", () => Edit("using System;\nclass C { }\n", n => n is CsDocument, n => ((CsDocument)n) with { Items = new(((CsDocument)n).Items.Append(new CsTypeDeclaration(TypeKind.Class, "D"))) }), "using System;\nclass C { }\nclass D\n{\n}\n");
        yield return new("list.file-namespace-items", () => Edit("namespace N;\nclass C { }\n", n => n is CsNamespace, n => ((CsNamespace)n) with { Items = new(((CsNamespace)n).Items.Append(new CsTypeDeclaration(TypeKind.Class, "D"))) }), "namespace N;\nclass C { }\nclass D\n{\n}\n");
        yield return new("list.same-line-insert", () => Edit("class C { int F; }", n => n is CsTypeDeclaration, n => ((CsTypeDeclaration)n) with { Members = new(new CsMember[] { new CsField(Cs.Int, "E") }.Concat(((CsTypeDeclaration)n).Members)) }), "class C { \n    int E;\nint F; }");

        // Render options.
        yield return new("options.bad-indent", () => Cs.Document().Class("C").End().Build().Render(new RenderOptions { Indent = "--" }), "throws ArgumentException: Indent must contain only spaces or tabs. (Parameter 'options')");
        yield return new("options.null-indent", () => Cs.Document().Class("C").End().Build().Render(new RenderOptions { Indent = null! }), "throws ArgumentException: Indent must contain only spaces or tabs. (Parameter 'options')");
        yield return new("options.unknown-nullable", () => Cs.Document().Class("C").End().Build().Render(new RenderOptions { Nullable = (NullableMode)9 }), "throws ArgumentException: Unknown nullable mode. (Parameter 'options')");
        yield return new("options.unknown-namespace-style", () => Cs.Document().Namespace("N").Class("C").End().Build().Render(new RenderOptions { NamespaceStyle = (NamespaceStyle)9 }), "throws ArgumentException: Unknown namespace style. (Parameter 'options')");
        yield return new("options.block-namespace", () => Cs.Document().Namespace("N").Using("System").Class("C").End().Build().Render(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped, SortUsings = false }), "using System;\n\nnamespace N\n{\n    class C\n    {\n    }\n}\n");
        yield return new("options.empty-document", () => new CsDocument().Render(), "\n");
        yield return new("options.header-only", () => new CsDocument { Header = "// h" }.Render(), "// h\n\n");
        yield return new("options.nullable-document", () => new CsDocument { NullableContext = NullableMode.Disable, Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } }.Render(new RenderOptions { Nullable = NullableMode.Enable }), "#nullable enable\n\nclass C\n{\n}\n");
        yield return new("options.namespace-empty-block", () => new CsDocument { Namespace = "N" }.Render(new RenderOptions { NamespaceStyle = NamespaceStyle.BlockScoped }), "namespace N\n{\n}\n");
    }
}
