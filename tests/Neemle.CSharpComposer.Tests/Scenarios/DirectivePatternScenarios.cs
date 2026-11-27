using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Directive payloads and sequences, expression payloads and pattern rules.
public static class DirectivePatternScenarios
{
    static CsDirective D(DirectiveKind kind) => new(kind);

    // One directive as a class member, closed when it opens a region or conditional.
    static CsDocument Member(CsDirective directive) => InClass(directive.Kind switch
    {
        DirectiveKind.If => new CsMember[] { directive, D(DirectiveKind.EndIf) },
        DirectiveKind.Region => new CsMember[] { directive, D(DirectiveKind.EndRegion) },
        _ => new CsMember[] { directive },
    });

    static CsDocument Sequence(params DirectiveKind[] kinds) => InClass(kinds.Select(k => (CsMember)(k is DirectiveKind.If or DirectiveKind.Elif ? D(k) with { Condition = Cs.Id("A") } : D(k))).ToArray());

    static CsExpression Id => Cs.Id("a");

    static CsDocument Is(CsPattern pattern) => Value(new CsExpression(ExpressionKind.Is, "", new[] { Id }) { Pattern = pattern });

    static CsDocument Arm(CsPattern pattern) => Value(Cs.SwitchExpression(Id, Cs.Arm(pattern, Cs.Literal(1)), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(0))));

    static CsPattern P(PatternKind kind) => new(kind);

    static CsPattern Const(object value) => Cs.ConstantPattern(Cs.Literal(value));

    static CsSymbol Name(string name) => new(name, new CsSymbolOptions());

    static CsDocument Code(string statements) => Src("class C { void M(object o, int[] a) { " + statements + " } }");

    public static IEnumerable<Scenario> All()
    {
        // Directive payloads.
        yield return new("directive.unknown-kind", () => Member(D((DirectiveKind)99)), "invalid: GEN092 Unknown directive or action.");
        yield return new("directive.unknown-action", () => Member(D(DirectiveKind.Region) with { Action = (DirectiveAction)99 }), "invalid: GEN092 Unknown directive or action. | GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.name", () => Member(D(DirectiveKind.Region) with { Name = "x" }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.modifiers", () => Member(D(DirectiveKind.Region) with { Modifiers = new[] { "public" } }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.attributes", () => Member(D(DirectiveKind.Region) with { Attributes = new[] { Cs.Attribute("A") } }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.explicit-interface", () => Member(D(DirectiveKind.Region) with { ExplicitInterface = Cs.Type("I") }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.condition-on-region", () => Member(D(DirectiveKind.Region) with { Condition = Cs.Id("A") }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.symbol-on-region", () => Member(D(DirectiveKind.Region) with { Symbol = "A" }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.text-on-if", () => Member(D(DirectiveKind.If) with { Condition = Cs.Id("A"), Text = "x" }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.file-on-region", () => Member(D(DirectiveKind.Region) with { File = "a.cs" }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.line-on-region", () => Member(D(DirectiveKind.Region) with { Line = 1 }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.start-on-region", () => Member(D(DirectiveKind.Region) with { Start = new CsLinePosition(1, 1) }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.end-on-region", () => Member(D(DirectiveKind.Region) with { End = new CsLinePosition(1, 1) }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.offset-on-region", () => Member(D(DirectiveKind.Region) with { CharacterOffset = 1 }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.checksum-on-region", () => Member(D(DirectiveKind.Region) with { Checksum = "00" }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.algorithm-on-region", () => Member(D(DirectiveKind.Region) with { ChecksumAlgorithm = "x" }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.warnings-on-region", () => Member(D(DirectiveKind.Region) with { Warnings = new[] { "CS0001" } }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.warnings-null-on-region", () => Member(D(DirectiveKind.Region) with { Warnings = null! }), "class C\n{\n    #region\n    #endregion\n}\n");
        yield return new("directive.action-on-region", () => Member(D(DirectiveKind.Region) with { Action = DirectiveAction.Enable }), "invalid: GEN065 Payload does not belong to this directive kind.");
        yield return new("directive.condition-invalid", () => Member(D(DirectiveKind.If) with { Condition = Cs.Binary(CsOperator.Add, Cs.Id("A"), Cs.Literal(1)) }), "invalid: GEN092 Preprocessor condition permits symbols, booleans, !, ==, !=, && and ||.");
        yield return new("directive.condition-forms", () => Member(D(DirectiveKind.If) with { Condition = Cs.Binary(CsOperator.AndAlso, new CsExpression(ExpressionKind.Parenthesized, "", new[] { Cs.Unary(CsOperator.Not, Cs.Id("A")) }), Cs.Binary(CsOperator.OrElse, Cs.Binary(CsOperator.Equal, Cs.Id("B"), Cs.Literal(true)), Cs.Binary(CsOperator.NotEqual, Cs.Id("C"), Cs.Literal(false)))) }), "class C\n{\n#if (!A) && (B == true || C != false)\n#endif\n}\n");
        yield return new("directive.condition-number", () => Member(D(DirectiveKind.If) with { Condition = Cs.Literal(1) }), "invalid: GEN092 Preprocessor condition permits symbols, booleans, !, ==, !=, && and ||.");
        yield return new("directive.condition-negate", () => Member(D(DirectiveKind.If) with { Condition = Cs.Unary(CsOperator.Negate, Cs.Id("A")) }), "invalid: GEN092 Preprocessor condition permits symbols, booleans, !, ==, !=, && and ||.");
        yield return new("directive.condition-member", () => Member(D(DirectiveKind.If) with { Condition = Cs.Id("A").Member("B") }), "invalid: GEN092 Preprocessor condition permits symbols, booleans, !, ==, !=, && and ||.");
        yield return new("directive.define", () => InClass(D(DirectiveKind.Define) with { Symbol = "A" }, D(DirectiveKind.Undef) with { Symbol = "B" }), "class C\n{\n#define A\n#undef B\n}\n");
        yield return new("directive.define-verbatim", () => Member(D(DirectiveKind.Define) with { Symbol = "@A" }), "invalid: GEN092 Invalid preprocessor symbol.");
        yield return new("directive.define-true", () => Member(D(DirectiveKind.Define) with { Symbol = "true" }), "invalid: GEN092 Invalid preprocessor symbol.");
        yield return new("directive.define-null", () => Member(D(DirectiveKind.Define)), "invalid: GEN003 Invalid identifier.");
        yield return new("directive.text-newline", () => Member(D(DirectiveKind.Warning) with { Text = "a\nb" }), "invalid: GEN092 Directive text must occupy one line.");
        yield return new("directive.text-return", () => Member(D(DirectiveKind.Error) with { Text = "a\rb" }), "invalid: GEN092 Directive text must occupy one line.");
        yield return new("directive.symbol-newline", () => Member(D(DirectiveKind.Define) with { Symbol = "A\nB" }), "invalid: GEN003 Invalid identifier. | GEN092 Directive text must occupy one line.");
        yield return new("directive.nullable", () => InClass(D(DirectiveKind.Nullable) with { Action = DirectiveAction.Enable, Text = "warnings" }, D(DirectiveKind.Nullable) with { Action = DirectiveAction.Disable, Text = "annotations" }, D(DirectiveKind.Nullable) with { Action = DirectiveAction.Restore }), "class C\n{\n#nullable enable warnings\n#nullable disable annotations\n#nullable restore\n}\n");
        yield return new("directive.nullable-old", () => Member(D(DirectiveKind.Nullable) with { Action = DirectiveAction.Enable }).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable directive requires C# 8.");
        yield return new("directive.nullable-action", () => Member(D(DirectiveKind.Nullable) with { Action = DirectiveAction.Hidden }), "invalid: GEN092 Invalid nullable directive action or target.");
        yield return new("directive.nullable-target", () => Member(D(DirectiveKind.Nullable) with { Action = DirectiveAction.Enable, Text = "everything" }), "invalid: GEN092 Invalid nullable directive action or target.");
        yield return new("directive.pragma-warning", () => Member(D(DirectiveKind.PragmaWarning) with { Action = DirectiveAction.Disable, Warnings = new[] { "CS0168", "0219" } }), "class C\n{\n#pragma warning disable CS0168, 0219\n}\n");
        yield return new("directive.pragma-warning-action", () => Member(D(DirectiveKind.PragmaWarning) with { Action = DirectiveAction.Enable }), "invalid: GEN092 Warning pragma requires disable or restore.");
        yield return new("directive.pragma-warning-null", () => Member(D(DirectiveKind.PragmaWarning) with { Action = DirectiveAction.Restore, Warnings = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("directive.pragma-warning-empty-id", () => Member(D(DirectiveKind.PragmaWarning) with { Action = DirectiveAction.Restore, Warnings = new[] { "" } }), "invalid: GEN092 Invalid warning identifier.");
        yield return new("directive.pragma-warning-bad-id", () => Member(D(DirectiveKind.PragmaWarning) with { Action = DirectiveAction.Restore, Warnings = new[] { "CS-1" } }), "invalid: GEN092 Invalid warning identifier.");
        yield return new("directive.line", () => InClass(D(DirectiveKind.Line) with { Line = 10, File = "a.cs" }, D(DirectiveKind.Line) with { Action = DirectiveAction.Default }, D(DirectiveKind.Line) with { Action = DirectiveAction.Hidden }), "class C\n{\n#line 10 \"a.cs\"\n#line default\n#line hidden\n}\n");
        yield return new("directive.line-action", () => Member(D(DirectiveKind.Line) with { Action = DirectiveAction.Enable }), "invalid: GEN092 Invalid line directive action.");
        yield return new("directive.line-none", () => Member(D(DirectiveKind.Line)), "invalid: GEN092 Line directive needs one mapping or default/hidden.");
        yield return new("directive.line-both", () => Member(D(DirectiveKind.Line) with { Line = 1, Start = new CsLinePosition(1, 1), End = new CsLinePosition(1, 2), File = "a.cs" }), "invalid: GEN092 Line directive needs one mapping or default/hidden.");
        yield return new("directive.line-default-line", () => Member(D(DirectiveKind.Line) with { Action = DirectiveAction.Default, Line = 1 }), "invalid: GEN092 Line directive needs one mapping or default/hidden.");
        yield return new("directive.line-default-start", () => Member(D(DirectiveKind.Line) with { Action = DirectiveAction.Default, Start = new CsLinePosition(1, 1), End = new CsLinePosition(1, 1), File = "a.cs" }), "invalid: GEN092 Line directive needs one mapping or default/hidden.");
        yield return new("directive.line-default-file", () => Member(D(DirectiveKind.Line) with { Action = DirectiveAction.Hidden, File = "a.cs" }), "invalid: GEN092 Line directive needs one mapping or default/hidden.");
        yield return new("directive.line-zero", () => Member(D(DirectiveKind.Line) with { Line = 0 }), "invalid: GEN092 Mapped line must be positive.");
        yield return new("directive.line-span", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 2), End = new CsLinePosition(3, 4), CharacterOffset = 5, File = "a.cs" }), "class C\n{\n#line (1, 2)-(3, 4) 5 \"a.cs\"\n}\n");
        yield return new("directive.line-span-old", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 2), End = new CsLinePosition(3, 4), File = "a.cs" }).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Enhanced line mapping requires C# 10.");
        yield return new("directive.line-span-no-end", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 2), File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-start-line", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(0, 2), End = new CsLinePosition(3, 4), File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-start-column", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 0), End = new CsLinePosition(3, 4), File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-end-before", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(3, 2), End = new CsLinePosition(1, 4), File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-end-column", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 2), End = new CsLinePosition(3, 0), File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-same-line", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 5), End = new CsLinePosition(1, 4), File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-same-line-ok", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 4), End = new CsLinePosition(1, 5), File = "a.cs" }), "class C\n{\n#line (1, 4)-(1, 5) \"a.cs\"\n}\n");
        yield return new("directive.line-span-offset", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 2), End = new CsLinePosition(3, 4), CharacterOffset = -1, File = "a.cs" }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-span-file", () => Member(D(DirectiveKind.Line) with { Start = new CsLinePosition(1, 2), End = new CsLinePosition(3, 4) }), "invalid: GEN092 Invalid line span mapping.");
        yield return new("directive.line-end-only", () => Member(D(DirectiveKind.Line) with { Line = 1, End = new CsLinePosition(1, 2) }), "invalid: GEN065 Line endpoint requires a start position.");
        yield return new("directive.line-offset-only", () => Member(D(DirectiveKind.Line) with { Line = 1, CharacterOffset = 2 }), "invalid: GEN065 Line endpoint requires a start position.");
        yield return new("directive.checksum", () => Member(D(DirectiveKind.PragmaChecksum) with { File = "a.cs", ChecksumAlgorithm = "{406ea660-64cf-4c82-b6f0-42d48172a799}", Checksum = "0aF1" }), "class C\n{\n#pragma checksum \"a.cs\" \"{406ea660-64cf-4c82-b6f0-42d48172a799}\" \"0aF1\"\n}\n");
        yield return new("directive.checksum-file", () => Member(D(DirectiveKind.PragmaChecksum) with { ChecksumAlgorithm = "{406ea660-64cf-4c82-b6f0-42d48172a799}", Checksum = "00" }), "invalid: GEN092 Invalid checksum directive.");
        yield return new("directive.checksum-algorithm", () => Member(D(DirectiveKind.PragmaChecksum) with { File = "a.cs", ChecksumAlgorithm = "sha", Checksum = "00" }), "invalid: GEN092 Invalid checksum directive.");
        yield return new("directive.checksum-null", () => Member(D(DirectiveKind.PragmaChecksum) with { File = "a.cs", ChecksumAlgorithm = "{406ea660-64cf-4c82-b6f0-42d48172a799}" }), "invalid: GEN092 Invalid checksum directive.");
        yield return new("directive.checksum-odd", () => Member(D(DirectiveKind.PragmaChecksum) with { File = "a.cs", ChecksumAlgorithm = "{406ea660-64cf-4c82-b6f0-42d48172a799}", Checksum = "000" }), "invalid: GEN092 Invalid checksum directive.");
        yield return new("directive.checksum-hex", () => Member(D(DirectiveKind.PragmaChecksum) with { File = "a.cs", ChecksumAlgorithm = "{406ea660-64cf-4c82-b6f0-42d48172a799}", Checksum = "0g" }), "invalid: GEN092 Invalid checksum directive.");
        yield return new("directive.reference", () => InClass(D(DirectiveKind.Reference) with { File = "a.dll" }, D(DirectiveKind.Load) with { File = "b.csx" }), "class C\n{\n#r \"a.dll\"\n#load \"b.csx\"\n}\n");
        yield return new("directive.reference-null", () => Member(D(DirectiveKind.Reference)), "invalid: GEN002 Required node or collection is null.");
        yield return new("directive.load-null", () => Member(D(DirectiveKind.Load)), "invalid: GEN002 Required node or collection is null.");
        yield return new("directive.app-directives", () => InClass(D(DirectiveKind.Sdk) with { Text = "Microsoft.NET.Sdk" }, D(DirectiveKind.Package) with { Text = "A@1" }, D(DirectiveKind.Project) with { Text = "a.csproj" }, D(DirectiveKind.Property) with { Text = "A=B" }, D(DirectiveKind.Ignored) with { Text = "x" }, D(DirectiveKind.Shebang) with { Text = "/usr/bin/env dotnet" }), "class C\n{\n#:sdk Microsoft.NET.Sdk\n#:package A@1\n#:project a.csproj\n#:property A=B\n#:x\n#!/usr/bin/env dotnet\n}\n");
        yield return new("directive.app-directive-old", () => Member(D(DirectiveKind.Sdk) with { Text = "Microsoft.NET.Sdk" }).At(CsLanguageVersion.CSharp13), "invalid: GEN073 File-based application directive requires C# 14.");
        yield return new("directive.depth", () =>
        {
            CsStatement statement = new(StatementKind.Directive) { Directive = D(DirectiveKind.Region) };
            for (int i = 0; i < 64; i++) statement = Cs.Block(statement);
            return InMethod(statement);
        }, "invalid: GEN001 Node graph exceeds128 or contains a cycle.");

        // Directive sequences.
        yield return new("sequence.if-elif-else", () => Sequence(DirectiveKind.If, DirectiveKind.Elif, DirectiveKind.Else, DirectiveKind.EndIf), "class C\n{\n#if A\n#elif A\n#else\n#endif\n}\n");
        yield return new("sequence.elif-alone", () => Sequence(DirectiveKind.Elif), "invalid: GEN092 Elif requires a preceding if and cannot follow else.");
        yield return new("sequence.elif-after-else", () => Sequence(DirectiveKind.If, DirectiveKind.Else, DirectiveKind.Elif, DirectiveKind.EndIf), "invalid: GEN092 Elif requires a preceding if and cannot follow else.");
        yield return new("sequence.else-alone", () => Sequence(DirectiveKind.Else), "invalid: GEN092 Else requires a preceding if and cannot repeat. | GEN092 Conditional or region directive is not closed.");
        yield return new("sequence.else-twice", () => Sequence(DirectiveKind.If, DirectiveKind.Else, DirectiveKind.Else, DirectiveKind.EndIf), "invalid: GEN092 Else requires a preceding if and cannot repeat.");
        yield return new("sequence.endif-alone", () => Sequence(DirectiveKind.EndIf), "invalid: GEN092 Endif has no matching if.");
        yield return new("sequence.endregion-alone", () => Sequence(DirectiveKind.EndRegion), "invalid: GEN092 Endregion has no matching region.");
        yield return new("sequence.if-open", () => Sequence(DirectiveKind.If), "invalid: GEN092 Conditional or region directive is not closed.");
        yield return new("sequence.region-open", () => Sequence(DirectiveKind.Region), "invalid: GEN092 Conditional or region directive is not closed.");
        yield return new("sequence.parsed-attached", () => Src("class C\n{\n#if A\n    int F;\n#elif B\n    int G;\n#else\n    int H;\n#endif\n#region R\n    void M()\n    {\n#pragma warning disable CS0168\n        int x;\n#pragma warning restore CS0168\n    }\n#endregion\n}\n"), "class C\n{\n#if A\n    int F;\n#elif B\n    int G;\n#else\n\n    int H;\n\n#endif\n    #region R\n\n    void M()\n    {\n#pragma warning disable CS0168\n        int x;\n#pragma warning restore CS0168\n    }\n\n    #endregion\n}\n");
        yield return new("sequence.parsed-mixed-sources", () =>
        {
            var parsed = Src("class C\n{\n#region R\n    int F;\n#endregion\n}\n");
            return parsed with { Types = new[] { parsed.Types[0] with { Members = new(parsed.Types[0].Members.Append(D(DirectiveKind.Region)).Append(D(DirectiveKind.EndRegion))) } } };
        }, "class C\n{\n    #region R\n\n    int F;\n\n    #endregion\n    #region\n    #endregion\n}\n");

        // Expression payloads.
        yield return new("payload.expression.text", () => Init(E(ExpressionKind.Parenthesized, Id) with { Text = "x" }), "invalid: GEN065 This expression has no text payload.");
        yield return new("payload.expression.type", () => Init(Id with { Type = Cs.Int }), "invalid: GEN065 Type payload does not belong to this expression kind.");
        yield return new("payload.expression.cast-no-type", () => Init(E(ExpressionKind.Cast, Id)), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.expression.typeof-no-type", () => Init(E(ExpressionKind.TypeOf)), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.expression.reference-no-type", () => Init(E(ExpressionKind.TypeReference)), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.expression.refvalue-no-type", () => Init(E(ExpressionKind.RefValue, Id)), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.expression.pattern", () => Init(Id with { Pattern = Cs.DiscardPattern() }), "invalid: GEN065 Pattern belongs to is expressions.");
        yield return new("payload.expression.conditional-access", () => Init(Id with { ConditionalAccess = true }), "invalid: GEN065 Conditional access needs a member or index.");
        yield return new("payload.expression.conditional-index", () => Init(E(ExpressionKind.Index, Id, Cs.Literal(0)) with { ConditionalAccess = true }), "class C\n{\n    object F = a?[0];\n}\n");
        yield return new("payload.expression.rank", () => Init(Id with { ArrayRank = 2 }), "invalid: GEN065 Rank belongs to array construction.");
        yield return new("payload.expression.initializer-on-call", () => Init(Cs.Call(Cs.Id("F")) with { Initializer = E(ExpressionKind.Initializer) }), "invalid: GEN065 Construction initializer must be an initializer expression.");
        yield return new("payload.expression.initializer-kind", () => Init(E(ExpressionKind.New) with { Type = Cs.Type("C"), Initializer = Id }), "invalid: GEN065 Construction initializer must be an initializer expression.");
        yield return new("payload.expression.with", () => Init(E(ExpressionKind.With, Id) with { Initializer = E(ExpressionKind.Initializer, Cs.Assign(Cs.Id("X"), Cs.Literal(1))) }), "class C\n{\n    object F = a with { X = 1 };\n}\n");
        yield return new("payload.expression.stackalloc", () => InMethod(Cs.Var("s", E(ExpressionKind.StackAlloc) with { Type = Cs.Int, Initializer = E(ExpressionKind.Initializer, Cs.Literal(1)) })), "class C\n{\n    void M()\n    {\n        var s = stackalloc int[] { 1 };\n    }\n}\n");
        yield return new("payload.expression.new-target-typed-old", () => Init(E(ExpressionKind.New)).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Target-typed construction requires C# 9.");
        yield return new("payload.expression.new-array-type", () => Init(E(ExpressionKind.New) with { Type = Cs.Int.Array() }), "invalid: GEN091 Constructor invocation requires a constructible type.");
        yield return new("payload.expression.new-pointer-type", () => Init(E(ExpressionKind.New) with { Type = Cs.Int.Pointer() }), "invalid: GEN091 Constructor invocation requires a constructible type.");
        yield return new("payload.expression.new-void", () => Init(E(ExpressionKind.New) with { Type = Cs.Void }), "invalid: GEN091 Constructor invocation requires a constructible type.");
        yield return new("payload.expression.new-text", () => Init(E(ExpressionKind.New) with { Type = Cs.Type("C"), Text = "x" }), "invalid: GEN065 This expression has no text payload.");
        yield return new("payload.expression.is-both", () => Init(new CsExpression(ExpressionKind.Is, "", new[] { Id }) { Type = Cs.Int, Pattern = Cs.DiscardPattern() }), "invalid: GEN092 Is requires exactly one type or pattern.");
        yield return new("payload.expression.is-neither", () => Init(new CsExpression(ExpressionKind.Is, "", new[] { Id })), "invalid: GEN092 Is requires exactly one type or pattern.");
        yield return new("payload.expression.is-type", () => Init(new CsExpression(ExpressionKind.Is, "", new[] { Id }) { Type = Cs.Int }), "class C\n{\n    object F = a is int;\n}\n");
        yield return new("payload.expression.array-rank-zero", () => Init(E(ExpressionKind.Array, Cs.Literal(1)) with { Type = Cs.Int, ArrayRank = 0 }), "invalid: GEN092 Array rank must be between 1 and 32. | GEN092 Array dimension count must equal rank.");
        yield return new("payload.expression.array-rank-high", () => Init(E(ExpressionKind.Array, Cs.Literal(1)) with { Type = Cs.Int, ArrayRank = 33 }), "invalid: GEN092 Array rank must be between 1 and 32. | GEN092 Array dimension count must equal rank.");
        yield return new("payload.expression.array-sizes", () => Init(E(ExpressionKind.Array, Cs.Literal(1)) with { Type = Cs.Int, ArrayRank = 2 }), "invalid: GEN092 Array dimension count must equal rank.");
        yield return new("payload.expression.array-inferred-sizes", () => Init(E(ExpressionKind.Array, Cs.Literal(1)) with { Initializer = E(ExpressionKind.Initializer, Cs.Literal(1)) }), "invalid: GEN092 An inferred array requires a nonempty initializer and no explicit sizes.");
        yield return new("payload.expression.array-inferred-empty", () => Init(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer) }), "invalid: GEN092 An inferred array requires a nonempty initializer and no explicit sizes.");
        yield return new("payload.expression.array-inferred-null-arguments", () => Init(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer) with { Arguments = null! } }), "invalid: GEN002 Required node or collection is null. | GEN092 An inferred array requires a nonempty initializer and no explicit sizes.");
        yield return new("payload.expression.array-inferred", () => Init(E(ExpressionKind.Array) with { Initializer = E(ExpressionKind.Initializer, Cs.Literal(1)) }), "class C\n{\n    object F = new[] { 1 };\n}\n");
        yield return new("payload.expression.array-empty", () => Init(E(ExpressionKind.Array) with { Type = Cs.Int }), "invalid: GEN092 An array needs sizes or an initializer.");
        yield return new("payload.expression.array-void", () => Init(E(ExpressionKind.Array, Cs.Literal(1)) with { Type = Cs.Void }), "invalid: GEN092 Array element type cannot be void.");
        yield return new("payload.expression.initializer-alone", () => Init(E(ExpressionKind.Initializer)), "invalid: GEN092 Initializer requires a construction/initializer context.");
        yield return new("payload.expression.conditional-increment", () => InMethod(Cs.Statement(Cs.PostIncrement(Id.Member("b") with { ConditionalAccess = true }))), "invalid: GEN092 Conditional access cannot be incremented or decremented.");
        yield return new("payload.expression.conditional-chain-increment", () => InMethod(Cs.Statement(Cs.PreDecrement(Cs.Call(Id.Member("b") with { ConditionalAccess = true }).Member("c")))), "invalid: GEN092 Conditional access cannot be incremented or decremented.");
        yield return new("payload.expression.conditional-increment-literal", () => InMethod(Cs.Statement(Cs.PostIncrement(E(ExpressionKind.Invocation)))), "invalid: GEN088 Increment and decrement require a writable target. | GEN013 Expression has an invalid number of operands.");
        yield return new("payload.expression.conditional-null-receiver", () => InMethod(Cs.Statement(Cs.PostIncrement(Id.Member("b") with { Arguments = null! }))), "invalid: GEN002 Required node or collection is null.");
        yield return new("payload.expression.coalesce-assignment-old", () => InMethod(Cs.Statement(Cs.Assign(Id, Id) with { Operator = CsOperator.Coalesce })).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Null-coalescing assignment requires C# 8.");
        yield return new("payload.expression.conditional-assignment-old", () => InMethod(Cs.Statement(Cs.Assign(Id.Member("b") with { ConditionalAccess = true }, Id))).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Null-conditional assignment requires C# 14.");

        // Patterns.
        yield return new("pattern.null", () => Is(null!), "invalid: GEN092 Is requires exactly one type or pattern.");
        yield return new("pattern.unknown-kind", () => Is(P((PatternKind)99)), "invalid: GEN076 Unknown pattern kind.");
        yield return new("pattern.value-on-type", () => Is(Cs.TypePattern(Cs.Int) with { Value = Cs.Literal(1) }), "invalid: GEN065 Payload does not belong to this pattern kind.");
        yield return new("pattern.type-on-constant", () => Is(Const(1) with { Type = Cs.Int }), "invalid: GEN065 Payload does not belong to this pattern kind.");
        yield return new("pattern.name-on-type", () => Is(Cs.TypePattern(Cs.Int) with { Name = "x" }), "invalid: GEN065 Payload does not belong to this pattern kind.");
        yield return new("pattern.operator-on-constant", () => Is(Const(1) with { Operator = CsOperator.LessThan }), "invalid: GEN065 Payload does not belong to this pattern kind.");
        yield return new("pattern.designation-on-declaration", () => Is(Cs.DeclarationPattern(Cs.Int, "x") with { Designation = Name("y") }), "invalid: GEN065 Payload does not belong to this pattern kind. | GEN065 Compound designation belongs to a var pattern.");
        yield return new("pattern.designation-named-var", () => Is(Cs.VarPattern("x") with { Designation = Name("y") }), "invalid: GEN065 Payload does not belong to this pattern kind. | GEN065 Compound designation belongs to a var pattern.");
        yield return new("pattern.designation", () => Is(P(PatternKind.Var) with { Designation = new CsSymbol(null, new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = new CsNode[] { Name("x"), Name("y") } }) }), "class C\n{\n    void M()\n    {\n        object x = a is var (x, y);\n    }\n}\n");
        yield return new("pattern.designation-old", () => Is(P(PatternKind.Var) with { Designation = new CsSymbol(null, new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = new CsNode[] { Name("x"), Name("y") } }) }).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Deconstructing var pattern requires C# 8.");
        yield return new("pattern.designation-tuple-payload", () => Is(P(PatternKind.Var) with { Designation = new CsSymbol(null, new CsSymbolOptions { Operation = SymbolOperation.Tuple, Await = true, Parts = new CsNode[] { Name("x"), Name("y") } }) }), "invalid: GEN076 Tuple designation accepts only nested names.");
        yield return new("pattern.designation-not-symbol", () => Is(P(PatternKind.Var) with { Designation = new CsSymbol(null, new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = new CsNode[] { Name("x"), Cs.Id("y") } }) }), "invalid: GEN076 Designation requires nested symbols.");
        yield return new("pattern.designation-name-payload", () => Is(P(PatternKind.Var) with { Designation = new CsSymbol("x", new CsSymbolOptions { Await = true }) }), "invalid: GEN076 A designation is a name or tuple of names.");
        yield return new("pattern.designation-in-or", () => Is(Cs.OrPattern(P(PatternKind.Var) with { Designation = new CsSymbol(null, new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = new CsNode[] { Name("x"), Name("_") } }) }, Cs.DiscardPattern())), "invalid: GEN076 Variables cannot be declared within not/or patterns.");
        yield return new("pattern.designation-depth", () =>
        {
            CsSymbol symbol = Name("x");
            for (int i = 0; i < 130; i++) symbol = new CsSymbol(null, new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = new CsNode[] { symbol, Name("y" + i) } });
            return Is(P(PatternKind.Var) with { Designation = symbol });
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        yield return new("pattern.type-nullable", () => Is(Cs.TypePattern(Cs.Int.Nullable())), "invalid: GEN076 A pattern cannot use a nullable, pointer, void or dynamic type.");
        yield return new("pattern.type-pointer", () => Is(Cs.TypePattern(Cs.Int.Pointer())), "invalid: GEN076 A pattern cannot use a nullable, pointer, void or dynamic type.");
        yield return new("pattern.type-void", () => Is(Cs.TypePattern(Cs.Void)), "invalid: GEN076 A pattern cannot use a nullable, pointer, void or dynamic type.");
        yield return new("pattern.type-dynamic", () => Is(Cs.DeclarationPattern(Cs.Type("dynamic"), "d")), "invalid: GEN076 A pattern cannot use a nullable, pointer, void or dynamic type.");
        yield return new("pattern.type-null", () => Is(P(PatternKind.Type)), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.name-forbidden", () => Is(Cs.NotPattern(Cs.OrPattern(Cs.DeclarationPattern(Cs.Int, "x"), Cs.DiscardPattern()))), "invalid: GEN076 Variables cannot be declared within not or or patterns.");
        yield return new("pattern.name-discard-in-or", () => Arm(Cs.OrPattern(Cs.VarPattern("_"), Cs.TypePattern(Cs.Int))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("pattern.not-top-chain", () => Is(Cs.NotPattern(Cs.NotPattern(Cs.DeclarationPattern(Cs.Int, "x")))), "class C\n{\n    void M()\n    {\n        object x = a is not not int x;\n    }\n}\n");
        yield return new("pattern.not-in-arm", () => Arm(Cs.NotPattern(Cs.DeclarationPattern(Cs.Int, "x"))), "invalid: GEN076 Variables cannot be declared within not or or patterns.");
        yield return new("pattern.parenthesized-scope", () => Arm(Cs.OrPattern(Cs.ParenthesizedPattern(Cs.DeclarationPattern(Cs.Int, "x")), Cs.DiscardPattern())), "invalid: GEN076 Variables cannot be declared within not or or patterns.");
        yield return new("pattern.logical-old", () => Arm(Cs.AndPattern(Cs.TypePattern(Cs.Int), Cs.NotPattern(Const(1)))).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Type, relational and logical patterns requires C# 9.");
        yield return new("pattern.type-test-old", () => Is(Cs.TypePattern(Cs.Int)).At(CsLanguageVersion.CSharp8), "class C\n{\n    void M()\n    {\n        object x = a is int;\n    }\n}\n");
        yield return new("pattern.parenthesized-old", () => Arm(Cs.ParenthesizedPattern(Const(1))).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Type, relational and logical patterns requires C# 9.");
        yield return new("pattern.discard-old", () => Is(Cs.DiscardPattern()).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Discard pattern requires C# 8.");
        yield return new("pattern.relational-operator", () => Is(Cs.RelationalPattern(CsOperator.Equal, Cs.Literal(1))), "invalid: GEN076 Relational patterns require <, <=, > or >=.");
        yield return new("pattern.relational-forms", () => Is(Cs.AndPattern(Cs.RelationalPattern(CsOperator.LessThanOrEqual, Cs.Literal(1)), Cs.OrPattern(Cs.RelationalPattern(CsOperator.GreaterThan, Cs.Literal('a')), Cs.RelationalPattern(CsOperator.GreaterThanOrEqual, Cs.Id("K"))))), "class C\n{\n    void M()\n    {\n        object x = a is <= 1 and (> 'a' or >= K);\n    }\n}\n");
        yield return new("pattern.relational-null", () => Is(Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(null))), "invalid: GEN076 A relational pattern requires a numeric or character constant.");
        yield return new("pattern.relational-true", () => Is(Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(true))), "invalid: GEN076 A relational pattern requires a numeric or character constant.");
        yield return new("pattern.relational-false", () => Is(Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(false))), "invalid: GEN076 A relational pattern requires a numeric or character constant.");
        yield return new("pattern.relational-string", () => Is(Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal("a"))), "invalid: GEN076 A relational pattern requires a numeric or character constant.");
        yield return new("pattern.relational-verbatim", () => Is(Cs.RelationalPattern(CsOperator.LessThan, Cs.RawExpression("@\"a\"") with { Kind = ExpressionKind.Literal })), "invalid: GEN076 A relational pattern requires a numeric or character constant.");
        yield return new("pattern.recursive-old", () => Is(P(PatternKind.Recursive) with { HasProperties = true }).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Recursive patterns requires C# 8.");
        yield return new("pattern.property-extended-old", () => Is(P(PatternKind.Recursive) with { HasProperties = true, Properties = new[] { P(PatternKind.Property) with { Name = "A.B", Patterns = new[] { Const(1) } } } }).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Extended property pattern requires C# 10.");
        yield return new("pattern.property-simple", () => Is(P(PatternKind.Recursive) with { HasProperties = true, Properties = new[] { P(PatternKind.Property) with { Name = "A", Patterns = new[] { Const(1) } } } }), "class C\n{\n    void M()\n    {\n        object x = a is { A: 1 };\n    }\n}\n");
        yield return new("pattern.list-old", () => Is(P(PatternKind.List) with { Patterns = new[] { Const(1), P(PatternKind.Slice) } }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 List patterns requires C# 11.");
        yield return new("pattern.slice-outside", () => Is(P(PatternKind.Slice)), "invalid: GEN076 Slice belongs only to a list pattern.");
        yield return new("pattern.slice-two", () => Is(P(PatternKind.List) with { Patterns = new[] { P(PatternKind.Slice) with { Patterns = new[] { Cs.DiscardPattern(), Cs.DiscardPattern() } } } }), "invalid: GEN076 Slice permits at most one nested pattern.");
        yield return new("pattern.slice-designated", () => Is(P(PatternKind.List) with { Patterns = new[] { P(PatternKind.Slice) with { Patterns = new[] { Cs.VarPattern("rest") } } }, Name = "all" }), "class C\n{\n    void M()\n    {\n        object x = a is [.. var rest] all;\n    }\n}\n");
        yield return new("pattern.list-two-slices", () => Is(P(PatternKind.List) with { Patterns = new[] { P(PatternKind.Slice), P(PatternKind.Slice) } }), "invalid: GEN076 List pattern permits only one slice.");
        yield return new("pattern.list-patterns-null", () => Is(P(PatternKind.List) with { Patterns = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.slice-patterns-null", () => Is(P(PatternKind.List) with { Patterns = new[] { P(PatternKind.Slice) with { Patterns = null! } } }), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.positional-on-constant", () => Is(Const(1) with { HasPositional = true }), "invalid: GEN065 Recursive clauses belong to recursive patterns.");
        yield return new("pattern.properties-on-constant", () => Is(Const(1) with { HasProperties = true }), "invalid: GEN065 Recursive clauses belong to recursive patterns.");
        yield return new("pattern.property-list-on-constant", () => Is(Const(1) with { Properties = new[] { P(PatternKind.Property) with { Name = "A", Patterns = new[] { Const(1) } } } }), "invalid: GEN065 Recursive clauses belong to recursive patterns.");
        yield return new("pattern.property-list-null-on-constant", () => Is(Const(1) with { Properties = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.recursive-empty", () => Is(P(PatternKind.Recursive)), "invalid: GEN076 Recursive pattern needs positional or property clauses.");
        yield return new("pattern.recursive-patterns-without-positional", () => Is(P(PatternKind.Recursive) with { HasProperties = true, Patterns = new[] { Const(1) } }), "invalid: GEN076 Recursive pattern needs positional or property clauses.");
        yield return new("pattern.recursive-patterns-null", () => Is(P(PatternKind.Recursive) with { HasProperties = true, Patterns = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.recursive-properties-absent", () => Is(P(PatternKind.Recursive) with { HasPositional = true, Properties = new[] { P(PatternKind.Property) with { Name = "A", Patterns = new[] { Const(1) } } } }), "invalid: GEN065 Property clause is absent.");
        yield return new("pattern.recursive-properties-null", () => Is(P(PatternKind.Recursive) with { HasPositional = true, Properties = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.recursive-full", () => Is(P(PatternKind.Recursive) with { Type = Cs.Type("R"), Name = "r", HasPositional = true, HasProperties = true, Patterns = new[] { Const(1), Cs.VarPattern("y") }, Properties = new[] { P(PatternKind.Property) with { Name = "A", Patterns = new[] { Cs.DiscardPattern() } } } }), "class C\n{\n    void M()\n    {\n        object x = a is R(1, var y) { A: _ } r;\n    }\n}\n");
        yield return new("pattern.property-clause-kind", () => Is(P(PatternKind.Recursive) with { HasProperties = true, Properties = new[] { Const(1) } }), "invalid: GEN076 Property clause requires named subpatterns.");
        yield return new("pattern.operand-count", () => Is(Cs.NotPattern(Const(1)) with { Patterns = new[] { Const(1), Const(2) } }), "invalid: GEN076 Pattern has an invalid number of operands.");
        yield return new("pattern.operand-count-constant", () => Is(Const(1) with { Patterns = new[] { Const(2) } }), "invalid: GEN076 Pattern has an invalid number of operands.");
        yield return new("pattern.patterns-null", () => Is(Const(1) with { Patterns = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("pattern.constant-not-constant", () => Is(Cs.ConstantPattern(Cs.Call(Cs.Id("F")))), "invalid: GEN077 A case value or relational bound must be a constant expression.");
        yield return new("pattern.constant-interpolated-old", () => Is(Cs.ConstantPattern(new CsExpression(ExpressionKind.InterpolatedString, "", new[] { new CsExpression(ExpressionKind.StringText, "a", Values<CsExpression>.Empty) }))).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Constant interpolated string requires C# 10.");
        yield return new("pattern.constant-forms", () => Is(Cs.ConstantPattern(new CsExpression(ExpressionKind.Conditional, "", new[] { Cs.Literal(true), Cs.Unary(CsOperator.Negate, Cs.Literal(1)), Cs.Binary(CsOperator.Add, Cs.Id("A").Member("B"), E(ExpressionKind.Checked, E(ExpressionKind.SizeOf) with { Type = Cs.Int })) }))), "class C\n{\n    void M()\n    {\n        object x = a is (true ? -1 : A.B + checked(sizeof(int)));\n    }\n}\n");
        yield return new("pattern.constant-null-argument", () => Is(Cs.ConstantPattern(Cs.Binary(CsOperator.Add, Cs.Literal(1), Cs.Literal(2)) with { Arguments = new CsExpression[] { Cs.Literal(1), null! } })), "invalid: GEN002 Required node or collection is null. | GEN077 A case value or relational bound must be a constant expression.");
        yield return new("pattern.constant-null-arguments", () => Is(Cs.ConstantPattern(Cs.Literal(1) with { Arguments = null! })), "invalid: GEN002 Required node or collection is null. | GEN077 A case value or relational bound must be a constant expression.");
        yield return new("pattern.constant-deep", () =>
        {
            CsExpression value = Cs.Literal(1);
            for (int i = 0; i < 140; i++) value = new CsExpression(ExpressionKind.Parenthesized, "", new[] { value });
            return Is(Cs.ConstantPattern(value));
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle. | GEN077 A case value or relational bound must be a constant expression.");
        yield return new("pattern.constant-chain", () => Is(Cs.ConstantPattern(Cs.Id("A").Member("B").Member("C"))), "class C\n{\n    void M()\n    {\n        object x = a is A.B.C;\n    }\n}\n");

        // Switch expressions and guards.
        yield return new("switch-expression.old", () => Arm(Const(1)).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Switch expression requires C# 8. | GEN073 Discard pattern requires C# 8.");
        yield return new("switch-expression.empty", () => Value(Cs.SwitchExpression(Id)), "invalid: GEN078 Switch expression requires at least one arm.");
        yield return new("switch-expression.arms-null", () => Value(Cs.SwitchExpression(Id) with { Arms = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("switch-expression.guard-literal", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Const(1), Cs.Literal(1), Cs.Literal(1)), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(0)))), "invalid: GEN077 A case guard must be Boolean.");
        yield return new("switch-expression.guard-true", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Const(1), Cs.Literal(1), Cs.Literal(true)), Cs.Arm(Const(1), Cs.Literal(2)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.guard-false", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Const(1), Cs.Literal(1), Cs.Literal(false)), Cs.Arm(Const(1), Cs.Literal(2)))), "class C\n{\n    void M()\n    {\n        object x = a switch { 1 when false => 1, 1 => 2 };\n    }\n}\n");
        yield return new("switch-expression.guarded-repeat", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Const(1), Cs.Literal(1), Cs.Id("ready")), Cs.Arm(Const(1), Cs.Literal(2)))), "class C\n{\n    void M()\n    {\n        object x = a switch { 1 when ready => 1, 1 => 2 };\n    }\n}\n");
        yield return new("switch-expression.subsumed", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Cs.DiscardPattern(), Cs.Literal(0)), Cs.Arm(Const(1), Cs.Literal(1)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.subsumed-var", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Cs.VarPattern("x"), Cs.Literal(0)), Cs.Arm(Const(1), Cs.Literal(1)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.subsumed-parenthesized", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Cs.ParenthesizedPattern(Cs.DiscardPattern()), Cs.Literal(0)), Cs.Arm(Const(1), Cs.Literal(1)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.subsumed-or-left", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Cs.OrPattern(Cs.DiscardPattern(), Const(2)), Cs.Literal(0)), Cs.Arm(Const(1), Cs.Literal(1)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.subsumed-or-right", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Cs.OrPattern(Const(2), Cs.DiscardPattern()), Cs.Literal(0)), Cs.Arm(Const(1), Cs.Literal(1)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.or-not-catch-all", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Cs.OrPattern(Const(2), Const(3)), Cs.Literal(0)), Cs.Arm(Cs.ParenthesizedPattern(Const(4)), Cs.Literal(1)), Cs.Arm(Cs.DiscardPattern(), Cs.Literal(5)))), "class C\n{\n    void M()\n    {\n        object x = a switch { 2 or 3 => 0, (4) => 1, _ => 5 };\n    }\n}\n");
        yield return new("switch-expression.repeat-with-comments", () => Value(Cs.SwitchExpression(Id, Cs.Arm(Const(1), Cs.Literal(1)), Cs.Arm(Const(1) with { Comments = new[] { new CsComment("/*c*/", CommentKind.Block) } }, Cs.Literal(2)))), "invalid: GEN079 This pattern is subsumed by a preceding unguarded pattern.");
        yield return new("switch-expression.parsed", () => Code("var x = o switch { int n when n > 0 => n, string { Length: > 1 } s => s.Length, [1, .., var last] => last, null => 0, _ => throw null };"), "class C\n{\n    void M(object o, int[] a)\n    {\n        var x = o switch\n        {\n            int n when n > 0 => n,\n            string { Length: > 1 } s => s.Length,\n            [1, .., var last] => last,\n            null => 0,\n            _ => throw null\n        };\n    }\n}\n");
    }
}
