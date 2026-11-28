using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-29 F4 (AD 84939297 v2 C1/C3/E): every render goes through the formatter, parsed documents included. A row renders
// a parsed source untouched or a built document: item order and using runs, comments of top-level statements, the newline
// a parsed document renders with, preprocessor conditions, the blank lines around a file-scoped namespace and the
// end-of-file comments, and the obsolete Reformat.
public static class DefaultFormattingScenarios
{
    static CsExpression Binary(CsOperator op, CsExpression left, CsExpression right) => Cs.Binary(op, left, right);

    static CsDocument Condition(CsExpression condition) => InClass(new CsDirective(DirectiveKind.If) { Condition = condition }, new CsDirective(DirectiveKind.EndIf));

    public static IEnumerable<Scenario> All()
    {
        // Items and usings: one order for parsed and built documents.
        yield return new("gene29.f4.usings.sorted-parsed", () => Src("using B;\nusing A;\nusing static Z;\nglobal using Q;\nclass C { }\n"), "global using Q;\nusing A;\nusing B;\nusing static Z;\n\nclass C\n{\n}\n");
        yield return new("gene29.f4.usings.unsorted-by-option", () => Src("using B;\nusing A;\nclass C { }\n").Render(new RenderOptions { SortUsings = false }), "using B;\nusing A;\n\nclass C\n{\n}\n");
        yield return new("gene29.f4.usings.run-per-directive-branch", () => Src("using B;\nusing A;\n#region R\nusing D;\nusing C;\n#endregion\nclass K { }\n"), "using A;\nusing B;\n\n#region R\n\nusing C;\nusing D;\n\n#endregion\n\nclass K\n{\n}\n");
        yield return new("gene29.f4.usings.leading-comment-keeps-order", () => Src("using Z;\n// about B\nusing B;\nusing A;\nclass C { }\n"), "using Z;\n// about B\nusing B;\nusing A;\n\nclass C\n{\n}\n");
        // GENE-29 F5 (AD B3): a license header followed by a blank line is the document's preamble, so the usings sort.
        yield return new("gene29.f5.usings.license-preamble-sorts", () => Src("// license\n\nusing B;\nusing A;\n\nclass C { }\n"), "// license\n\nusing A;\nusing B;\n\nclass C\n{\n}\n");
        yield return new("gene29.f4.usings.comments", () => Src("// lead\nextern alias X; // alias\nusing A; // a\n/* b */ using B;\nclass C { }\n"), "// lead\nextern alias X; // alias\n\nusing A; // a\n/* b */\nusing B;\n\nclass C\n{\n}\n");
        yield return new("gene29.f5.usings.container-comment", () => new CsDocument { Usings = new[] { new CsUsing("System") { Comments = new[] { new CsComment("/* c */", CommentKind.Block, CommentPlacement.Container) } } } }, "using System /* c */;\n");
        yield return new("gene29.f4.raw.empty-body", () => InClass(new CsMethod(Cs.Void, "M") { Body = Cs.RawBody("") }), "class C\n{\n    void M()\n    {\n    }\n}\n");
        yield return new("gene29.f4.usings.duplicates-once", () => new CsDocument { Usings = new[] { new CsUsing("System"), new CsUsing("System") } }, "using System;\n");
        yield return new("gene29.f4.items.built-order", () => new CsDocument
        {
            Items = new CsNode[] { new CsTypeDeclaration(TypeKind.Class, "C"), new CsAttribute(Cs.Type("A")) { Target = "assembly" }, new CsUsing("System"), new CsExternAlias("X") }
        }, "extern alias X;\n\nusing System;\n\n[assembly: A]\n\nclass C\n{\n}\n");
        yield return new("gene29.f4.items.namespace-after-prelude", () => new CsDocument
        {
            Namespace = "N",
            Items = new CsNode[] { new CsExternAlias("X"), new CsUsing("System"), new CsTypeDeclaration(TypeKind.Class, "C") }
        }, "extern alias X;\n\nusing System;\n\nnamespace N;\n\nclass C\n{\n}\n");
        yield return new("gene29.f4.items.namespace-with-directives", () => new CsDocument
        {
            Namespace = "N",
            Items = new CsNode[] { new CsUsing("System"), new CsDirective(DirectiveKind.Region) { Text = "R" }, new CsTypeDeclaration(TypeKind.Class, "C"), new CsDirective(DirectiveKind.EndRegion) }
        }, "using System;\n\nnamespace N;\n\n#region R\n\nclass C\n{\n}\n\n#endregion\n");
        yield return new("gene29.f4.items.file-scoped-namespace", () => Src("namespace N;\nusing System;\nclass C { }\n"), "namespace N;\n\nusing System;\n\nclass C\n{\n}\n");
        yield return new("gene29.f4.items.file-scoped-namespace-empty", () => Src("namespace N;\n"), "namespace N;\n");
        yield return new("gene29.f4.items.file-scoped-namespace-comment", () => new CsDocument { Items = new CsNode[] { new CsNamespace("N", Values<CsNode>.Empty, true) { Comments = new[] { new CsComment("// inside", CommentKind.Line, CommentPlacement.Container) } } } }, "namespace N;\n\n// inside\n");
        yield return new("gene29.f4.items.end-of-file-comment", () => Src("class C { }\n// eof\n"), "class C\n{\n}\n\n// eof\n");
        yield return new("gene29.f4.items.comment-only", () => Src("// only\n"), "// only\n");

        // Output that is the parsed (already formatted) source reads back as that parse, for the same language version only.
        yield return new("gene29.f4.guard.formatted-source", () => Src("class C\n{\n    int F;\n}\n"), "class C\n{\n    int F;\n}\n");
        yield return new("gene29.f4.guard.formatted-source-other-version", () => Src("class C\n{\n    int F;\n}\n").At(CsLanguageVersion.CSharp12), "class C\n{\n    int F;\n}\n");

        // Top-level statements keep their own comments.
        yield return new("gene29.f4.global.comments", () => Src("// c\nvar x = F(1); // t\n/* a */ if (a) G();\n"), "// c\nvar x = F(1); // t\n/* a */\nif (a)\n{\n    G();\n}\n");

        // A parsed document renders with its dominant newline.
        yield return new("gene29.f4.newline.crlf", () => Src("class C\r\n{\r\n    int  A;\r\n}\r\n"), "class C\r\n{\r\n    int A;\r\n}\r\n");
        yield return new("gene29.f4.newline.literal-breaks-do-not-count", () => Src("class C\r\n{\r\n    string V = @\"a\nb\nc\";\r\n}\r\n"), "class C\r\n{\r\n    string V = @\"a\nb\nc\";\r\n}\r\n");
        yield return new("gene29.f4.newline.tree-crlf", () => Cs.ParseTree("Run();\r\nStop();\r\n").Root, "Run();\r\nStop();\r\n");

        // Preprocessor conditions: only the parentheses the model holds or the precedence needs.
        yield return new("gene29.f4.condition.parsed", () => Src("#if A && !B || (C == true) && !(D || E) || !!F\nclass K { }\n#endif\n"), "#if A && !B || (C == true) && !(D || E) || !!F\nclass K { }\n#endif\n");
        yield return new("gene29.f4.condition.looser-left", () => Condition(Binary(CsOperator.AndAlso, Binary(CsOperator.OrElse, Cs.Id("A"), Cs.Id("B")), Cs.Id("C"))), "class C\n{\n#if (A || B) && C\n#endif\n}\n");
        yield return new("gene29.f4.condition.right-nested", () => Condition(Binary(CsOperator.OrElse, Cs.Id("A"), Binary(CsOperator.OrElse, Cs.Id("B"), Cs.Id("C")))), "class C\n{\n#if A || (B || C)\n#endif\n}\n");
        yield return new("gene29.f4.condition.not-of-binary", () => Condition(Cs.Unary(CsOperator.Not, Binary(CsOperator.Equal, Cs.Id("A"), Cs.Literal(true)))), "class C\n{\n#if !(A == true)\n#endif\n}\n");
        yield return new("gene29.f4.condition.equality-chain", () => Condition(Binary(CsOperator.NotEqual, Binary(CsOperator.Equal, Cs.Id("A"), Cs.Literal(true)), Cs.Literal(false))), "class C\n{\n#if A == true != false\n#endif\n}\n");

        // Prefix operators: parentheses only where two operators would lex as one token.
        yield return new("gene29.f4.unary.parsed-chain", () => Src("class C { int M(int a) => ~~-~a + -(int)a + !!b; }"), "class C\n{\n    int M(int a) => ~~-~a + -(int)a + !!b;\n}\n");
        yield return new("gene29.f4.unary.negate-negate", () => Init(Cs.Unary(CsOperator.Negate, Cs.Unary(CsOperator.Negate, Cs.Id("a")))), "class C\n{\n    object F = -(-a);\n}\n");
        yield return new("gene29.f4.unary.plus-plus", () => Init(Cs.Unary(CsOperator.UnaryPlus, Cs.Unary(CsOperator.UnaryPlus, Cs.Id("a")))), "class C\n{\n    object F = +(+a);\n}\n");
        yield return new("gene29.f4.unary.address-address", () => Init(Cs.Unary(CsOperator.AddressOf, Cs.Unary(CsOperator.AddressOf, Cs.Id("a")))), "class C\n{\n    object F = &(&a);\n}\n");
        yield return new("gene29.f4.unary.negate-literal", () => Init(Cs.Unary(CsOperator.Negate, Cs.Literal(-1))), "class C\n{\n    object F = -(-1);\n}\n");
        yield return new("gene29.f4.unary.comment-between", () => Init(Cs.Unary(CsOperator.Negate, Cs.Unary(CsOperator.Negate, Cs.Id("a"))) with { Comments = new[] { new CsComment("/* c */", CommentKind.Block, CommentPlacement.Container) } }), "class C\n{\n    object F = -/* c */ -a;\n}\n");

        // A parsed lambda's header comments read back before it (GENE-29 F4, until the F5 ownership pass).
        yield return new("gene29.f4.lambda.header-comments", () => Src("class C { object M() => (/* parameter */ int x) => /* arrow */ {\n// body\nreturn x;\n}; }"), "class C\n{\n    object M() => (/* parameter */ int x) =>\n    /* arrow */\n    {\n        // body\n        return x;\n    };\n}\n");

        // GENE-29 F5: a comment in a lambda's parameter list is the parameter's (F4 refused this render with EMIT001).
        yield return new("gene29.f5.field-initializer-lambda-comment", () => Src("class C { System.Func<int, int> F = (/* p */ int x) => x; }"), "class C\n{\n    System.Func<int, int> F = (/* p */ int x) => x;\n}\n");

        // Trailing block comments are followed by what the writer writes next, with no extra space.
        yield return new("gene29.f4.comment.trailing-block-before-operator", () => Src("class C { int F = a /* c */ + b; }"), "class C\n{\n    int F = a /* c */ + b;\n}\n");
    }
}
