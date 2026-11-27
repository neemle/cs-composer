using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Operation payloads, lambdas and query expressions.
public static class OperationScenarios
{
    static CsExpression Id => Cs.Id("a");

    static CsExpression Text(string text) => new(ExpressionKind.StringText, text, Values<CsExpression>.Empty);

    static CsExpression Interpolated(params CsExpression[] parts) => new(ExpressionKind.InterpolatedString, "", parts);

    static CsExpression Arg(CsExpression value, ArgumentModifier modifier = ArgumentModifier.None, string name = "") => new(ExpressionKind.Argument, name, new[] { value }) { ArgumentModifier = modifier };

    static CsExpression Call(params CsExpression[] arguments) => E(ExpressionKind.Invocation, new[] { Cs.Id("F") }.Concat(arguments).ToArray());

    static CsExpression Lambda(params CsLambdaParameter[] parameters) => Cs.Lambda(Cs.Literal(1), parameters);

    static CsLambdaParameter L(string name, CsType? type = null, string? modifier = null) => new(name, type, modifier);

    static CsDocument Statements(string statements, CsLanguageVersion version = CsLanguageVersion.CSharp15) =>
        Src("class C { void M(object o, int[] a, int n) { " + statements + " } }", version);

    static CsQueryClause Q(QueryClauseKind kind) => new(kind);

    static CsQueryClause From => Q(QueryClauseKind.From) with { Name = "x", Value = Cs.Id("xs") };

    static CsQueryClause Select => Q(QueryClauseKind.Select) with { Value = Cs.Id("x") };

    static CsDocument Query(params CsQueryClause[] clauses) => Value(E(ExpressionKind.Query) with { QueryClauses = clauses });

    public static IEnumerable<Scenario> All()
    {
        // Operation payloads.
        yield return new("operation.unsafe-expression", () => Value(E(ExpressionKind.Unsafe, Id)), "invalid: GEN073 Unsafe expression requires C# preview.");
        yield return new("operation.nameof-unbound-old", () => Value(E(ExpressionKind.NameOf, E(ExpressionKind.TypeReference) with { Type = Cs.Type("List").Unbound() })).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Unbound generic nameof requires C# 14.");
        yield return new("operation.nameof-unbound-nested-old", () => Value(E(ExpressionKind.NameOf, E(ExpressionKind.TypeReference) with { Type = Cs.Type("List").Unbound().Array() })).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Unbound generic nameof requires C# 14.");
        yield return new("operation.nameof-unbound-argument-old", () => Value(E(ExpressionKind.NameOf, E(ExpressionKind.TypeReference) with { Type = Cs.Type("Dictionary").Generic(Cs.Int, Cs.Type("List").Unbound()) })).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Unbound generic nameof requires C# 14.");
        yield return new("operation.nameof-bound", () => Value(E(ExpressionKind.NameOf, E(ExpressionKind.TypeReference) with { Type = Cs.Type("List").Generic(Cs.Int) })).At(CsLanguageVersion.CSharp13), "class C\n{\n    void M()\n    {\n        object x = nameof(List<int>);\n    }\n}\n");
        yield return new("operation.nameof-null-argument", () => Value(E(ExpressionKind.NameOf, E(ExpressionKind.TypeReference) with { Type = Cs.Type("Dictionary").Generic(Cs.Int) with { Arguments = new CsType[] { null! } } })), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.nameof-identifier", () => Value(E(ExpressionKind.NameOf, Id)), "class C\n{\n    void M()\n    {\n        object x = nameof(a);\n    }\n}\n");
        yield return new("operation.unknown-string-style", () => Value(Cs.Literal("x") with { StringStyle = (CsStringStyle)9 }), "invalid: GEN092 Unknown string style. | GEN065 String payload belongs to another expression kind.");
        yield return new("operation.string-style-on-identifier", () => Value(Id with { StringStyle = CsStringStyle.Verbatim }), "invalid: GEN065 String payload belongs to another expression kind.");
        yield return new("operation.utf8-on-identifier", () => Value(Id with { Utf8 = true }), "invalid: GEN065 String payload belongs to another expression kind.");
        yield return new("operation.format-on-identifier", () => Value(Id with { Format = "x" }), "invalid: GEN065 String payload belongs to another expression kind.");
        yield return new("operation.alignment-on-identifier", () => Value(Id with { Alignment = Cs.Literal(1) }), "invalid: GEN065 String payload belongs to another expression kind.");
        yield return new("operation.raw-string-old", () => Value(new CsExpression(ExpressionKind.StringLiteral, "x", Values<CsExpression>.Empty) { StringStyle = CsStringStyle.Raw }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Raw or UTF-8 string requires C# 11.");
        yield return new("operation.utf8-old", () => Value(new CsExpression(ExpressionKind.StringLiteral, "x", Values<CsExpression>.Empty) { Utf8 = true }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Raw or UTF-8 string requires C# 11.");
        yield return new("operation.literal-u8-old", () => Value(Cs.RawExpression("\"x\"u8") with { Kind = ExpressionKind.Literal }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Raw or UTF-8 string requires C# 11.");
        yield return new("operation.literal-raw-old", () => Value(Cs.RawExpression("\"\"\"x\"\"\"") with { Kind = ExpressionKind.Literal }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Raw or UTF-8 string requires C# 11.");
        yield return new("operation.interpolated-part", () => Value(Interpolated(Text("a"), Id)), "invalid: GEN092 Interpolated string requires text/interpolation parts.");
        yield return new("operation.interpolated-null-part", () => Value(Interpolated(Text("a"), null!)), "invalid: GEN092 Interpolated string requires text/interpolation parts. | GEN002 Required node or collection is null.");
        yield return new("operation.interpolated", () => Value(Interpolated(Text("a"), new CsExpression(ExpressionKind.Interpolation, "", new[] { Id }) { Alignment = Cs.Literal(3), Format = "x2" })), "class C\n{\n    void M()\n    {\n        object x = $\"a{a, 3:x2}\";\n    }\n}\n");
        yield return new("operation.string-text-alone", () => Value(Text("a")), "invalid: GEN092 String part belongs to an interpolated string.");
        yield return new("operation.interpolation-alone", () => Value(new CsExpression(ExpressionKind.Interpolation, "", new[] { Id })), "invalid: GEN092 String part belongs to an interpolated string.");
        yield return new("operation.pointer-on-identifier", () => Value(Id with { PointerAccess = true }), "invalid: GEN065 Pointer/alias access belongs to a named member with one access mode.");
        yield return new("operation.alias-on-identifier", () => Value(Id with { AliasAccess = true }), "invalid: GEN065 Pointer/alias access belongs to a named member with one access mode.");
        yield return new("operation.pointer-conditional", () => Value(Id.Member("b") with { PointerAccess = true, ConditionalAccess = true }), "invalid: GEN065 Pointer/alias access belongs to a named member with one access mode.");
        yield return new("operation.pointer-and-alias", () => Value(Id.Member("b") with { PointerAccess = true, AliasAccess = true }), "invalid: GEN065 Pointer/alias access belongs to a named member with one access mode.");
        yield return new("operation.pointer-member", () => Src("unsafe class C { int M(P* p) => p->X; }"), "unsafe class C { int M(P* p) => p->X; }");
        yield return new("operation.alias-receiver", () => Value(Cs.Call(Id).Member("b") with { AliasAccess = true }), "invalid: GEN092 Alias qualifier requires a simple alias name.");
        yield return new("operation.alias-null-receiver", () => Value(Id.Member("b") with { AliasAccess = true, Arguments = new CsExpression[] { null! } }), "invalid: GEN092 Alias qualifier requires a simple alias name. | GEN002 Required node or collection is null.");
        yield return new("operation.alias-two-arguments", () => Value(Id.Member("b") with { AliasAccess = true, Arguments = new[] { Id, Id } }), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("operation.alias", () => Value(Cs.Id("global").Member("System") with { AliasAccess = true }), "class C\n{\n    void M()\n    {\n        object x = global::System;\n    }\n}\n");
        yield return new("operation.field-old", () => InClass(new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get") { Body = CsBody.FromExpression(E(ExpressionKind.Field)) } } }).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Field-backed property requires C# 14.");
        yield return new("operation.lambda-attributes-on-identifier", () => Value(Id with { LambdaAttributes = new[] { Cs.Attribute("A") } }), "invalid: GEN065 Function signature belongs to a function expression.");
        yield return new("operation.return-modifier-on-identifier", () => Value(Id with { ReturnModifier = "ref" }), "invalid: GEN065 Function signature belongs to a function expression.");
        yield return new("operation.no-parameter-list-on-identifier", () => Value(Id with { HasParameterList = false }), "invalid: GEN065 Function signature belongs to a function expression.");
        yield return new("operation.lambda-attributes-null", () => Value(Id with { LambdaAttributes = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.query-clauses-on-identifier", () => Value(Id with { QueryClauses = new[] { From, Select } }), "invalid: GEN065 Query clauses belong to a query expression.");
        yield return new("operation.query-clauses-null", () => Value(Id with { QueryClauses = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.unknown-argument-modifier", () => Value(Call(Arg(Id, (ArgumentModifier)9))), "invalid: GEN092 Unknown argument modifier.");
        yield return new("operation.argument-modifier-on-identifier", () => Value(Id with { ArgumentModifier = ArgumentModifier.Ref }), "invalid: GEN065 Argument modifier belongs to an argument expression.");
        yield return new("operation.type-arguments-on-call", () => Value(Cs.Call(Id) with { TypeArguments = new[] { Cs.Int } }), "invalid: GEN065 Generic type arguments require a name reference.");
        yield return new("operation.type-arguments-null", () => Value(Id with { TypeArguments = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.type-arguments", () => Value(Cs.Call(Id with { TypeArguments = new[] { Cs.Int } }, Cs.Id("b").Member("c") with { TypeArguments = new[] { Cs.Type("string") } })), "class C\n{\n    void M()\n    {\n        object x = a<int>(b.c<string>);\n    }\n}\n");
        yield return new("operation.as-no-type", () => Value(E(ExpressionKind.As, Id)), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.sizeof-no-type", () => Value(E(ExpressionKind.SizeOf)), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.tuple-one", () => Value(E(ExpressionKind.Tuple, Id)), "invalid: GEN092 Tuple needs at least two elements.");
        yield return new("operation.argument-named", () => Value(Call(Arg(Id, name: "x"), Arg(Id, ArgumentModifier.In))), "class C\n{\n    void M()\n    {\n        object x = F(x: a, in a);\n    }\n}\n");
        yield return new("operation.argument-bad-name", () => Value(Call(Arg(Id, name: "1x"))), "invalid: GEN003 Invalid identifier.");
        yield return new("operation.argument-alone", () => Value(Arg(Id)), "invalid: GEN092 Argument requires an invocation, construction, index or tuple.");
        yield return new("operation.argument-in-new", () => Value(E(ExpressionKind.New, Arg(Id)) with { Type = Cs.Type("C") }), "class C\n{\n    void M()\n    {\n        object x = new C(a);\n    }\n}\n");
        yield return new("operation.argument-in-index", () => Value(E(ExpressionKind.Index, Id, Arg(Id))), "class C\n{\n    void M()\n    {\n        object x = a[a];\n    }\n}\n");
        yield return new("operation.argument-in-tuple", () => Value(E(ExpressionKind.Tuple, Arg(Id, name: "x"), Arg(Id))), "class C\n{\n    void M()\n    {\n        object x = (x: a, a);\n    }\n}\n");
        yield return new("operation.argument-modifier-in-tuple", () => Value(E(ExpressionKind.Tuple, Arg(Id, ArgumentModifier.Ref), Arg(Id))), "invalid: GEN092 Tuple elements cannot have argument modifiers.");
        yield return new("operation.omitted-alone", () => Value(E(ExpressionKind.Omitted)), "invalid: GEN092 Omitted endpoint belongs only to a range.");
        yield return new("operation.range", () => Value(E(ExpressionKind.Range, E(ExpressionKind.Omitted), Cs.Unary(CsOperator.FromEnd, Cs.Literal(1)))), "class C\n{\n    void M()\n    {\n        object x = ..^1;\n    }\n}\n");
        yield return new("operation.range-old", () => Value(E(ExpressionKind.Range, Cs.Literal(0), Cs.Literal(1))).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Indices and ranges requires C# 8.");
        yield return new("operation.from-end-old", () => Value(E(ExpressionKind.Index, Id, Cs.Unary(CsOperator.FromEnd, Cs.Literal(1)))).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Indices and ranges requires C# 8.");
        yield return new("operation.collection-arguments", () => Value(E(ExpressionKind.Collection, E(ExpressionKind.CollectionArguments, Arg(Cs.Literal(4), name: "capacity")), Cs.Literal(1))), "class C\n{\n    void M()\n    {\n        object x = [with(capacity: 4), 1];\n    }\n}\n");
        yield return new("operation.collection-arguments-old", () => Value(E(ExpressionKind.Collection, E(ExpressionKind.CollectionArguments), Cs.Literal(1))).At(CsLanguageVersion.CSharp14), "invalid: GEN073 Collection expression arguments requires C# 15.");
        yield return new("operation.collection-arguments-alone", () => Value(E(ExpressionKind.CollectionArguments)), "invalid: GEN092 Collection arguments belong to a collection expression.");
        yield return new("operation.collection-arguments-second", () => Value(E(ExpressionKind.Collection, Cs.Literal(1), E(ExpressionKind.CollectionArguments))), "invalid: GEN092 Collection arguments must be the first element.");
        yield return new("operation.collection-null-element", () => Value(E(ExpressionKind.Collection, Cs.Literal(1), null!)), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.spread-alone", () => Value(E(ExpressionKind.Spread, Id)), "invalid: GEN092 Spread belongs only to a collection expression.");
        yield return new("operation.collection-old", () => Value(E(ExpressionKind.Collection, E(ExpressionKind.Spread, Id))).At(CsLanguageVersion.CSharp11), "invalid: GEN073 Collection expression requires C# 12.");
        yield return new("operation.implicit-index-alone", () => Value(E(ExpressionKind.ImplicitIndex, Cs.Literal(0))), "invalid: GEN092 Implicit receiver index requires an initializer.");
        yield return new("operation.implicit-index", () => Value(E(ExpressionKind.New) with { Type = Cs.Type("C"), Initializer = E(ExpressionKind.Initializer, Cs.Assign(E(ExpressionKind.ImplicitIndex, Cs.Literal(0)), Cs.Literal(1))) }), "class C\n{\n    void M()\n    {\n        object x = new C() { [0] = 1 };\n    }\n}\n");
        yield return new("operation.declaration-named", () => Value(Call(Arg(new CsExpression(ExpressionKind.Declaration, "x", Values<CsExpression>.Empty) { Type = Cs.Int }, ArgumentModifier.Out))), "class C\n{\n    void M()\n    {\n        object x = F(out int x);\n    }\n}\n");
        yield return new("operation.declaration-bad-name", () => Value(Call(Arg(new CsExpression(ExpressionKind.Declaration, "1x", Values<CsExpression>.Empty) { Type = Cs.Int }, ArgumentModifier.Out))), "invalid: GEN003 Invalid identifier.");
        yield return new("operation.declaration-named-operands", () => Value(Call(Arg(new CsExpression(ExpressionKind.Declaration, "x", new[] { Id }) { Type = Cs.Int }, ArgumentModifier.Out))), "invalid: GEN013 Named declaration has no designation operands.");
        yield return new("operation.declaration-var-typed", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Declaration, Cs.Id("x"), Cs.Id("y")) with { Type = Cs.Int }, Id))), "invalid: GEN092 Var deconstruction requires a tuple designation.");
        yield return new("operation.declaration-var-one", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Declaration, Cs.Id("x")), Id))), "invalid: GEN092 Var deconstruction requires a tuple designation.");
        yield return new("operation.declaration-var-literal", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Declaration, Cs.Id("x"), Cs.Literal(1)), Id))), "invalid: GEN092 Var deconstruction requires a tuple designation.");
        yield return new("operation.declaration-var-null", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Declaration, Cs.Id("x"), null!), Id))), "invalid: GEN092 Var deconstruction requires a tuple designation. | GEN002 Required node or collection is null.");
        yield return new("operation.declaration-var", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Declaration, Cs.Id("x"), E(ExpressionKind.Tuple, Cs.Id("y"), Cs.Id("z"))), Id))), "class C\n{\n    void M()\n    {\n        var (x, (y, z)) = a;\n    }\n}\n");
        yield return new("operation.declaration-alone", () => Value(new CsExpression(ExpressionKind.Declaration, "x", Values<CsExpression>.Empty) { Type = Cs.Int }), "invalid: GEN092 Declaration operand requires an out argument or deconstruction.");
        yield return new("operation.declaration-in-tuple", () => InMethod(Cs.Statement(Cs.Assign(E(ExpressionKind.Tuple, new CsExpression(ExpressionKind.Declaration, "x", Values<CsExpression>.Empty) { Type = Cs.Int }, Cs.Id("y")), Id))), "class C\n{\n    void M()\n    {\n        (int x, y) = a;\n    }\n}\n");
        yield return new("operation.stackalloc-two-sizes", () => InMethod(Cs.Var("s", E(ExpressionKind.StackAlloc, Cs.Literal(1), Cs.Literal(2)) with { Type = Cs.Int })), "invalid: GEN092 Stack allocation requires a size or initializer.");
        yield return new("operation.stackalloc-nothing", () => InMethod(Cs.Var("s", E(ExpressionKind.StackAlloc) with { Type = Cs.Int })), "invalid: GEN092 Stack allocation requires a size or initializer.");
        yield return new("operation.stackalloc-untyped-size", () => InMethod(Cs.Var("s", E(ExpressionKind.StackAlloc, Cs.Literal(1)))), "invalid: GEN092 Stack allocation requires a size or initializer.");
        yield return new("operation.stackalloc-size", () => InMethod(Cs.Var("s", E(ExpressionKind.StackAlloc, Cs.Literal(1)) with { Type = Cs.Int })), "class C\n{\n    void M()\n    {\n        var s = stackalloc int[1];\n    }\n}\n");
        yield return new("operation.stackalloc-untyped", () => InMethod(Cs.Var("s", E(ExpressionKind.StackAlloc) with { Initializer = E(ExpressionKind.Initializer, Cs.Literal(1)) })), "class C\n{\n    void M()\n    {\n        var s = stackalloc [] { 1 };\n    }\n}\n");
        yield return new("operation.with-no-initializer", () => Value(E(ExpressionKind.With, Id)), "invalid: GEN002 Required node or collection is null.");
        yield return new("operation.with-old", () => Value(E(ExpressionKind.With, Id) with { Initializer = E(ExpressionKind.Initializer) }).At(CsLanguageVersion.CSharp8), "invalid: GEN073 With expression requires C# 9.");
        yield return new("operation.null-forgiving-old", () => Value(E(ExpressionKind.NullForgiving, Id)).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Null-forgiving operator requires C# 8.");
        yield return new("operation.unsigned-shift-old", () => Value(Cs.Binary(CsOperator.UnsignedRightShift, Id, Cs.Literal(1))).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Unsigned right shift requires C# 11.");
        yield return new("operation.parsed-operators", () => Statements("var b = a[^1..]; var c = o!; var d = n >>> 1; var e = (n, n); var f = sizeof(int); var g = o as string; var h = $\"{n,4:x}\"; F(out var p, ref n, in n, name: n);"), "class C { void M(object o, int[] a, int n) { var b = a[^1..]; var c = o!; var d = n >>> 1; var e = (n, n); var f = sizeof(int); var g = o as string; var h = $\"{n,4:x}\"; F(out var p, ref n, in n, name: n); } }");

        // Lambdas and anonymous methods.
        yield return new("lambda.static-old", () => Value(Lambda() with { IsStatic = true }).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Static lambda requires C# 9.");
        yield return new("lambda.return-type-old", () => Value(Lambda() with { Type = Cs.Int }).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Explicit lambda return types and attributes requires C# 10.");
        yield return new("lambda.attributes-old", () => Value(Lambda() with { LambdaAttributes = new[] { Cs.Attribute("A") } }).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Explicit lambda return types and attributes requires C# 10.");
        yield return new("lambda.attributes-null", () => Value(Lambda() with { LambdaAttributes = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("lambda.return-modifier-untyped", () => Value(Lambda() with { ReturnModifier = "ref" }), "invalid: GEN089 Invalid lambda return modifier.");
        yield return new("lambda.return-modifier-bad", () => Value(Lambda() with { ReturnModifier = "out", Type = Cs.Int }), "invalid: GEN089 Invalid lambda return modifier.");
        yield return new("lambda.return-modifier-async", () => Value(Lambda() with { ReturnModifier = "ref readonly", Type = Cs.Int, IsAsync = true }), "invalid: GEN089 Invalid lambda return modifier.");
        yield return new("lambda.return-modifier", () => Value(Cs.Lambda(Cs.Id("x"), L("x", Cs.Int.Ref())) with { ReturnModifier = "ref", Type = Cs.Int }), "class C\n{\n    void M()\n    {\n        object x = ref int (ref int x) => x;\n    }\n}\n");
        yield return new("lambda.anonymous-static", () => Value(Cs.Lambda(Cs.Body()) with { Kind = ExpressionKind.AnonymousMethod, IsStatic = true }), "invalid: GEN089 Invalid anonymous method signature.");
        yield return new("lambda.anonymous-typed", () => Value(Cs.Lambda(Cs.Body()) with { Kind = ExpressionKind.AnonymousMethod, Type = Cs.Int }), "invalid: GEN065 Type payload does not belong to this expression kind. | GEN089 Invalid anonymous method signature.");
        yield return new("lambda.anonymous-return-modifier", () => Value(Cs.Lambda(Cs.Body()) with { Kind = ExpressionKind.AnonymousMethod, ReturnModifier = "ref" }), "invalid: GEN089 Invalid lambda return modifier. | GEN089 Invalid anonymous method signature.");
        yield return new("lambda.anonymous-attributes", () => Value(Cs.Lambda(Cs.Body()) with { Kind = ExpressionKind.AnonymousMethod, LambdaAttributes = new[] { Cs.Attribute("A") } }), "invalid: GEN089 Invalid anonymous method signature.");
        yield return new("lambda.anonymous-expression", () => Value(Lambda() with { Kind = ExpressionKind.AnonymousMethod }), "invalid: GEN089 Invalid anonymous method signature.");
        yield return new("lambda.anonymous-body-null", () => Value(Lambda() with { Kind = ExpressionKind.AnonymousMethod, LambdaBody = null }), "invalid: GEN002 Required node or collection is null.");
        yield return new("lambda.anonymous", () => Value(Cs.Lambda(Cs.Body(Cs.Return())) with { Kind = ExpressionKind.AnonymousMethod, HasParameterList = false }), "class C\n{\n    void M()\n    {\n        object x = delegate\n        {\n            return;\n        };\n    }\n}\n");
        yield return new("lambda.no-parameter-list", () => Value(Lambda() with { HasParameterList = false }), "invalid: GEN089 Only an anonymous method can omit its parameter list.");
        yield return new("lambda.anonymous-no-list-parameters", () => Value(Cs.Lambda(Cs.Body(), L("x", Cs.Int)) with { Kind = ExpressionKind.AnonymousMethod, HasParameterList = false }), "invalid: GEN089 Only an anonymous method can omit its parameter list.");
        yield return new("lambda.discards", () => Value(Lambda(L("_"), L("_"))), "class C\n{\n    void M()\n    {\n        object x = (_, _) => 1;\n    }\n}\n");
        yield return new("lambda.discards-old", () => Value(Lambda(L("_"), L("_"))).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Lambda discard parameters requires C# 9.");
        yield return new("lambda.discard-and-verbatim", () => Value(Lambda(L("_"), L("_"), L("@_"))), "invalid: GEN089 Duplicate lambda parameter.");
        yield return new("lambda.duplicate", () => Value(Lambda(L("x"), L("x"))), "invalid: GEN089 Duplicate lambda parameter.");
        yield return new("lambda.null-name", () => Value(Lambda(L(null!))), "invalid: GEN003 Invalid identifier.");
        yield return new("lambda.null-parameter", () => Value(Lambda(L("_"), null!, L("_"))), "invalid: GEN002 Required node or collection is null.");
        yield return new("lambda.mixed-typing", () => Value(Lambda(L("x", Cs.Int), L("y"))), "invalid: GEN089 Lambda parameters must be all explicit or all inferred.");
        yield return new("lambda.void-parameter", () => Value(Lambda(L("x", Cs.Void))), "invalid: GEN089 Lambda parameters cannot have type void.");
        yield return new("lambda.parameter-attributes-old", () => Value(Lambda(L("x", Cs.Int) with { Attributes = new[] { Cs.Attribute("A") } })).At(CsLanguageVersion.CSharp9), "invalid: GEN073 Lambda parameter attributes requires C# 10.");
        yield return new("lambda.parameter-attributes-null", () => Value(Lambda(L("x", Cs.Int) with { Attributes = null! })), "invalid: GEN002 Required node or collection is null.");
        yield return new("lambda.modifier-ref", () => Value(Lambda(L("x", Cs.Int.Array(), "ref"))), "class C\n{\n    void M()\n    {\n        object x = (ref int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-out", () => Value(Lambda(L("x", Cs.Int.Array(), "out"))), "class C\n{\n    void M()\n    {\n        object x = (out int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-in", () => Value(Lambda(L("x", Cs.Int.Array(), "in"))), "class C\n{\n    void M()\n    {\n        object x = (in int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-ref-readonly", () => Value(Lambda(L("x", Cs.Int.Array(), "ref readonly"))), "class C\n{\n    void M()\n    {\n        object x = (ref readonly int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-params", () => Value(Lambda(L("x", Cs.Int.Array(), "params"))), "class C\n{\n    void M()\n    {\n        object x = (params int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-scoped-ref", () => Value(Lambda(L("x", Cs.Int.Array(), "scoped ref"))), "class C\n{\n    void M()\n    {\n        object x = (scoped ref int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-scoped-in", () => Value(Lambda(L("x", Cs.Int.Array(), "scoped in"))), "class C\n{\n    void M()\n    {\n        object x = (scoped in int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.modifier-this", () => Value(Lambda(L("x", Cs.Int.Array(), "this"))), "invalid: GEN089 Invalid lambda parameter modifier.");
        yield return new("lambda.async-ref", () => Value(Lambda(L("x", Cs.Int, "ref")) with { IsAsync = true }), "invalid: GEN089 Invalid lambda parameter modifier.");
        yield return new("lambda.async-params", () => Value(Lambda(L("x", Cs.Int.Array(), "params")) with { IsAsync = true }), "class C\n{\n    void M()\n    {\n        object x = async (params int[] x) => 1;\n    }\n}\n");
        yield return new("lambda.implicit-modifier-old", () => Value(Lambda(L("x", null, "ref"))).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Implicitly typed lambda parameter modifiers requires C# 14.");
        yield return new("lambda.params-old", () => Value(Lambda(L("x", Cs.Int.Array(), "params"))).At(CsLanguageVersion.CSharp11), "invalid: GEN073 Lambda parameter collection requires C# 12.");
        yield return new("lambda.ref-readonly-old", () => Value(Lambda(L("x", Cs.Int, "ref readonly"))).At(CsLanguageVersion.CSharp11), "invalid: GEN073 Ref readonly parameters requires C# 12.");
        yield return new("lambda.default-old", () => Value(Lambda(L("x", Cs.Int) with { Default = Cs.Literal(1) })).At(CsLanguageVersion.CSharp11), "invalid: GEN073 Lambda default parameters requires C# 12.");
        yield return new("lambda.default-with-modifier", () => Value(Lambda(L("x", Cs.Int, "in") with { Default = Cs.Literal(1) })), "invalid: GEN089 Default value cannot accompany a parameter modifier.");
        yield return new("lambda.required-after-optional", () => Value(Lambda(L("x", Cs.Int) with { Default = Cs.Literal(1) }, L("y", Cs.Int))), "invalid: GEN089 Required parameter follows an optional parameter.");
        yield return new("lambda.params-after-optional", () => Value(Lambda(L("x", Cs.Int) with { Default = Cs.Literal(1) }, L("y", Cs.Int.Array(), "params"))), "class C\n{\n    void M()\n    {\n        object x = (int x = 1, params int[] y) => 1;\n    }\n}\n");
        yield return new("lambda.parsed", () => Statements("System.Func<int, int> f = static (int x) => x; var g = [A] int ([A] int y = 2) => y; System.Action h = delegate { }; System.Action<int> i = async delegate (int z) { await System.Threading.Tasks.Task.Yield(); };"), "class C { void M(object o, int[] a, int n) { System.Func<int, int> f = static (int x) => x; var g = [A] int ([A] int y = 2) => y; System.Action h = delegate { }; System.Action<int> i = async delegate (int z) { await System.Threading.Tasks.Task.Yield(); }; } }");

        // Query expressions.
        yield return new("query.full", () => Statements("var q = from x in a from int y in a let z = x * y join w in a on x equals w into g where z > 0 orderby x descending, y group x by y into h select h;"), "class C { void M(object o, int[] a, int n) { var q = from x in a from int y in a let z = x * y join w in a on x equals w into g where z > 0 orderby x descending, y group x by y into h select h; } }");
        yield return new("query.clauses-null", () => Value(E(ExpressionKind.Query) with { QueryClauses = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("query.empty", () => Query(), "invalid: GEN092 Query requires an initial from clause and a terminal select/group. | GEN092 Query requires a terminal select or group clause.");
        yield return new("query.no-from", () => Query(Select), "invalid: GEN092 Query requires an initial from clause and a terminal select/group.");
        yield return new("query.no-terminal", () => Query(From), "invalid: GEN092 Query requires a terminal select or group clause.");
        yield return new("query.after-terminal", () => Query(From, Select, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true) }), "invalid: GEN092 Query clauses are out of order.");
        yield return new("query.into-first", () => Query(From, Q(QueryClauseKind.Into) with { Name = "g", Clauses = new[] { Select } }, Select), "invalid: GEN092 Query clauses are out of order.");
        yield return new("query.after-continuation", () => Query(From, Select, Q(QueryClauseKind.Into) with { Name = "g", Clauses = new[] { Q(QueryClauseKind.Select) with { Value = Cs.Id("g") } } }, Q(QueryClauseKind.Select) with { Value = Cs.Id("g") }), "invalid: GEN092 Query clauses are out of order.");
        yield return new("query.null-clause", () => Query(From, null!, Select), "invalid: GEN002 Required node or collection is null.");
        yield return new("query.depth", () =>
        {
            var clause = Q(QueryClauseKind.Into) with { Name = "g", Clauses = new[] { Q(QueryClauseKind.Select) with { Value = Cs.Id("g") } } };
            for (int i = 0; i < 70; i++) clause = Q(QueryClauseKind.Into) with { Name = "g", Clauses = new[] { Q(QueryClauseKind.Select) with { Value = Cs.Id("g") }, clause } };
            return Query(From, Select, clause);
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        yield return new("query.unknown-clause", () => Query(From, Q((QueryClauseKind)99), Select), "invalid: GEN092 Unknown query clause. | GEN002 Required node or collection is null.");
        yield return new("query.name-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Name = "x", Value = Cs.Literal(true) }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.value-on-into", () => Query(From, Select, Q(QueryClauseKind.Into) with { Name = "g", Value = Id, Clauses = new[] { Select } }), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.key-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), Key = Id }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.type-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), Type = Cs.Int }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.into-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), Into = "g" }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.equals-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), EqualsKey = Id }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.descending-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), Descending = true }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.clauses-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), Clauses = new[] { Select } }, Select), "invalid: GEN065 Payload does not belong to this query clause.");
        yield return new("query.clauses-null-on-where", () => Query(From, Q(QueryClauseKind.Where) with { Value = Cs.Literal(true), Clauses = null! }, Select), "invalid: GEN002 Required node or collection is null.");
        yield return new("query.orderby-empty", () => Query(From, Q(QueryClauseKind.OrderBy), Select), "invalid: GEN092 Orderby requires an ordering.");
        yield return new("query.orderby-null", () => Query(From, Q(QueryClauseKind.OrderBy) with { Clauses = null! }, Select), "invalid: GEN002 Required node or collection is null.");
        yield return new("query.orderby-not-order", () => Query(From, Q(QueryClauseKind.OrderBy) with { Clauses = new[] { Select } }, Select), "invalid: GEN092 Expected an ordering.");
        yield return new("query.join-type", () => Query(From, Q(QueryClauseKind.Join) with { Name = "y", Type = Cs.Int, Value = Id, Key = Cs.Id("x"), EqualsKey = Cs.Id("y"), Into = "g" }, Select), "class C\n{\n    void M()\n    {\n        object x = from x in xs join int y in a on x equals y into g select x;\n    }\n}\n");
        yield return new("query.into-bad-name", () => Query(From, Q(QueryClauseKind.Join) with { Name = "y", Value = Id, Key = Cs.Id("x"), EqualsKey = Cs.Id("y"), Into = "1g" }, Select), "invalid: GEN003 Invalid identifier.");
    }
}
