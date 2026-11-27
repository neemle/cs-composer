using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Document, type, expression, parameter, generic, body and accessor rules of the validator.
public static class ValidatorScenarios
{
    static CsType Deep(int levels) { CsType type = Cs.Int; for (int i = 0; i < levels; i++) type = type.Array(); return type; }

    static CsMethod M(CsBody? body = null) => new(Cs.Void, "M") { Body = body ?? Cs.Body() };

    static CsDocument Generic(params TypeParameter[] parameters) => InClass(M() with { TypeParameters = parameters });

    static CsDocument Constraints(params CsConstraint[] constraints) => Generic(new TypeParameter("T") { Constraints = constraints });

    static CsDocument Parameters(params CsParameter[] parameters) => InClass(M() with { Parameters = parameters });

    static CsDocument Property(params CsAccessor[] accessors) => InClass(new CsProperty(Cs.Int, "P") { Accessors = accessors });

    static CsDocument Event(params CsAccessor[] accessors) => InClass(new CsEvent("System.Action", "E") { Accessors = accessors });

    static CsDocument Using(CsUsing use) => new() { Usings = new[] { use }, Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } };

    static CsExpression Id => Cs.Id("a");

    public static IEnumerable<Scenario> All()
    {
        // Document level.
        yield return new("validator.depth.type", () => Field(Deep(150)), "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        yield return new("validator.required.field-type", () => Field(null!), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.identifier.simple", () => InClass(new CsField(Cs.Int, "x; y")), "invalid: GEN003 Invalid identifier.");
        yield return new("validator.identifier.qualified", () => new CsDocument { Namespace = "a..b" }, "invalid: GEN003 Invalid qualified identifier.");
        yield return new("validator.document.namespace", () => new CsDocument { Namespace = "A.B", Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } }, "namespace A.B;\n\nclass C\n{\n}\n");
        yield return new("validator.document.items-null", () => new CsDocument { Items = null! }, "invalid: GEN002 Required child collection is null.");
        yield return new("validator.document.version-unknown", () => new CsDocument() with { LanguageVersion = (CsLanguageVersion)1 }, "invalid: GEN072 Unsupported C# language version. Supported: 7.3, 8, 9, 10, 11, 12, 13, 14, 15, preview.");
        yield return new("validator.document.nullable-context-old", () => new CsDocument(CsLanguageVersion.CSharp7_3) { NullableContext = NullableMode.Enable }, "invalid: GEN073 Nullable context requires C# 8.");
        yield return new("validator.document.nullable-context", () => new CsDocument { NullableContext = NullableMode.Enable, Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") } }, "#nullable enable\n\nclass C\n{\n}\n");
        yield return new("validator.document.global-after-type", () =>
        {
            var parsed = Src("System.Console.WriteLine(1);\nclass C { }\n");
            return parsed with { Items = new(parsed.Items.Reverse()) };
        }, "invalid: GEN092 Top-level statements must precede type/namespace declarations.");
        yield return new("validator.document.global-invalid-symbol", () => new CsDocument { Items = new CsNode[] { new CsGlobalStatement(new CsSymbol(null, new CsSymbolOptions())) } }, "invalid: SYM001 A symbol must be a reference, value, operation, declaration or compound.");
        yield return new("validator.document.graph-error-kept", () => new CsDocument { Items = new CsNode[] { new CsTypeDeclaration(TypeKind.Class, "C"), Cs.Int } }, "invalid: GEN075 Invalid document child kind.");
        yield return new("validator.document.duplicate-origin", () =>
        {
            var parsed = Src("class C { int F; }\n");
            var type = parsed.Types[0];
            return parsed with { Types = new[] { type, type with { Name = "D" } } };
        }, "invalid: GEN074 A parsed occurrence is owned twice; use Move or Detach before copying.");
        yield return new("validator.namespace.items-null", () => new CsDocument { Items = new CsNode[] { new CsNamespace("N", null!) } }, "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.namespace.items", () => Src("namespace N { extern alias A; using System; namespace M { class C { } } }"), "namespace N\n{\n    extern alias A;\n\n    using System;\n\n    namespace M\n    {\n        class C\n        {\n        }\n    }\n}\n");
        yield return new("validator.namespace.bad-alias", () => new CsDocument { Items = new CsNode[] { new CsNamespace("N", new CsNode[] { new CsExternAlias("1A") }) } }, "invalid: GEN003 Invalid identifier.");
        yield return new("validator.document.bad-alias", () => new CsDocument { Items = new CsNode[] { new CsExternAlias("1A") } }, "invalid: GEN003 Invalid identifier.");

        // Usings.
        yield return new("validator.using.target-named", () => Using(new CsUsing("X", Alias: "A") { Target = Cs.Int }), "invalid: GEN065 A type target requires an alias or static import and no name payload.");
        yield return new("validator.using.target-plain", () => Using(new CsUsing("") { Target = Cs.Int }), "invalid: GEN065 A type target requires an alias or static import and no name payload.");
        yield return new("validator.using.target-static", () => Using(new CsUsing("", IsStatic: true) { Target = Cs.Type("System.Math") }), "using static System.Math;\n\nclass C\n{\n}\n");
        yield return new("validator.using.target-alias-tuple", () => Using(new CsUsing("", Alias: "P") { Target = Cs.Tuple(new TupleElement(Cs.Int), new TupleElement(Cs.Int)) }).At(CsLanguageVersion.CSharp11), "invalid: GEN073 Using alias to any type requires C# 12.");
        yield return new("validator.using.unsafe-plain", () => Using(new CsUsing("System") { IsUnsafe = true }), "invalid: GEN065 Unsafe belongs to a static or alias using.");
        yield return new("validator.using.unsafe-static", () => Using(new CsUsing("", IsStatic: true) { Target = Cs.Type("System.Math"), IsUnsafe = true }), "using static unsafe System.Math;\n\nclass C\n{\n}\n");
        yield return new("validator.using.static-alias", () => Using(new CsUsing("System", Alias: "S", IsStatic: true)), "invalid: GEN005 Using aliases cannot be static.");
        yield return new("validator.using.usings-null", () => new CsDocument { Usings = null! }, "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.using.null-item", () => new CsDocument { Usings = new CsUsing[] { null! } }, "invalid: GEN075 Invalid document child kind. | GEN002 Required node is null.");

        // Attributes.
        yield return new("validator.attribute.target", () => new CsDocument { Attributes = new[] { Cs.Attribute("X") with { Target = "nowhere" } } }, "invalid: GEN006 Unknown attribute target.");
        yield return new("validator.attribute.named-argument", () => new CsDocument { Attributes = new[] { Cs.Attribute("X", Cs.Arg(Cs.Literal(1), "1bad")) with { Target = "assembly" } } }, "invalid: GEN003 Invalid identifier.");

        // Type shapes.
        yield return new("validator.type.named-payload-on-array", () => Typed(Cs.Int.Array() with { Name = "x" }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.element-on-named", () => Typed(Cs.Int with { Element = Cs.Int }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.arguments-on-named", () => Typed(Cs.Int with { Arguments = new[] { Cs.Int } }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.arguments-null-on-named", () => Typed(Cs.Int with { Arguments = null! }), "invalid: GEN002 Required child collection is null.");
        yield return new("validator.type.elements-on-named", () => Typed(Cs.Int with { Elements = new[] { new TupleElement(Cs.Int), new TupleElement(Cs.Int) } }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.elements-null-on-named", () => Typed(Cs.Int with { Elements = null! }), "invalid: GEN002 Required child collection is null.");
        yield return new("validator.type.rank-on-named", () => Typed(Cs.Int with { Rank = 2 }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.readonly-on-named", () => Typed(Cs.Int with { ReadOnly = true }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.convention-on-named", () => Typed(Cs.Int with { CallingConvention = FunctionPointerConvention.Managed }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.unmanaged-on-named", () => Typed(Cs.Int with { UnmanagedConventions = new[] { "Cdecl" } }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.unmanaged-null-on-named", () => Typed(Cs.Int with { UnmanagedConventions = null! }), "class C\n{\n    int F;\n}\n");
        yield return new("validator.type.parameters-on-named", () => Typed(Cs.Int with { FunctionParameters = new[] { new CsFunctionPointerParameter(Cs.Int) } }), "invalid: GEN065 Payload belongs to another type form.");
        yield return new("validator.type.parameters-null-on-named", () => Typed(Cs.Int with { FunctionParameters = null! }), "invalid: GEN002 Required child collection is null.");
        yield return new("validator.type.unbound-arity", () => Typed(Cs.Type("List").Unbound(0)), "invalid: GEN008 Unbound generic arity must be positive.");
        yield return new("validator.type.ref-ref", () => Typed(Cs.Int.Ref().Ref()), "invalid: GEN012 A ref type cannot contain another ref type.");
        yield return new("validator.type.ref-null-element", () => Typed(new CsType("") { Form = TypeForm.Ref }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.type.pointer-unknown-convention", () => Typed(FunctionPointer() with { CallingConvention = (FunctionPointerConvention)9 }), "invalid: GEN012 Unknown function pointer convention.");
        yield return new("validator.type.pointer-conventions-managed", () => Typed(FunctionPointer() with { CallingConvention = FunctionPointerConvention.Managed, UnmanagedConventions = new[] { "Cdecl" } }), "invalid: GEN012 Calling conventions require unmanaged.");
        yield return new("validator.type.pointer-conventions-unmanaged", () => Typed(FunctionPointer() with { CallingConvention = FunctionPointerConvention.Unmanaged, UnmanagedConventions = new[] { "Cdecl" } }), "class C\n{\n    delegate* unmanaged[Cdecl]<void> F;\n}\n");
        yield return new("validator.type.pointer-no-return", () => Typed(FunctionPointer() with { FunctionParameters = Values<CsFunctionPointerParameter>.Empty }), "invalid: GEN012 Function pointer requires a return type.");
        yield return new("validator.type.pointer-parameters-null", () => Typed(FunctionPointer() with { FunctionParameters = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.type.pointer-modifier", () => Typed(FunctionPointer() with { FunctionParameters = new[] { new CsFunctionPointerParameter(Cs.Int, "params"), new CsFunctionPointerParameter(Cs.Void) } }), "invalid: GEN016 Invalid function pointer parameter modifier.");
        yield return new("validator.type.pointer-modifiers-valid", () => Typed(FunctionPointer() with { FunctionParameters = new[] { new CsFunctionPointerParameter(Cs.Int, "ref"), new CsFunctionPointerParameter(Cs.Int, "ref readonly"), new CsFunctionPointerParameter(Cs.Void) } }), "class C\n{\n    delegate*<ref int, ref readonly int, void> F;\n}\n");
        yield return new("validator.type.pointer-conventions-null", () => Typed(FunctionPointer() with { UnmanagedConventions = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.type.raw", () => Typed(Cs.RawType("int")), "class C\n{\n    int F;\n}\n");
        yield return new("validator.type.raw-empty", () => Typed(Cs.RawType(" ")), "invalid: GEN007 Raw type source is empty.");
        yield return new("validator.type.nint-old", () => Typed(Cs.Type("nint")).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Native integer type requires C# 9.");
        yield return new("validator.type.nuint", () => Typed(Cs.Type("nuint")), "class C\n{\n    nuint F;\n}\n");
        yield return new("validator.type.parameter", () => Typed(Cs.TypeParameter("T")), "class C\n{\n    T F;\n}\n");
        yield return new("validator.type.parameter-bad", () => Typed(Cs.TypeParameter("1T")), "invalid: GEN003 Invalid identifier.");
        yield return new("validator.type.unknown-form", () => Typed(new CsType("X") { Form = (TypeForm)99 }), "invalid: GEN065 Payload belongs to another type form. | GEN012 Unknown type form.");
        yield return new("validator.type.generic-empty", () => Typed(Cs.Type("List").Generic()), "invalid: GEN008 Generic type needs at least one argument.");
        yield return new("validator.type.generic-null", () => Typed(Cs.Type("List").Generic(Cs.Int) with { Arguments = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.type.array-rank-zero", () => Typed(Cs.Int.Array(0)), "invalid: GEN009 Array rank must be between 1 and 32.");
        yield return new("validator.type.array-rank-high", () => Typed(Cs.Int.Array(33)), "invalid: GEN009 Array rank must be between 1 and 32.");
        yield return new("validator.type.nullable-nullable", () => Typed(new CsType("") { Form = TypeForm.Nullable, Element = Cs.Int.Nullable() }), "invalid: GEN010 Nullable type is already nullable.");
        yield return new("validator.type.nullable-null-element", () => Typed(new CsType("") { Form = TypeForm.Nullable }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.type.tuple-one", () => Typed(Cs.Tuple(new TupleElement(Cs.Int))), "invalid: GEN011 Tuple type needs at least two elements.");
        yield return new("validator.type.tuple-null", () => Typed(new CsType("") { Form = TypeForm.Tuple, Elements = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.type.tuple-names", () => Typed(Cs.Tuple(new TupleElement(Cs.Int, "a"), new TupleElement(Cs.Int, "1b"))), "invalid: GEN003 Invalid identifier.");

        // Expressions.
        yield return new("validator.expression.arguments-null", () => Init(Id with { Arguments = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.expression.text-null", () => Init(Id with { Text = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.expression.null-argument", () => Init(E(ExpressionKind.Invocation, Id) with { Arguments = new CsExpression[] { Id, null! } }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.expression.binary-unary-operator", () => Init(Cs.Binary(CsOperator.Not, Id, Id)), "invalid: GEN064 Operator is not supported for this expression kind.");
        yield return new("validator.expression.unary-binary-operator", () => Init(Cs.Unary(CsOperator.Multiply, Id)), "invalid: GEN064 Operator is not supported for this expression kind.");
        yield return new("validator.expression.assignment-operator", () => InMethod(Cs.Statement(Cs.Assign(Id, Id) with { Operator = CsOperator.Equal })), "invalid: GEN064 Operator is not supported for this expression kind.");
        yield return new("validator.expression.operator-on-identifier", () => Init(Id with { Operator = CsOperator.Add }), "invalid: GEN065 Payload does not belong to this node kind.");
        yield return new("validator.expression.binary-text", () => Init(Cs.Binary(CsOperator.Add, Id, Id) with { Text = "x" }), "invalid: GEN065 Payload does not belong to this node kind. | GEN065 This expression has no text payload.");
        yield return new("validator.expression.unary-text", () => Init(Cs.Unary(CsOperator.Not, Id) with { Text = "x" }), "invalid: GEN065 Payload does not belong to this node kind. | GEN065 This expression has no text payload.");
        yield return new("validator.expression.arms-null", () => Init(Id with { Arms = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.expression.arms-on-identifier", () => Init(Id with { Arms = new[] { new CsSwitchArm(new CsPattern(PatternKind.Discard), Id) } }), "invalid: GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.switch-text", () => Init(E(ExpressionKind.Switch, Id) with { Text = "x", Arms = new[] { new CsSwitchArm(new CsPattern(PatternKind.Discard), Id) } }), "invalid: GEN065 This expression has no text payload. | GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.throw-text", () => InClass(new CsMethod(Cs.Type("object"), "M") { Body = CsBody.FromExpression(E(ExpressionKind.Throw, Cs.Id("e")) with { Text = "x" }) }), "invalid: GEN065 This expression has no text payload. | GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.throw-not-allowed", () => Init(E(ExpressionKind.Throw, Cs.Id("e"))), "invalid: GEN083 Throw expression requires a coalescing/conditional branch, switch arm or expression-bodied function/member.");
        yield return new("validator.expression.lambda-parameters-null", () => Init(Id with { LambdaParameters = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.expression.lambda-body-on-identifier", () => Init(Id with { LambdaBody = Cs.Body() }), "invalid: GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.lambda-parameters-on-identifier", () => Init(Id with { LambdaParameters = new[] { new CsLambdaParameter("x") } }), "invalid: GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.async-identifier", () => Init(Id with { IsAsync = true }), "invalid: GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.static-identifier", () => Init(Id with { IsStatic = true }), "invalid: GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.lambda-text", () => Init(Cs.Lambda(Id) with { Text = "x" }), "invalid: GEN065 This expression has no text payload. | GEN065 Payload does not belong to this expression kind.");
        yield return new("validator.expression.mutation-text", () => InMethod(Cs.Statement(Cs.PostIncrement(Id) with { Text = "x" })), "invalid: GEN065 This expression has no text payload. | GEN065 Mutation expressions have no text payload.");
        yield return new("validator.expression.increment-literal", () => InMethod(Cs.Statement(Cs.PostIncrement(Cs.Literal(1)))), "invalid: GEN088 Increment and decrement require a writable target.");
        yield return new("validator.expression.increment-no-operand", () => InMethod(Cs.Statement(E(ExpressionKind.PostIncrement))), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.expression.assign-literal", () => InMethod(Cs.Statement(Cs.Assign(Cs.Literal(1), Id))), "invalid: GEN088 An assignment requires a writable target.");
        yield return new("validator.expression.unknown-kind", () => Init(E((ExpressionKind)999)), "invalid: GEN014 Unknown expression kind.");
        yield return new("validator.arity.invocation", () => Init(E(ExpressionKind.Invocation)), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.index", () => Init(E(ExpressionKind.Index, Id)), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.conditional", () => Init(E(ExpressionKind.Conditional, Id, Id)), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.literal", () => Init(Cs.Literal(1) with { Arguments = new[] { Id } }), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.binary", () => Init(Cs.Binary(CsOperator.Add, Id, Id) with { Arguments = new[] { Id } }), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.range", () => Init(E(ExpressionKind.Range, Id)), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.await", () => Init(E(ExpressionKind.Await)), "invalid: GEN013 Expression has an invalid number of operands.");
        yield return new("validator.arity.default-ok", () => Init(E(ExpressionKind.Default)), "class C\n{\n    object F = default;\n}\n");

        // Parameters.
        yield return new("validator.parameter.arglist-not-last", () => Parameters(new CsParameter(null, "") { IsArgList = true }, new CsParameter(Cs.Int, "x")), "invalid: GEN016 Arglist must be last.");
        yield return new("validator.parameter.arglist-last", () => Parameters(new CsParameter(Cs.Int, "x"), new CsParameter(null, "") { IsArgList = true }), "class C\n{\n    void M(int x, __arglist)\n    {\n    }\n}\n");
        yield return new("validator.parameter.arglist-type", () => Parameters(new CsParameter(Cs.Int, "") { IsArgList = true }), "invalid: GEN065 Arglist has no parameter payload.");
        yield return new("validator.parameter.arglist-name", () => Parameters(new CsParameter(null, "x") { IsArgList = true }), "invalid: GEN065 Arglist has no parameter payload.");
        yield return new("validator.parameter.arglist-modifier", () => Parameters(new CsParameter(null, "") { IsArgList = true, Modifier = "ref" }), "invalid: GEN065 Arglist has no parameter payload.");
        yield return new("validator.parameter.arglist-default", () => Parameters(new CsParameter(null, "") { IsArgList = true, Default = Cs.Literal(1) }), "invalid: GEN065 Arglist has no parameter payload.");
        yield return new("validator.parameter.arglist-attributes", () => Parameters(new CsParameter(null, "") { IsArgList = true, Attributes = new[] { Cs.Attribute("A") } }), "invalid: GEN065 Arglist has no parameter payload.");
        yield return new("validator.parameter.arglist-attributes-null", () => Parameters(new CsParameter(null, "") { IsArgList = true, Attributes = null! }), "invalid: GEN002 Required child collection is null.");
        yield return new("validator.parameter.duplicate", () => Parameters(new CsParameter(Cs.Int, "x"), new CsParameter(Cs.Int, "@x")), "invalid: GEN015 Duplicate parameter name.");
        yield return new("validator.parameter.modifier", () => Parameters(new CsParameter(Cs.Int, "x") { Modifier = "const" }), "invalid: GEN016 Invalid parameter modifier.");
        yield return new("validator.parameter.default", () => Parameters(new CsParameter(Cs.Int, "x") { Modifier = "in", Default = Cs.Literal(1), Attributes = new[] { Cs.Attribute("A") } }), "class C\n{\n    void M([A] in int x = 1)\n    {\n    }\n}\n");

        // Generic parameters and constraints.
        yield return new("validator.generic.duplicate", () => Generic(new TypeParameter("T"), new TypeParameter("T")), "invalid: GEN017 Duplicate generic parameter.");
        yield return new("validator.generic.variance-method", () => Generic(new TypeParameter("T") { Variance = "out" }), "invalid: GEN018 Variance is permitted only as in/out on interface type parameters.");
        yield return new("validator.generic.variance-interface-bad", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Interface, "I") { TypeParameters = new[] { new TypeParameter("T") { Variance = "inout" } } } } }, "invalid: GEN018 Variance is permitted only as in/out on interface type parameters.");
        yield return new("validator.generic.variance-interface", () => new CsDocument { Types = new[] { new CsTypeDeclaration(TypeKind.Interface, "I") { TypeParameters = new[] { new TypeParameter("T") { Variance = "in" }, new TypeParameter("U") { Variance = "out" } } } } }, "interface I<in T, out U>\n{\n}\n");
        yield return new("validator.constraint.two-primary", () => Constraints(Cs.Constraint("class"), Cs.Constraint("notnull")), "invalid: GEN021 Incompatible generic constraints.");
        yield return new("validator.constraint.struct-new", () => Constraints(Cs.Constraint("struct"), Cs.Constraint("new()")), "invalid: GEN021 Incompatible generic constraints.");
        yield return new("validator.constraint.unmanaged", () => Constraints(Cs.Constraint("unmanaged")), "class C\n{\n    void M<T>()\n        where T : unmanaged\n    {\n    }\n}\n");
        yield return new("validator.constraint.nullable-class-old", () => Constraints(Cs.Constraint("class?")).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable constraint requires C# 8.");
        yield return new("validator.constraint.notnull-old", () => Constraints(Cs.Constraint("notnull")).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Nullable constraint requires C# 8.");
        yield return new("validator.constraint.duplicate", () => Constraints(Cs.Constraint(Cs.Type("I")), Cs.Constraint(Cs.Type("I"))), "invalid: GEN019 Duplicate generic constraint.");
        yield return new("validator.constraint.default-old", () => Constraints(Cs.Constraint("default")).At(CsLanguageVersion.CSharp8), "invalid: GEN073 Default constraint requires C# 9.");
        yield return new("validator.constraint.ref-struct-old", () => Constraints(Cs.Constraint("allows ref struct")).At(CsLanguageVersion.CSharp12), "invalid: GEN073 Allows ref struct constraint requires C# 13.");
        yield return new("validator.constraint.new", () => Constraints(Cs.Constraint("class"), Cs.Constraint("new()")), "class C\n{\n    void M<T>()\n        where T : class, new()\n    {\n    }\n}\n");
        yield return new("validator.constraint.unknown", () => Constraints(Cs.Constraint("record")), "invalid: GEN020 Unknown generic constraint.");

        // Bodies.
        yield return new("validator.body.null-statements", () => InClass(M(new CsBody { Statements = null! })), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.body.tree-and-expression", () => InClass(new CsMethod(Cs.Int, "M") { Body = new CsBody { Tree = Cs.Tree(), Expression = Cs.Literal(1) } }), "invalid: GEN022 A body tree cannot coexist with another body representation.");
        yield return new("validator.body.tree-and-raw", () => InClass(M(new CsBody { Tree = Cs.Tree(), Raw = "return;" })), "invalid: GEN022 A body tree cannot coexist with another body representation.");
        yield return new("validator.body.tree-and-statements", () => InClass(M(new CsBody(Cs.Return()) { Tree = Cs.Tree() })), "invalid: GEN022 A body tree cannot coexist with another body representation.");
        yield return new("validator.body.tree-invalid", () => InClass(M(new CsBody { Tree = new CsTree(new CsNode[] { new CsSymbol(null, new CsSymbolOptions()) }) })), "invalid: SYM001 A symbol must be a reference, value, operation, declaration or compound.");
        yield return new("validator.body.tree", () => InClass(M(new CsBody { Tree = Cs.ParseTree("var x = 1; x++;").Root })), "class C\n{\n    void M()\n    {\n        var x = 1;\n        x++;\n    }\n}\n");
        yield return new("validator.body.raw-and-expression", () => InClass(new CsMethod(Cs.Int, "M") { Body = new CsBody { Raw = "return 1;", Expression = Cs.Literal(1) } }), "invalid: GEN022 Body must use a single raw, expression, or statement representation.");
        yield return new("validator.body.depth", () =>
        {
            CsStatement statement = Cs.Return();
            for (int i = 0; i < 140; i++) statement = Cs.Block(statement);
            return InMethod(statement);
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");

        // Accessors.
        yield return new("validator.accessor.duplicate", () => Property(new CsAccessor("get"), new CsAccessor("get")), "invalid: GEN024 Duplicate accessor.");
        yield return new("validator.accessor.set-init", () => Property(new CsAccessor("get"), new CsAccessor("set"), new CsAccessor("init")), "invalid: GEN029 Property cannot combine set and init.");
        yield return new("validator.accessor.event-add-only", () => Event(new CsAccessor("add") { Body = Cs.Body() }), "invalid: GEN030 Custom event needs both add and remove.");
        yield return new("validator.accessor.event-remove-only", () => Event(new CsAccessor("remove") { Body = Cs.Body() }), "invalid: GEN030 Custom event needs both add and remove.");
        yield return new("validator.accessor.modifiers-null", () => Property(new CsAccessor("get") { Modifiers = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("validator.accessor.modifiers-two", () => Property(new CsAccessor("get") { Modifiers = new[] { "readonly", "readonly" } }), "invalid: GEN031 Invalid accessor modifier.");
        yield return new("validator.accessor.modifier-static", () => Property(new CsAccessor("get") { Modifiers = new[] { "static" } }), "invalid: GEN031 Invalid accessor modifier.");
        yield return new("validator.accessor.readonly-old", () => InType(TypeKind.Struct, new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get") { Modifiers = new[] { "readonly" }, Body = Cs.Body(Cs.Return(Cs.Literal(1))) } } }).At(CsLanguageVersion.CSharp7_3), "invalid: GEN073 Readonly accessor requires C# 8.");
        yield return new("validator.accessor.kind", () => Property(new CsAccessor("add")), "invalid: GEN025 Invalid accessor kind.");
        yield return new("validator.accessor.event-kind", () => Event(new CsAccessor("get") { Body = Cs.Body() }, new CsAccessor("remove") { Body = Cs.Body() }), "invalid: GEN025 Invalid accessor kind. | GEN030 Custom event needs both add and remove.");
        yield return new("validator.accessor.accessibility", () => Property(new CsAccessor("get"), new CsAccessor("set") { Accessibility = "public" }), "invalid: GEN026 Invalid accessor accessibility.");
        yield return new("validator.accessor.accessibility-valid", () => Property(new CsAccessor("get"), new CsAccessor("set") { Accessibility = "private protected" }), "class C\n{\n    int P { get; private protected set; }\n}\n");
        yield return new("validator.accessor.abstract-body", () => InType(TypeKind.Class, new CsProperty(Cs.Int, "P") { Modifiers = new[] { "abstract" }, Accessors = new[] { new CsAccessor("get") { Body = Cs.Body() } } }), "invalid: GEN027 Abstract accessor cannot have a body.");
        yield return new("validator.accessor.event-no-body", () => Event(new CsAccessor("add"), new CsAccessor("remove") { Body = Cs.Body() }), "invalid: GEN028 Custom event accessor needs a body.");
    }

    static CsType FunctionPointer() => new("") { Form = TypeForm.FunctionPointer, FunctionParameters = new[] { new CsFunctionPointerParameter(Cs.Void) } };
}
