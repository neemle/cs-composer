using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Statement, loop, switch-statement and jump rules.
public static class StatementScenarios
{
    static CsExpression Id => Cs.Id("a");

    static CsStatement S(StatementKind kind) => new(kind);

    static CsStatement Run => Cs.Statement(Cs.Call(Cs.Id("Run")));

    static CsDocument Body(string statements) => Src("class C { void M(int a, object o) { " + statements + " } }");

    static CsStatement Local(string name = "x") => Cs.Local(Cs.Int, name, Cs.Literal(1));

    static CsStatement Loop(CsStatement body) => Cs.While(Cs.Literal(true), Cs.Body(body));

    static CsSwitchSection Section(CsBody body, params CsSwitchLabel[] labels) => Cs.Section(body, labels);

    static CsStatement Switch(params CsSwitchSection[] sections) => Cs.Switch(Id, sections);

    public static IEnumerable<Scenario> All()
    {
        yield return new("statement.depth", () =>
        {
            CsStatement statement = Cs.Return();
            for (int i = 0; i < 70; i++) statement = Cs.If(Cs.Literal(true), Cs.Body(statement));
            return InMethod(statement);
        }, "invalid: GEN001 Node graph exceeds128 or contains a cycle.");
        yield return new("statement.unknown-kind", () => InMethod(S((StatementKind)99)), "invalid: GEN023 Unknown statement kind.");
        yield return new("statement.disabled-text", () => InMethod(S(StatementKind.DisabledText) with { Disabled = new CsDisabledText("x\n") }), "invalid: EMIT001 Rendered output does not read back as the model: CsBody reads back with 1 parts instead of 0.");
        yield return new("statement.disabled-text-null", () => InMethod(S(StatementKind.DisabledText)), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.directive-null", () => InMethod(S(StatementKind.Directive)), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.directive", () => InMethod(S(StatementKind.Directive) with { Directive = new CsDirective(DirectiveKind.Region) { Text = "R" } }, S(StatementKind.Directive) with { Directive = new CsDirective(DirectiveKind.EndRegion) }), "class C\n{\n    void M()\n    {\n        #region R\n        #endregion\n    }\n}\n");
        yield return new("statement.local-function-null", () => InMethod(S(StatementKind.LocalFunction)), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.local-function-modifier", () => InMethod(S(StatementKind.LocalFunction) with { Function = new CsMethod(Cs.Void, "L") { Modifiers = new[] { "public" }, Body = Cs.Body() } }), "invalid: GEN031 Invalid local function modifier.");
        yield return new("statement.local-function-static-old", () => InMethod(S(StatementKind.LocalFunction) with { Function = new CsMethod(Cs.Void, "L") { Modifiers = new[] { "static", "async", "unsafe" }, Body = Cs.Body() } }).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Static local function requires C# 8.");
        yield return new("statement.local-function-extern", () => InMethod(S(StatementKind.LocalFunction) with { Function = new CsMethod(Cs.Void, "L") { Modifiers = new[] { "static", "extern" } } }), "class C\n{\n    void M()\n    {\n        static extern void L();\n    }\n}\n");
        yield return new("statement.goto", () => InMethod(new CsStatement(StatementKind.Goto, Text: "1end")), "invalid: GEN003 Invalid identifier.");
        yield return new("statement.yield", () => Body("yield return 1; yield break;"), "class C\n{\n    void M(int a, object o)\n    {\n        yield return 1;\n        yield break;\n    }\n}\n");
        yield return new("statement.lock-fixed-unsafe", () => Src("unsafe class C { void M(object o, int[] a) { lock (o) { } fixed (int* p = a) { } unsafe { } checked { } unchecked { } } }"), "unsafe class C\n{\n    void M(object o, int[] a)\n    {\n        lock (o)\n        {\n        }\n        fixed (int* p = a)\n        {\n        }\n        unsafe\n        {\n        }\n        checked\n        {\n        }\n        unchecked\n        {\n        }\n    }\n}\n");
        yield return new("statement.fixed-null-resource", () => InMethod(S(StatementKind.Fixed) with { Body = Cs.Body() }), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.continue-outside-loop", () => InMethod(Cs.Continue()), "invalid: GEN085 Continue requires an enclosing loop outside finally.");
        yield return new("statement.continue-in-loop", () => InMethod(Loop(Cs.Continue())), "class C\n{\n    void M()\n    {\n        while (true)\n        {\n            continue;\n        }\n    }\n}\n");
        yield return new("statement.local-group-null", () => InMethod(S(StatementKind.LocalGroup)), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.local-group", () => Body("int x = 1, y = 2; const int z = 3, w = 4;"), "class C\n{\n    void M(int a, object o)\n    {\n        int x = 1, y = 2;\n        const int z = 3, w = 4;\n    }\n}\n");
        yield return new("statement.var-without-initializer", () => InMethod(Cs.Var("x", null!)), "invalid: GEN067 A var declaration requires an initializer.");
        yield return new("statement.var-lambda-old", () => InMethod(Cs.Var("f", Cs.Lambda(Cs.Literal(1)))).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Natural lambda type inference requires C# 10.");
        yield return new("statement.const-local", () => InMethod(Cs.Local(Cs.Int, "x", Cs.Literal(1)) with { VariableModifiers = CsVariableModifiers.Const }), "class C\n{\n    void M()\n    {\n        const int x = 1;\n    }\n}\n");
        yield return new("statement.const-local-not-constant", () => InMethod(Cs.Local(Cs.Int, "x", Cs.Call(Cs.Id("F"))) with { VariableModifiers = CsVariableModifiers.Const }), "invalid: GEN077 A case value or relational bound must be a constant expression.");
        yield return new("statement.block-expression-body", () => InMethod(S(StatementKind.Block) with { Body = CsBody.FromExpression(Id) }), "invalid: GEN066 Embedded blocks cannot use an expression body.");
        yield return new("statement.if-else-expression", () => InMethod(Cs.If(Id, Cs.Body(), Run)), "invalid: GEN065 Else must be a block or an if statement.");
        yield return new("statement.if-else-if", () => Body("if (a == 1) { } else if (a == 2) { } else { }"), "class C\n{\n    void M(int a, object o)\n    {\n        if (a == 1)\n        {\n        }\n        else if (a == 2)\n        {\n        }\n        else\n        {\n        }\n    }\n}\n");
        yield return new("statement.try-empty", () => InMethod(Cs.Try(Cs.Body())), "invalid: GEN068 Try requires a catch or finally block.");
        yield return new("statement.try-catches-null", () => InMethod(Cs.Try(Cs.Body()) with { Catches = null!, Finally = Cs.Body() }), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.try-catch-all-named", () => InMethod(Cs.Try(Cs.Body(), new[] { new CsCatch(Cs.Body(), null, "e") })), "invalid: GEN069 Catch-all must be unnamed, unique and last.");
        yield return new("statement.try-catch-all-not-last", () => InMethod(Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body()), Cs.Catch(Cs.Type("System.Exception"), Cs.Body()) })), "invalid: GEN069 Catch-all must be unnamed, unique and last.");
        yield return new("statement.try-catch-filtered", () => Body("try { } catch when (a > 0) { } catch (System.Exception e) when (e != null) { throw; } catch { } finally { }"), "class C\n{\n    void M(int a, object o)\n    {\n        try\n        {\n        }\n        catch when (a > 0)\n        {\n        }\n        catch (System.Exception e) when (e != null)\n        {\n            throw;\n        }\n        catch\n        {\n        }\n        finally\n        {\n        }\n    }\n}\n");
        yield return new("statement.rethrow-outside", () => InMethod(Cs.Rethrow()), "invalid: GEN070 Rethrow requires an enclosing catch outside finally.");
        yield return new("statement.rethrow-in-finally", () => InMethod(Cs.Try(Cs.Body(), new[] { Cs.CatchAll(Cs.Body()) }, Cs.Body(Cs.Rethrow()))), "invalid: GEN070 Rethrow requires an enclosing catch outside finally.");
        yield return new("statement.using-both", () => InMethod(new CsStatement(StatementKind.Using, Id) { Resource = Cs.Var("r", Id), Body = Cs.Body() }), "invalid: GEN071 Using requires exactly one resource expression or declaration.");
        yield return new("statement.using-neither", () => InMethod(new CsStatement(StatementKind.Using) { Body = Cs.Body() }), "invalid: GEN071 Using requires exactly one resource expression or declaration.");
        yield return new("statement.using-resource-kind", () => InMethod(Cs.Using(Run, Cs.Body())), "invalid: GEN071 Using requires an initialized local declaration.");
        yield return new("statement.using-resource-uninitialized", () => InMethod(Cs.Using(Cs.Local(Cs.Type("R"), "r"), Cs.Body())), "invalid: GEN071 Using requires an initialized local declaration.");
        yield return new("statement.using-resource-group-uninitialized", () => InMethod(Cs.Using(new CsStatement(StatementKind.LocalGroup) { ForInitializer = Cs.ForDeclaration(Cs.Type("R"), Cs.Variable("r", Id), Cs.Variable("s")) }, Cs.Body())), "invalid: GEN071 Using requires an initialized local declaration.");
        yield return new("statement.using-resource-group", () => Body("using (System.IDisposable r = null, s = null) { }"), "class C\n{\n    void M(int a, object o)\n    {\n        using (System.IDisposable r = null, s = null)\n        {\n        }\n    }\n}\n");
        yield return new("statement.using-declaration", () => Body("using var r = (System.IDisposable)o; using System.IDisposable s = null, t = null; await using var u = (System.IAsyncDisposable)o;"), "class C\n{\n    void M(int a, object o)\n    {\n        using var r = (System.IDisposable)o;\n        using System.IDisposable s = null, t = null;\n        await using var u = (System.IAsyncDisposable)o;\n    }\n}\n");
        yield return new("statement.using-declaration-old", () => Body("using var r = (System.IDisposable)o;").At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Using declaration requires C# 8.");
        yield return new("statement.using-declaration-null", () => InMethod(S(StatementKind.UsingDeclaration)), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.using-declaration-kind", () => InMethod(S(StatementKind.UsingDeclaration) with { Resource = Run }), "invalid: GEN071 Using declaration needs an initialized local.");
        yield return new("statement.using-declaration-uninitialized", () => InMethod(S(StatementKind.UsingDeclaration) with { Resource = Cs.Local(Cs.Type("R"), "r") }), "invalid: GEN071 Using declaration needs an initialized local.");
        yield return new("statement.using-declaration-group-uninitialized", () => InMethod(S(StatementKind.UsingDeclaration) with { Resource = new CsStatement(StatementKind.LocalGroup) { ForInitializer = Cs.ForDeclaration(Cs.Type("R"), Cs.Variable("r")) } }), "invalid: GEN071 Using declaration needs an initialized local.");
        yield return new("statement.label-loop", () => Body("outer: while (true) { inner: for (;;) { break outer; } }"), "class C\n{\n    void M(int a, object o)\n    {\n        outer:\n        while (true)\n        {\n            inner:\n            for (; ; )\n            {\n                break outer;\n            }\n        }\n    }\n}\n");
        yield return new("statement.label-switch", () => Body("cases: switch (a) { case 1: break cases; default: break; }"), "class C\n{\n    void M(int a, object o)\n    {\n        cases:\n        switch (a)\n        {\n            case 1:\n                break cases;\n            default:\n                break;\n        }\n    }\n}\n");
        yield return new("statement.label-plain", () => Body("done: Run(); goto done;"), "class C\n{\n    void M(int a, object o)\n    {\n        done:\n        Run();\n        goto done;\n    }\n}\n");
        yield return new("statement.label-empty-body", () => InMethod(new CsStatement(StatementKind.Label, Text: "l")), "invalid: GEN002 Required node or collection is null.");
        yield return new("statement.label-duplicate", () => InMethod(new CsStatement(StatementKind.Label, Text: "l") { Body = Cs.Body(Loop(new CsStatement(StatementKind.Label, Text: "l") { Body = Cs.Body(Loop(Cs.Break())) })) }), "invalid: GEN092 Duplicate enclosing label.");
        yield return new("statement.labeled-break-unknown", () => InMethod(Loop(new CsStatement(StatementKind.Break, Text: "nowhere"))), "invalid: GEN092 Named jump requires an enclosing labeled loop or switch; continue requires a loop.");
        yield return new("statement.labeled-continue-switch", () => InMethod(new CsStatement(StatementKind.Label, Text: "l") { Body = Cs.Body(Switch(Section(Cs.Body(Loop(new CsStatement(StatementKind.Continue, Text: "l"))), Cs.Default()))) }), "invalid: GEN092 Named jump requires an enclosing labeled loop or switch; continue requires a loop.");
        yield return new("statement.labeled-break-old", () => Body("l: while (true) { break l; }").At(CsLanguageVersion.CSharp14), "invalid: GEN073 Labeled break/continue requires C# 15.");

        // Payload rules.
        yield return new("payload.await-on-expression", () => InMethod(Run with { IsAwait = true }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.await-old", () => Body("await foreach (var x in (System.Collections.Generic.IAsyncEnumerable<int>)o) { }").At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Asynchronous iteration/disposal requires C# 8.");
        yield return new("payload.body-on-return", () => InMethod(Cs.Return() with { Body = Cs.Body() }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.type-on-return", () => InMethod(Cs.Return() with { Type = Cs.Int }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.type-on-var", () => InMethod(Cs.Var("x", Cs.Literal(1)) with { Type = Cs.Int }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.var-on-return", () => InMethod(Cs.Return() with { IsVar = true }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.else-on-while", () => InMethod(Loop(Cs.Break()) with { Else = Cs.Block() }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.finally-on-block", () => InMethod(Cs.Block() with { Finally = Cs.Body() }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.catches-on-block", () => InMethod(Cs.Block() with { Catches = new[] { Cs.CatchAll(Cs.Body()) } }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.resource-on-block", () => InMethod(Cs.Block() with { Resource = Local() }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.expression-on-break", () => InMethod(Loop(Cs.Break() with { Expression = Id })), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.text-on-return", () => InMethod(Cs.Return() with { Text = "x" }), "invalid: EMIT001 Rendered output does not read back as the model: statement Return reads back as statement Return.");
        yield return new("payload.sections-on-block", () => InMethod(Cs.Block() with { Sections = new[] { Section(Cs.Body(Cs.Break()), Cs.Default()) } }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.initializer-on-while", () => InMethod(Loop(Cs.Break()) with { ForInitializer = Cs.ForVar("i", Cs.Literal(0)) }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.iterators-on-while", () => InMethod(Loop(Cs.Break()) with { Iterators = new[] { Cs.PostIncrement(Id) } }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("payload.sections-null", () => InMethod(Cs.Block() with { Sections = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.iterators-null", () => InMethod(Cs.Block() with { Iterators = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.disabled-on-block", () => InMethod(Cs.Block() with { Disabled = new CsDisabledText("x") }), "invalid: GEN065 Disabled text belongs to inactive source.");
        yield return new("payload.directive-on-block", () => InMethod(Cs.Block() with { Directive = new CsDirective(DirectiveKind.Region) }), "invalid: GEN092 Conditional or region directive is not closed. | GEN065 Directive payload belongs to a directive.");
        yield return new("payload.binding-on-block", () => InMethod(Cs.Block() with { Binding = Id }), "invalid: GEN065 Binding belongs to foreach.");
        yield return new("payload.modifiers-on-block", () => InMethod(Cs.Block() with { VariableModifiers = CsVariableModifiers.Ref }), "invalid: GEN065 Variable modifiers belong to a local or foreach binding.");
        yield return new("payload.function-on-block", () => InMethod(Cs.Block() with { Function = new CsMethod(Cs.Void, "L") { Body = Cs.Body() } }), "invalid: GEN065 Function payload belongs to a local function.");
        yield return new("payload.catches-null", () => InMethod(Cs.Block() with { Catches = null! }), "invalid: GEN002 Required node or collection is null.");

        // Loops.
        yield return new("loop.for-full", () => Body("for (int i = 0, j = 1; i < j; i++, j--) { continue; }"), "class C\n{\n    void M(int a, object o)\n    {\n        for (int i = 0, j = 1; i < j; i++, j--)\n        {\n            continue;\n        }\n    }\n}\n");
        yield return new("loop.for-expressions", () => Body("int i; for (i = 0, a = 1; ; i++) { break; }"), "class C\n{\n    void M(int a, object o)\n    {\n        int i;\n        for (i = 0, a = 1; ; i++)\n        {\n            break;\n        }\n    }\n}\n");
        yield return new("loop.for-condition-literal", () => InMethod(Cs.For(null, Cs.Literal(1), Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN086 A loop condition must be Boolean.");
        yield return new("loop.while-condition-literal", () => InMethod(Cs.While(Cs.Literal(0), Cs.Body(Cs.Break()))), "invalid: GEN086 A loop condition must be Boolean.");
        yield return new("loop.do-while", () => InMethod(Cs.DoWhile(Cs.Body(Cs.Break()), Cs.Literal(false))), "class C\n{\n    void M()\n    {\n        do\n        {\n            break;\n        }\n        while (false);\n    }\n}\n");
        yield return new("loop.iterators-null", () => InMethod(Cs.For(Cs.Body(Cs.Break())) with { Iterators = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("loop.iterator-not-statement", () => InMethod(Cs.For(null, null, new[] { Cs.Binary(CsOperator.Add, Id, Id) }, Cs.Body(Cs.Break()))), "invalid: GEN087 For initializers and iterators require statement expressions.");
        yield return new("loop.iterator-assign-literal", () => InMethod(Cs.For(null, null, new[] { Cs.Assign(Cs.Literal(1), Id) }, Cs.Body(Cs.Break()))), "invalid: GEN088 An assignment requires a writable target.");
        yield return new("loop.foreach-binding", () => Body("foreach (var (x, y) in (System.Collections.Generic.IEnumerable<(int, int)>)o) { } foreach ((int p, int q) in (System.Collections.Generic.IEnumerable<(int, int)>)o) { }"), "class C\n{\n    void M(int a, object o)\n    {\n        foreach (var (x, y) in (System.Collections.Generic.IEnumerable<(int, int)>)o)\n        {\n        }\n        foreach ((int p, int q) in (System.Collections.Generic.IEnumerable<(int, int)>)o)\n        {\n        }\n    }\n}\n");
        yield return new("loop.foreach-void", () => InMethod(Cs.ForEach(Cs.Void, "x", Id, Cs.Body())), "invalid: GEN084 A loop variable cannot have type void.");
        yield return new("loop.foreach-var", () => InMethod(Cs.ForEach("x", Id, Cs.Body())), "class C\n{\n    void M()\n    {\n        foreach (var x in a)\n        {\n        }\n    }\n}\n");
        yield return new("loop.initializer-depth", () =>
        {
            CsStatement statement = Cs.For(Cs.ForVar("i", Cs.Literal(0)), null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()));
            for (int i = 0; i < 63; i++) statement = Cs.Block(statement);
            return InMethod(statement);
        }, "invalid: GEN001 Node graph exceeds128 or contains a cycle.");
        yield return new("loop.initializer-mixed", () => InMethod(Cs.For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i", Cs.Literal(0))) with { Expressions = new[] { Cs.PostIncrement(Id) } }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN084 For initialization cannot mix a declaration and statement expressions.");
        yield return new("loop.initializer-var-typed", () => InMethod(Cs.For(Cs.ForVar("i", Cs.Literal(0)) with { Type = Cs.Int }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN065 An inferred variable cannot have an explicit type.");
        yield return new("loop.initializer-no-variables", () => InMethod(Cs.For(new CsForInitializer { Type = Cs.Int }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN084 A declaration requires variables; var requires exactly one.");
        yield return new("loop.initializer-var-two", () => InMethod(Cs.For(Cs.ForVar("i", Cs.Literal(0)) with { Variables = new[] { Cs.Variable("i", Cs.Literal(0)), Cs.Variable("j", Cs.Literal(0)) } }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN084 A declaration requires variables; var requires exactly one.");
        yield return new("loop.initializer-variables-null", () => InMethod(Cs.For(new CsForInitializer { Type = Cs.Int, Variables = null! }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN002 Required node or collection is null.");
        yield return new("loop.initializer-var-variables-null", () => InMethod(Cs.For(new CsForInitializer { IsVar = true, Variables = null! }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN084 A declaration requires variables; var requires exactly one. | GEN002 Required node or collection is null.");
        yield return new("loop.initializer-expressions-null", () => InMethod(Cs.For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i")) with { Expressions = null! }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN002 Required node or collection is null.");
        yield return new("loop.initializer-fixed-size", () => InMethod(Cs.For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i") with { FixedSize = Cs.Literal(2) }), null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN065 Fixed buffer size belongs to field declarations.");
        yield return new("loop.initializer-duplicate", () => InMethod(Cs.For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i"), Cs.Variable("@i")), null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN084 Duplicate for variable name.");
        yield return new("loop.initializer-null-name", () => InMethod(Cs.For(Cs.ForDeclaration(Cs.Int, Cs.Variable(null!)), null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN003 Invalid identifier.");
        yield return new("loop.initializer-var-uninitialized", () => InMethod(Cs.For(new CsForInitializer { IsVar = true, Variables = new[] { Cs.Variable("i") } }, null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN067 An inferred variable requires an initializer.");
        yield return new("loop.initializer-var-lambda-old", () => InMethod(Cs.For(Cs.ForVar("f", Cs.Lambda(Cs.Literal(1))), null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Natural lambda type inference requires C# 10.");
        yield return new("loop.initializer-const", () => InMethod(new CsStatement(StatementKind.LocalGroup) { ForInitializer = Cs.ForDeclaration(Cs.Int, Cs.Variable("i", Cs.Call(Cs.Id("F")))) with { VariableModifiers = CsVariableModifiers.Const } }), "invalid: GEN077 A case value or relational bound must be a constant expression.");
        yield return new("loop.initializer-expression", () => InMethod(Cs.For(Cs.ForExpressions(Cs.Assign(Id, Cs.Literal(0)), Cs.Literal(1)), null, Values<CsExpression>.Empty, Cs.Body(Cs.Break()))), "invalid: GEN087 For initializers and iterators require statement expressions.");

        // Switch statements and jumps.
        yield return new("switch.no-labels", () => InMethod(Switch(Section(Cs.Body(Cs.Break())))), "invalid: GEN080 A switch section requires at least one label.");
        yield return new("switch.labels-null", () => InMethod(Switch(new CsSwitchSection(null!, Cs.Body(Cs.Break())))), "invalid: GEN002 Required node or collection is null.");
        yield return new("switch.two-defaults", () => InMethod(Switch(Section(Cs.Body(Cs.Break()), Cs.Default(), Cs.Default()))), "invalid: GEN080 Only one default label is permitted.");
        yield return new("switch.default-guard", () => InMethod(Switch(Section(Cs.Body(Cs.Break()), new CsSwitchLabel(null, Cs.Literal(true))))), "invalid: GEN080 Default cannot have a case guard.");
        yield return new("switch.discard-label", () => InMethod(Switch(Section(Cs.Body(Cs.Break()), Cs.Case(Cs.DiscardPattern())))), "invalid: GEN080 Use default or case var _ in a switch statement.");
        yield return new("switch.guarded-case", () => Body("switch (o) { case int n when n > 0: break; case string s: return; default: throw null; }"), "class C\n{\n    void M(int a, object o)\n    {\n        switch (o)\n        {\n            case int n when n > 0:\n                break;\n            case string s:\n                return;\n            default:\n                throw null;\n        }\n    }\n}\n");
        yield return new("switch.expression-body", () => InMethod(Switch(Section(CsBody.FromExpression(Id), Cs.Default()))), "invalid: GEN066 Switch sections require a statement list. | GEN081 A switch section cannot fall through; use break, return, throw or goto.");
        yield return new("switch.fall-through", () => InMethod(Switch(Section(Cs.Body(Run), Cs.Case(Cs.Literal(1))), Section(Cs.Body(Cs.Break()), Cs.Default()))), "invalid: GEN081 A switch section cannot fall through; use break, return, throw or goto.");
        yield return new("switch.break-outside", () => InMethod(Cs.Break()), "invalid: GEN082 Break requires an enclosing loop or switch outside finally.");
        yield return new("switch.goto-case-outside", () => InMethod(Cs.GotoCase(Cs.Literal(1))), "invalid: GEN082 Switch jump requires an enclosing switch outside finally.");
        yield return new("switch.goto-default-outside", () => InMethod(Cs.GotoDefault()), "invalid: GEN082 Switch jump requires an enclosing switch outside finally.");
        yield return new("switch.goto-default-missing", () => InMethod(Switch(Section(Cs.Body(Cs.GotoDefault()), Cs.Case(Cs.Literal(1))))), "invalid: GEN082 The enclosing switch has no default label.");
        yield return new("switch.goto-default-sections-null", () => InMethod(Switch(Section(Cs.Body(Cs.Break()), Cs.Case(Cs.Literal(2)))) with { Sections = new[] { Section(Cs.Body(Cs.Switch(Cs.Id("b"), Section(Cs.Body(Cs.GotoDefault()), Cs.Case(Cs.Literal(1))))), Cs.Case(Cs.Literal(3))) } }), "invalid: GEN082 The enclosing switch has no default label.");
        yield return new("switch.goto-default-null-section", () => InMethod(Cs.Switch(Id, Section(Cs.Body(Cs.GotoDefault()), Cs.Default()), null!)), "invalid: GEN002 Required node or collection is null.");
        yield return new("switch.goto-default-null-label", () => InMethod(Cs.Switch(Id, Section(Cs.Body(Cs.GotoDefault()), Cs.Default(), null!))), "invalid: GEN002 Required node or collection is null.");
        yield return new("switch.goto-case", () => Body("switch (a) { case 1: goto case 2; case 2: goto default; default: break; }"), "class C\n{\n    void M(int a, object o)\n    {\n        switch (a)\n        {\n            case 1:\n                goto case 2;\n            case 2:\n                goto default;\n            default:\n                break;\n        }\n    }\n}\n");
        yield return new("switch.goto-case-not-constant", () => InMethod(Switch(Section(Cs.Body(Cs.GotoCase(Cs.Call(Cs.Id("F")))), Cs.Case(Cs.Literal(1))), Section(Cs.Body(Cs.Break()), Cs.Default()))), "invalid: GEN077 A case value or relational bound must be a constant expression.");
    }
}
