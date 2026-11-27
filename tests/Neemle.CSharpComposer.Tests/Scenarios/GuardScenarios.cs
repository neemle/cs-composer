using System.Reflection;
using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-29 F3 (AD 84939297 v2 section D): the meaning guard. Every checked render reads its output back and compares it
// with the model; parentheses repair a divergence in an expression or a pattern, and anything else is EMIT001. The rows
// cover the validation rules the F1/F2 corpus "move" defects called for, the equivalences the guard accepts, its repair
// loop and, through the internal fault hook (AD H4), every way a read-back can diverge. Each row failed on 2e32b9b or
// covers a new branch.
public static class GuardScenarios
{
    static CsComment Line(string text, CommentPlacement placement = CommentPlacement.Leading) => new("// " + text, CommentKind.Line, placement);

    static CsComment Block(string text, CommentPlacement placement = CommentPlacement.Leading) => new("/* " + text + " */", CommentKind.Block, placement);

    // The first node matching `match`, moved to the start of its parent's children.
    static CsDocument MoveFirst(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        var path = document.Descendants().First(n => match(n.Node)).Path;
        return document.Move(path, new NodePath(new Values<int>(path.Segments.Take(path.Segments.Count - 1))), 0);
    }

    static CsDocument RemoveFirst(string source, Func<CsNode, bool> match)
    {
        var document = Src(source);
        return document.Remove(document.Descendants().First(n => match(n.Node)).Path);
    }

    // Renders with the guard's fault hook rewriting the output before it is read back.
    static Func<object?> Faulted(Func<string, string> change, Func<string> render) => () =>
    {
        var inject = typeof(Cs).Assembly.GetType("Neemle.CSharpComposer.Rendering.MeaningGuard")!.GetMethod("Inject", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = inject.Invoke(null, [change]);
        try { return render(); }
        finally { inject.Invoke(null, [previous]); }
    };

    static string Replace(string text, string from, string to) => text.Contains(from, StringComparison.Ordinal) ? text.Replace(from, to, StringComparison.Ordinal) :
        throw new InvalidOperationException("The output holds no " + from + ".");

    static CsExpression Id(string name) => Cs.Id(name);

    public static IEnumerable<Scenario> All()
    {
        // 1. Validation: the corpus "move" defects that read back differently are refused before rendering.
        yield return new("gene29.f3.validate.namespace-before-using", () => MoveFirst("using A;\nnamespace N;\nclass C\n{\n}\n", n => n is CsNamespace),
            "throws GenValidationException: GEN092 Items: A file-scoped namespace must follow every other document item.");
        yield return new("gene29.f3.validate.namespace-then-directive", () => Src("namespace N;\n#region r\nclass C\n{\n}\n#endregion\n").Detach(),
            "namespace N;\n\n#region r\n\nclass C\n{\n}\n\n#endregion\n");
        yield return new("gene29.f3.validate.throw-left-of-coalesce", () => MoveFirst("class C\n{\n    object M(object a) => a ?? throw new E();\n}\n", n => n is CsExpression { Kind: ExpressionKind.Throw }),
            "throws GenValidationException: GEN083 Types[0].Members[0].Body.Expression.Arguments[0]: Throw expression requires a coalescing/conditional branch, switch arm or expression-bodied function/member.");
        yield return new("gene29.f3.validate.throw-condition", () => Value(E(ExpressionKind.Conditional, E(ExpressionKind.Throw, Id("e")), Id("a"), Id("b"))),
            "invalid: GEN083 Throw expression requires a coalescing/conditional branch, switch arm or expression-bodied function/member.");
        yield return new("gene29.f3.validate.throw-conditional-branch", () => Value(E(ExpressionKind.Conditional, Id("c"), Id("a"), E(ExpressionKind.Throw, Id("e")))),
            "class C\n{\n    void M()\n    {\n        object x = c ? a : throw e;\n    }\n}\n");
        yield return new("gene29.f3.validate.named-argument-invoked", () => MoveFirst("class C\n{\n    void M()\n    {\n        F(a, b: 1);\n    }\n}\n", n => n is CsExpression { Kind: ExpressionKind.Argument }),
            "throws GenValidationException: GEN092 Types[0].Members[0].Body.Statements[0].Expression.Arguments[0]: Argument requires an invocation, construction, index or tuple.");
        yield return new("gene29.f3.validate.named-argument-indexed", () => Value(E(ExpressionKind.Index, new CsExpression(ExpressionKind.Argument, "n", new[] { Id("a") }), Id("i"))),
            "invalid: GEN092 Argument requires an invocation, construction, index or tuple.");

        // 2. Equivalences: what reads back in another shape but means the model.
        yield return new("gene29.f3.equivalent.local-group-of-one", () => RemoveFirst("class C\n{\n    void M()\n    {\n        int low = 0, high = 1;\n    }\n}\n", n => n is CsVariableDeclarator { Name: "low" }),
            "class C\n{\n    void M()\n    {\n        int high = 1;\n    }\n}\n");
        yield return new("gene29.f3.equivalent.or-pattern-moved", () => MoveFirst("class C\n{\n    bool M(int x) => x is 1 or 2 or 3;\n}\n", n => n is CsPattern { Kind: PatternKind.Constant, Value.Text: "3" }),
            "class C\n{\n    bool M(int x) => x is 3 or (1 or 2);\n}\n");
        yield return new("gene29.f3.equivalent.unicode-escaped-local", () => Src("class C\n{\n    int M()\n    {\n        int \\u0061 = 1;\n        return (int)\\u0061;\n    }\n}\n").Detach(), "class C\n{\n    int M()\n    {\n        int a = 1;\n        return (int)a;\n    }\n}\n");
        yield return new("gene29.f3.equivalent.unchecked-tree", () => Cs.Tree(Cs.Statement(Cs.Assign(Id("x"), Id("y")))).Render(new Rendering.RenderOptions { Validate = false }), "x = y;\n");
        yield return new("gene29.f3.equivalent.escaped-identifier", () => Value(Id("await")), "class C\n{\n    void M()\n    {\n        object x = @await;\n    }\n}\n");

        // Comments read back on the parser's owner, or, where that differs, on the owner AD B2 gives their place in the
        // output (here the expression they lead and trail; the parser gives them to the declaration).
        yield return new("gene29.f3.equivalent.comments-around-a-value", () => Value(Id("a") with { Comments = new[] { Block("one"), Block("two"), Block("three", CommentPlacement.Trailing), Block("four", CommentPlacement.Trailing) } }),
            "class C\n{\n    void M()\n    {\n        object x = /* one */ /* two */ a /* three */  /* four */ ;\n    }\n}\n");
        yield return new("gene29.f3.equivalent.comments-in-an-argument-list", () => InMethod(Cs.Statement(Cs.Call(Id("F"), Cs.Binary(CsOperator.Add, Id("a"), Id("b") with { Comments = new[] { Block("b", CommentPlacement.Trailing) } }), Id("c")))),
            "class C\n{\n    void M()\n    {\n        F(a + b /* b */ , c);\n    }\n}\n");
        yield return new("gene29.f3.emit.document-trailing-comment", () => new CsDocument { Comments = new[] { Line("t", CommentPlacement.Trailing) } }, "invalid: EMIT001 Rendered output does not read back as the model: comment // t reads back on another owner.");

        // 3. Repair: parentheses the precedence rules miss are added where the read-back regroups.
        yield return new("gene29.f3.repair.relational-conditional", () => Value(E(ExpressionKind.Is, Id("a")) with { Pattern = new CsPattern(PatternKind.Relational) { Operator = CsOperator.LessThan, Value = E(ExpressionKind.Conditional, Cs.Literal(true), Cs.Literal(1), Cs.Literal(2)) } }),
            "class C\n{\n    void M()\n    {\n        object x = a is < (true ? 1 : 2);\n    }\n}\n");

        // 4. The fault hook: a read-back that differs from the output in each way the guard compares.
        yield return new("gene29.f3.fault.operator", Faulted(t => Replace(t, "a - b", "a + b"), () => Value(Cs.Binary(CsOperator.Subtract, Id("a"), Id("b"))).Render()),
            "throws GenValidationException: EMIT001 CsExpression: Rendered output does not read back as the model: expression Binary reads back as expression Binary.");
        yield return new("gene29.f3.fault.member-kind", Faulted(t => Replace(t, "int F;", "int F { get; }"), () => InClass(new CsField(Cs.Int, "F")).Render()),
            "throws GenValidationException: EMIT001 CsField: Rendered output does not read back as the model: CsField reads back as CsProperty.");
        yield return new("gene29.f3.fault.argument-count", Faulted(t => Replace(t, "F(a)", "F(a, b)"), () => InMethod(Cs.Statement(Cs.Call(Id("F"), Id("a")))).Render()),
            "throws GenValidationException: EMIT001 CsExpression: Rendered output does not read back as the model: expression Invocation reads back with 3 parts instead of 2.");
        yield return new("gene29.f3.fault.directive-added", Faulted(t => Replace(t, "class C", "#region r\n#endregion\nclass C"), () => InClass().Render()),
            "throws GenValidationException: EMIT001 CsDocument: Rendered output does not read back as the model: directive #region r reads back where the model holds none.");
        yield return new("gene29.f3.fault.directive-lost", Faulted(t => Replace(t, "#pragma warning disable CS0168\n", ""), () => InClass(new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Disable, Warnings = new[] { "CS0168" } }, new CsField(Cs.Int, "F")).Render()),
            "throws GenValidationException: EMIT001 CsDocument: Rendered output does not read back as the model: directive #pragma warning disable CS0168 does not read back.");
        yield return new("gene29.f3.fault.comment-added", Faulted(t => Replace(t, "int F;", "int F; // extra"), () => InClass(new CsField(Cs.Int, "F")).Render()),
            "throws GenValidationException: EMIT001 CsField: Rendered output does not read back as the model: comment // extra reads back on another owner.");
        yield return new("gene29.f3.fault.comment-lost", Faulted(t => Replace(t, "    // about F\n", ""), () => InClass(new CsField(Cs.Int, "F") { Comments = new[] { Line("about F") } }).Render()),
            "throws GenValidationException: EMIT001 CsField: Rendered output does not read back as the model: comment // about F reads back on another owner.");
        yield return new("gene29.f3.fault.cast-read-as-product", Faulted(t => Replace(t, "(T)y", "T * y"), () => Value(E(ExpressionKind.Cast, Id("y")) with { Type = Cs.Type("T") }).Render()), "throws GenValidationException: EMIT001 CsExpression: Rendered output does not read back as the model: expression Cast reads back as expression Binary.");
        yield return new("gene29.f3.fault.or-read-as-and", Faulted(t => Replace(t, " or ", " and "), () => Value(E(ExpressionKind.Is, Id("x")) with { Pattern = new CsPattern(PatternKind.Or) { Patterns = new[] { Cs.ConstantPattern(Id("A")), Cs.ConstantPattern(Id("B")) } } }).Render()), "throws GenValidationException: EMIT001 CsPattern: Rendered output does not read back as the model: CsPattern reads back as CsPattern.");
        yield return new("gene29.f3.fault.relational-turned", Faulted(t => Replace(t, "< 5", "> 5"), () => Value(E(ExpressionKind.Is, Id("x")) with { Pattern = new CsPattern(PatternKind.Relational) { Operator = CsOperator.LessThan, Value = Cs.Literal(5) } }).Render()), "throws GenValidationException: EMIT001 CsPattern: Rendered output does not read back as the model: CsPattern reads back as CsPattern.");
        yield return new("gene29.f3.fault.tree", Faulted(t => Replace(t, "a - b", "a + b"), () => Cs.Tree(Cs.Statement(Cs.Assign(Id("x"), Cs.Binary(CsOperator.Subtract, Id("a"), Id("b"))))).Render()),
            "throws GenValidationException: EMIT001 CsExpression: Rendered output does not read back as the model: expression Binary reads back as expression Binary.");
    }
}
