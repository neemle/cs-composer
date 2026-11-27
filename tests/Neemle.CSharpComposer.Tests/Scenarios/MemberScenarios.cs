using Neemle.CSharpComposer.Model;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Member, type declaration, operator, property, field, method, constructor and event rules.
public static class MemberScenarios
{
    static CsMethod M => new(Cs.Void, "M") { Body = Cs.Body() };

    static CsParameter P(string name, string type = "C") => new(Cs.Type(type), name);

    static CsDocument Top(params CsMember[] members) => new() { Types = members.OfType<CsTypeDeclaration>().ToArray(), Items = new(members) };

    static CsDocument TopType(CsTypeDeclaration type) => new() { Types = new[] { type } };

    static CsTypeDeclaration T(TypeKind kind, string name = "C") => new(kind, name);

    static CsDocument Operator(CsOperatorDeclaration op, TypeKind parent = TypeKind.Class) => InType(parent, op);

    static CsOperatorDeclaration Op(OverloadableOperator op, string returnType, params CsParameter[] parameters) =>
        new(op, Cs.Type(returnType)) { Modifiers = new[] { "public", "static" }, Parameters = parameters, Body = Cs.Body(Cs.Throw(Cs.Id("e"))) };

    static CsExtension Extension(params CsMember[] members) => new(Cs.Type("string"), "text") { Members = members };

    static CsDocument InStatic(params CsMember[] members) => new() { Types = new[] { new CsTypeDeclaration(TypeKind.Class, "E") { Modifiers = new[] { "static" }, Members = members } } };

    public static IEnumerable<Scenario> All()
    {
        // Common member rules.
        yield return new("member.depth", () =>
        {
            var type = T(TypeKind.Class, "N0");
            for (int i = 1; i < 135; i++) type = T(TypeKind.Class, "N" + i) with { Members = new CsMember[] { type } };
            return TopType(type);
        }, "invalid: GEN001 Node graph exceeds the supported nesting depth (128), or contains a cycle.");
        yield return new("member.disabled-text", () => InClass(new CsDisabledText("x\n")), "class C\n{\nx\n}\n");
        yield return new("member.directive", () => InClass(new CsDirective(DirectiveKind.Region) { Text = "R" }, new CsDirective(DirectiveKind.EndRegion)), "class C\n{\n#region R\n#endregion\n}\n");
        yield return new("member.explicit-interface-field", () => InClass(new CsField(Cs.Int, "F") { ExplicitInterface = Cs.Type("I") }), "invalid: GEN065 Explicit interface belongs to methods, properties, events or operators.");
        yield return new("member.explicit-interface-constructor", () => InClass(new CsConstructor("C") { ExplicitInterface = Cs.Type("I") }), "invalid: GEN065 Explicit interface belongs to methods, properties, events or operators.");
        yield return new("member.explicit-interface-method", () => InClass(new CsMethod(Cs.Void, "M") { ExplicitInterface = Cs.Type("I"), Body = Cs.Body() }), "class C\n{\n    void I.M()\n    {\n    }\n}\n");
        yield return new("member.explicit-interface-property", () => InClass(new CsProperty(Cs.Int, "P") { ExplicitInterface = Cs.Type("I"), Accessors = new[] { new CsAccessor("get") { Body = Cs.Body(Cs.Return(Cs.Literal(1))) } } }), "class C\n{\n    int I.P\n    {\n        get\n        {\n            return 1;\n        }\n    }\n}\n");
        yield return new("member.explicit-interface-event", () => InClass(new CsEvent("System.Action", "E") { ExplicitInterface = Cs.Type("I"), Accessors = new[] { new CsAccessor("add") { Body = Cs.Body() }, new CsAccessor("remove") { Body = Cs.Body() } } }), "class C\n{\n    event System.Action I.E\n    {\n        add\n        {\n        }\n        remove\n        {\n        }\n    }\n}\n");
        yield return new("member.explicit-interface-operator", () => InClass(new CsOperatorDeclaration(OverloadableOperator.Plus, Cs.Type("C")) { ExplicitInterface = Cs.Type("I"), Modifiers = new[] { "static" }, Parameters = new[] { P("a"), P("b") }, Body = Cs.Body(Cs.Return(Cs.Id("a"))) }), "class C\n{\n    static C I.operator +(C a, C b)\n    {\n        return a;\n    }\n}\n");
        yield return new("member.modifiers-null", () => InClass(new CsField(Cs.Int, "F") { Modifiers = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("member.file-nested", () => InClass(T(TypeKind.Class, "N") with { Modifiers = new[] { "file" } }), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers.");
        yield return new("member.file-public", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "file", "public" } }), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers.");
        yield return new("member.file-private", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "file", "private" } }), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers. | GEN062 Top-level type cannot be private, protected, or new.");
        yield return new("member.file-protected", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "file", "protected" } }), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers. | GEN062 Top-level type cannot be private, protected, or new.");
        yield return new("member.file-internal", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "file", "internal" } }), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers.");
        yield return new("member.file-sealed", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "file", "sealed" } }), "sealed file class C\n{\n}\n");
        yield return new("member.fixed-in-class", () => InClass(new CsField(Cs.Int, "F") { Modifiers = new[] { "fixed" }, FixedSize = Cs.Literal(2) }), "invalid: GEN092 Fixed buffer requires a struct.");
        yield return new("member.fixed-in-record-struct", () => InType(TypeKind.RecordStruct, new CsField(Cs.Int, "F") { Modifiers = new[] { "unsafe", "fixed" }, FixedSize = Cs.Literal(2) }), "record struct C\n{\n    unsafe fixed int F[2];\n}\n");
        yield return new("member.fixed-in-struct", () => InType(TypeKind.Struct, new CsField(Cs.Int, "F") { Modifiers = new[] { "unsafe", "fixed" }, FixedSize = Cs.Literal(2) }), "struct C\n{\n    unsafe fixed int F[2];\n}\n");
        yield return new("member.modifier-unknown", () => InClass(M with { Modifiers = new[] { "fixed" } }), "invalid: GEN031 Modifier 'fixed' is not valid for this construct.");
        yield return new("member.modifier-duplicate", () => InClass(M with { Modifiers = new[] { "static", "static" } }), "invalid: GEN032 Duplicate modifier.");
        yield return new("member.access-conflict", () => InClass(M with { Modifiers = new[] { "public", "internal" } }), "invalid: GEN033 Conflicting accessibility modifiers.");
        yield return new("member.sealed-no-override", () => InClass(M with { Modifiers = new[] { "sealed" } }), "invalid: GEN035 Sealed member must override.");
        yield return new("member.sealed-override", () => InClass(M with { Modifiers = new[] { "sealed", "override" } }), "class C\n{\n    sealed override void M()\n    {\n    }\n}\n");
        yield return new("member.sealed-type", () => InClass(T(TypeKind.Class, "N") with { Modifiers = new[] { "sealed" } }), "class C\n{\n    sealed class N\n    {\n    }\n}\n");
        yield return new("member.method-in-enum", () => InType(TypeKind.Enum, M), "invalid: GEN036 Enum may contain only enum members.");
        yield return new("member.enum-member-in-class", () => InClass(new CsEnumMember("A")), "invalid: GEN037 Enum member requires an enum parent.");
        yield return new("member.enum-member-value", () => InType(TypeKind.Enum, new CsEnumMember("A", Cs.Literal(1)), new CsEnumMember("B")), "enum C\n{\n    A = 1,\n    B,\n}\n");
        yield return new("member.unsupported", () => InClass(new ForeignMember()), "invalid: GEN055 Unsupported member node.");

        // Delegates and destructors.
        yield return new("member.delegate", () => new CsDocument { Items = new CsNode[] { new CsDelegate(Cs.Int, "D") { Parameters = new[] { P("x", "int") }, TypeParameters = new[] { new TypeParameter("T") { Variance = "in" } } } } }, "delegate int D<in T>(int x);\n");
        yield return new("member.destructor-in-struct", () => InType(TypeKind.Struct, new CsDestructor("C", Cs.Body())), "invalid: GEN051 Destructor requires its containing class name.");
        yield return new("member.destructor-in-record", () => InType(TypeKind.Record, new CsDestructor("C", Cs.Body())), "record C\n{\n    ~C()\n    {\n    }\n}\n");
        yield return new("member.destructor-in-record-class", () => InType(TypeKind.RecordClass, new CsDestructor("C", Cs.Body())), "record class C\n{\n    ~C()\n    {\n    }\n}\n");
        yield return new("member.destructor-wrong-name", () => InClass(new CsDestructor("D", Cs.Body())), "invalid: GEN051 Destructor requires its containing class name.");
        yield return new("member.destructor-top-level", () => new CsDocument { Items = new CsNode[] { new CsDestructor("C", Cs.Body()) } }, "invalid: GEN075 Invalid document child kind.");
        yield return new("member.destructor-extern", () => InClass(new CsDestructor("C", null) { Modifiers = new[] { "extern" } }), "class C\n{\n    extern ~C();\n}\n");
        yield return new("member.destructor-no-body", () => InClass(new CsDestructor("C", null)), "invalid: GEN002 Required node or collection is null.");

        // Extension blocks.
        yield return new("member.extension-old", () => InStatic(Extension()).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Extension blocks requires C# 14.");
        yield return new("member.extension-receiver", () => InStatic(new CsExtension(Cs.Type("string"), "1text")), "invalid: GEN003 Invalid identifier.");
        yield return new("member.extension-anonymous", () => InStatic(new CsExtension(Cs.Type("string")) { Members = new CsMember[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body() } } }), "static class E\n{\n    extension(string)\n    {\n        void M()\n        {\n        }\n    }\n}\n");
        yield return new("member.extension-modifier-ref", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "ref" }), "static class E\n{\n    extension(ref System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-in", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "in" }), "static class E\n{\n    extension(in System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-ref-readonly", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "ref readonly" }), "static class E\n{\n    extension(ref readonly System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-scoped", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "scoped" }), "static class E\n{\n    extension(scoped System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-scoped-ref", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "scoped ref" }), "static class E\n{\n    extension(scoped ref System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-scoped-in", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "scoped in" }), "static class E\n{\n    extension(scoped in System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-scoped-ref-readonly", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "scoped ref readonly" }), "static class E\n{\n    extension(scoped ref readonly System.Span<int> s)\n    {\n    }\n}\n");
        yield return new("member.extension-modifier-out", () => InStatic(new CsExtension(Cs.Type("System.Span").Generic(Cs.Int), "s") { ReceiverModifier = "out" }), "invalid: GEN016 Invalid extension receiver modifier.");
        yield return new("member.extension-indexer-old", () => InStatic(Extension(new CsProperty(Cs.Type("char"), "this") { IsIndexer = true, Parameters = new[] { P("i", "int") }, Expression = Cs.Literal('c') })).At(CsLanguageVersion.CSharp14), "invalid: GEN073 Extension indexer requires C# 15.");
        yield return new("member.extension-indexer-anonymous", () => InStatic(new CsExtension(Cs.Type("string")) { Members = new CsMember[] { new CsProperty(Cs.Type("char"), "this") { IsIndexer = true, Parameters = new[] { P("i", "int") }, Expression = Cs.Literal('c') } } }), "invalid: GEN092 Extension indexer requires a named receiver.");
        yield return new("member.extension-members-null", () => InStatic(Extension() with { Members = null! }), "invalid: GEN002 Required node or collection is null.");

        // Operators.
        yield return new("operator.parameters-null", () => Operator(Op(OverloadableOperator.Plus, "C") with { Parameters = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("operator.unknown", () => Operator(Op((OverloadableOperator)999, "C", P("a"), P("b"))), "invalid: GEN092 Unknown overloadable operator.");
        yield return new("operator.no-body", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { Body = null }), "invalid: GEN002 Required node or collection is null.");
        yield return new("operator.extern", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { Body = null, Modifiers = new[] { "public", "static", "extern" } }), "class C\n{\n    public static extern C operator +(C a, C b);\n}\n");
        yield return new("operator.abstract-interface", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { Body = null, Modifiers = new[] { "static", "abstract" } }, TypeKind.Interface), "interface C\n{\n    static abstract C operator +(C a, C b);\n}\n");
        yield return new("operator.instance", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { Modifiers = new[] { "public" } }), "invalid: GEN092 This operator must be static.");
        yield return new("operator.not-public", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { Modifiers = new[] { "static" } }), "invalid: GEN092 Operator must be public or explicitly implement an interface.");
        yield return new("operator.checked-not", () => Operator(Op(OverloadableOperator.Not, "C", P("a")) with { IsChecked = true }), "invalid: GEN092 This operator has no checked form.");
        yield return new("operator.checked-Plus", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { IsChecked = true }), "class C\n{\n    public static C operator checked +(C a, C b)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-Minus", () => Operator(Op(OverloadableOperator.Minus, "C", P("a"), P("b")) with { IsChecked = true }), "class C\n{\n    public static C operator checked -(C a, C b)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-Multiply", () => Operator(Op(OverloadableOperator.Multiply, "C", P("a"), P("b")) with { IsChecked = true }), "class C\n{\n    public static C operator checked *(C a, C b)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-Divide", () => Operator(Op(OverloadableOperator.Divide, "C", P("a"), P("b")) with { IsChecked = true }), "class C\n{\n    public static C operator checked /(C a, C b)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-Increment", () => Operator(Op(OverloadableOperator.Increment, "C", P("a")) with { IsChecked = true }), "class C\n{\n    public static C operator checked ++(C a)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-Decrement", () => Operator(Op(OverloadableOperator.Decrement, "C", P("a")) with { IsChecked = true }), "class C\n{\n    public static C operator checked --(C a)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-explicit", () => Operator(Op(OverloadableOperator.Explicit, "int", P("a")) with { IsChecked = true }), "class C\n{\n    public static explicit operator checked int(C a)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.checked-old", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b")) with { IsChecked = true }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Checked operators requires C# 11.");
        yield return new("operator.checked-add-assign", () => Operator(new CsOperatorDeclaration(OverloadableOperator.AddAssign, Cs.Void) { Modifiers = new[] { "public" }, Parameters = new[] { P("x", "int") }, Body = Cs.Body(), IsChecked = true }), "class C\n{\n    public void operator checked +=(int x)\n    {\n    }\n}\n");
        yield return new("operator.compound-old", () => Operator(new CsOperatorDeclaration(OverloadableOperator.AddAssign, Cs.Void) { Modifiers = new[] { "public" }, Parameters = new[] { P("x", "int") }, Body = Cs.Body() }).At(CsLanguageVersion.CSharp13), "invalid: GEN073 Instance mutation operators requires C# 14.");
        yield return new("operator.compound-static", () => Operator(new CsOperatorDeclaration(OverloadableOperator.AddAssign, Cs.Void) { Modifiers = new[] { "public", "static" }, Parameters = new[] { P("x", "int") }, Body = Cs.Body() }), "invalid: GEN092 Instance mutation operator has an invalid signature.");
        yield return new("operator.compound-two-parameters", () => Operator(new CsOperatorDeclaration(OverloadableOperator.AddAssign, Cs.Void) { Modifiers = new[] { "public" }, Parameters = new[] { P("x", "int"), P("y", "int") }, Body = Cs.Body() }), "invalid: GEN092 Instance mutation operator has an invalid signature.");
        yield return new("operator.compound-returns", () => Operator(new CsOperatorDeclaration(OverloadableOperator.AddAssign, Cs.Int) { Modifiers = new[] { "public" }, Parameters = new[] { P("x", "int") }, Body = Cs.Body(Cs.Return(Cs.Literal(1))) }), "invalid: GEN092 Instance mutation operator has an invalid signature.");
        yield return new("operator.instance-increment", () => Operator(new CsOperatorDeclaration(OverloadableOperator.Increment, Cs.Void) { Modifiers = new[] { "public" }, Body = Cs.Body() }), "class C\n{\n    public void operator ++()\n    {\n    }\n}\n");
        yield return new("operator.instance-increment-parameter", () => Operator(new CsOperatorDeclaration(OverloadableOperator.Decrement, Cs.Void) { Modifiers = new[] { "public" }, Parameters = new[] { P("x") }, Body = Cs.Body() }), "invalid: GEN092 Instance mutation operator has an invalid signature.");
        yield return new("operator.instance-increment-returns", () => Operator(new CsOperatorDeclaration(OverloadableOperator.Increment, Cs.Int) { Modifiers = new[] { "public" }, Body = Cs.Body(Cs.Return(Cs.Literal(1))) }), "invalid: GEN092 Instance mutation operator has an invalid signature.");
        yield return new("operator.unary-two", () => Operator(Op(OverloadableOperator.Complement, "C", P("a"), P("b"))), "invalid: GEN092 Operator has an invalid signature.");
        yield return new("operator.true-false", () => Operator(Op(OverloadableOperator.True, "bool", P("a")), TypeKind.Class) with { }, "class C\n{\n    public static bool operator true(C a)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.binary-one", () => Operator(Op(OverloadableOperator.Multiply, "C", P("a"))), "invalid: GEN092 Operator has an invalid signature.");
        yield return new("operator.minus-unary", () => Operator(Op(OverloadableOperator.Minus, "C", P("a"))), "class C\n{\n    public static C operator -(C a)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.void-return", () => Operator(Op(OverloadableOperator.Multiply, "void", P("a"), P("b"))), "invalid: GEN092 Operator has an invalid signature.");
        yield return new("operator.no-parameters", () => Operator(Op(OverloadableOperator.Plus, "C")), "invalid: GEN092 Operator has an invalid parameter count.");
        yield return new("operator.three-parameters", () => Operator(Op(OverloadableOperator.Plus, "C", P("a"), P("b"), P("c"))), "invalid: GEN092 Operator has an invalid parameter count.");
        yield return new("operator.conversion-two", () => Operator(Op(OverloadableOperator.Implicit, "int", P("a"), P("b"))), "invalid: GEN092 Operator has an invalid parameter count.");
        yield return new("operator.conversion", () => Operator(Op(OverloadableOperator.Implicit, "int", P("a"))), "class C\n{\n    public static implicit operator int(C a)\n    {\n        throw e;\n    }\n}\n");
        yield return new("operator.decrement-static", () => Operator(Op(OverloadableOperator.Decrement, "C", P("a"))), "class C\n{\n    public static C operator --(C a)\n    {\n        throw e;\n    }\n}\n");

        // Type declarations.
        yield return new("type.unknown-kind", () => TopType(T((TypeKind)99)), "invalid: GEN038 Unknown declaration kind.");
        yield return new("type.union", () => TopType(T(TypeKind.Union, "U") with { UnionCases = new[] { Cs.Int, Cs.Type("string") } }), "union U(int, string)\n{\n}\n");
        yield return new("type.union-old", () => TopType(T(TypeKind.Union, "U") with { UnionCases = new[] { Cs.Int } }).At(CsLanguageVersion.CSharp14), "invalid: GEN073 Union declaration requires C# 15.");
        yield return new("type.union-empty", () => TopType(T(TypeKind.Union, "U")), "invalid: GEN092 Union requires case types.");
        yield return new("type.union-null", () => TopType(T(TypeKind.Union, "U") with { UnionCases = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("type.union-cases-on-class", () => TopType(T(TypeKind.Class) with { UnionCases = new[] { Cs.Int } }), "invalid: GEN065 Union cases belong to a union.");
        yield return new("type.union-cases-null-on-class", () => TopType(T(TypeKind.Class) with { UnionCases = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("type.closed", () => TopType(T(TypeKind.Record) with { Modifiers = new[] { "closed" } }), "closed record C\n{\n}\n");
        yield return new("type.closed-class", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "closed" } }), "closed class C\n{\n}\n");
        yield return new("type.closed-record-class", () => TopType(T(TypeKind.RecordClass) with { Modifiers = new[] { "closed" } }), "closed record class C\n{\n}\n");
        yield return new("type.closed-old", () => TopType(T(TypeKind.Record) with { Modifiers = new[] { "closed" } }).At(CsLanguageVersion.CSharp14), "invalid: GEN073 Closed hierarchy requires C# 15.");
        yield return new("type.closed-struct", () => TopType(T(TypeKind.Struct) with { Modifiers = new[] { "closed" } }), "invalid: GEN034 Invalid closed hierarchy modifiers.");
        yield return new("type.closed-abstract", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "closed", "abstract" } }), "invalid: GEN034 Invalid closed hierarchy modifiers.");
        yield return new("type.closed-sealed", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "closed", "sealed" } }), "invalid: GEN034 Invalid closed hierarchy modifiers.");
        yield return new("type.closed-static", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "closed", "static" } }), "invalid: GEN034 Invalid closed hierarchy modifiers.");
        yield return new("type.top-private", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "private" } }), "invalid: GEN062 Top-level type cannot be private, protected, or new.");
        yield return new("type.struct-abstract", () => TopType(T(TypeKind.Struct) with { Modifiers = new[] { "abstract" } }), "invalid: GEN063 Struct declaration cannot be abstract or sealed.");
        yield return new("type.record-struct-sealed", () => TopType(T(TypeKind.RecordStruct) with { Modifiers = new[] { "sealed" } }), "invalid: GEN063 Struct declaration cannot be abstract or sealed.");
        yield return new("type.readonly-class", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "readonly" } }), "invalid: GEN039 Ref/readonly declaration must be a struct.");
        yield return new("type.ref-class", () => TopType(T(TypeKind.Class) with { Modifiers = new[] { "ref" } }), "invalid: GEN039 Ref/readonly declaration must be a struct.");
        yield return new("type.static-struct", () => TopType(T(TypeKind.Struct) with { Modifiers = new[] { "static" } }), "invalid: GEN040 Only class declarations can be static.");
        yield return new("type.ref-record-struct", () => TopType(T(TypeKind.RecordStruct) with { Modifiers = new[] { "ref" } }), "invalid: GEN041 Record struct cannot be ref.");
        yield return new("type.readonly-ref-struct", () => TopType(T(TypeKind.Struct) with { Modifiers = new[] { "readonly", "ref" } }), "readonly ref struct C\n{\n}\n");
        yield return new("type.primary-class", () => TopType(T(TypeKind.Class) with { PrimaryParameters = new[] { P("x", "int") } }), "class C(int x)\n{\n}\n");
        yield return new("type.primary-struct-old", () => TopType(T(TypeKind.Struct) with { HasPrimaryConstructor = true }).At(CsLanguageVersion.CSharp11), "invalid: GEN073 Primary constructor requires C# 12.");
        yield return new("type.primary-interface", () => TopType(T(TypeKind.Interface, "I") with { HasPrimaryConstructor = true }), "invalid: GEN042 Only classes, structs and records have primary constructors.");
        yield return new("type.primary-record", () => TopType(T(TypeKind.Record, "R") with { PrimaryParameters = new[] { P("X", "int") } }), "record R(int X)\n{\n}\n");
        yield return new("type.primary-record-struct", () => TopType(T(TypeKind.RecordStruct, "R") with { PrimaryParameters = new[] { P("X", "int") } }), "record struct R(int X)\n{\n}\n");
        yield return new("type.primary-null", () => TopType(T(TypeKind.Class) with { PrimaryParameters = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("type.base-arguments", () => TopType(T(TypeKind.Class) with { PrimaryParameters = new[] { P("x", "int") }, BaseTypes = new[] { Cs.Type("B") }, BaseArguments = new[] { Cs.Id("x") } }), "class C(int x) : B(x)\n{\n}\n");
        yield return new("type.base-arguments-no-base", () => TopType(T(TypeKind.Class) with { PrimaryParameters = new[] { P("x", "int") }, BaseArguments = new[] { Cs.Id("x") } }), "invalid: GEN042 Base arguments require a primary constructor and base type.");
        yield return new("type.base-arguments-base-null", () => TopType(T(TypeKind.Class) with { PrimaryParameters = new[] { P("x", "int") }, BaseTypes = null!, BaseArguments = new[] { Cs.Id("x") } }), "invalid: GEN002 Required node or collection is null.");
        yield return new("type.base-arguments-no-primary", () => TopType(T(TypeKind.Class) with { BaseTypes = new[] { Cs.Type("B") }, BaseArguments = Values<CsExpression>.Empty }), "invalid: GEN042 Base arguments require a primary constructor and base type.");
        yield return new("type.base-arguments-empty-primary", () => TopType(T(TypeKind.Class) with { HasPrimaryConstructor = true, BaseTypes = new[] { Cs.Type("B") }, BaseArguments = Values<CsExpression>.Empty }), "class C() : B()\n{\n}\n");
        yield return new("type.base-arguments-primary-null", () => TopType(T(TypeKind.Class) with { PrimaryParameters = null!, BaseTypes = new[] { Cs.Type("B") }, BaseArguments = Values<CsExpression>.Empty }), "invalid: GEN002 Required node or collection is null.");
        yield return new("type.generic-enum", () => TopType(T(TypeKind.Enum, "E") with { TypeParameters = new[] { new TypeParameter("T") } }), "invalid: GEN043 Enums cannot be generic.");
        yield return new("type.generic-enum-null", () => TopType(T(TypeKind.Enum, "E") with { TypeParameters = null! }), "invalid: GEN002 Required node or collection is null.");

        // Properties.
        yield return new("property.indexer-name", () => InClass(new CsProperty(Cs.Int, "Item") { IsIndexer = true, Parameters = new[] { P("i", "int") }, Expression = Cs.Literal(1) }), "invalid: GEN065 Indexer name must be this.");
        yield return new("property.indexer-no-parameters", () => InClass(new CsProperty(Cs.Int, "this") { IsIndexer = true, Expression = Cs.Literal(1) }), "invalid: GEN092 Indexer requires parameters and cannot have an initializer.");
        yield return new("property.indexer-parameters-null", () => InClass(new CsProperty(Cs.Int, "this") { IsIndexer = true, Parameters = null!, Expression = Cs.Literal(1) }), "invalid: GEN002 Required node or collection is null.");
        yield return new("property.indexer-initializer", () => InClass(new CsProperty(Cs.Int, "this") { IsIndexer = true, Parameters = new[] { P("i", "int") }, Accessors = new[] { new CsAccessor("get") }, Initializer = Cs.Literal(1) }), "invalid: GEN092 Indexer requires parameters and cannot have an initializer.");
        yield return new("property.parameters-not-indexer", () => InClass(new CsProperty(Cs.Int, "P") { Parameters = new[] { P("i", "int") }, Expression = Cs.Literal(1) }), "invalid: GEN065 Parameters belong to an indexer.");
        yield return new("property.parameters-null", () => InClass(new CsProperty(Cs.Int, "P") { Parameters = null!, Expression = Cs.Literal(1) }), "invalid: GEN002 Required child collection is null.");
        yield return new("property.expression-accessors", () => InClass(new CsProperty(Cs.Int, "P") { Expression = Cs.Literal(1), Accessors = new[] { new CsAccessor("get") } }), "invalid: GEN044 Expression property cannot have accessors or initializer.");
        yield return new("property.expression-initializer", () => InClass(new CsProperty(Cs.Int, "P") { Expression = Cs.Literal(1), Initializer = Cs.Literal(2) }), "invalid: GEN044 Expression property cannot have accessors or initializer.");
        yield return new("property.expression-accessors-null", () => InClass(new CsProperty(Cs.Int, "P") { Expression = Cs.Literal(1), Accessors = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("property.empty", () => InClass(new CsProperty(Cs.Int, "P")), "invalid: GEN045 Property needs an expression or accessors.");
        yield return new("property.accessors-null", () => InClass(new CsProperty(Cs.Int, "P") { Accessors = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("property.abstract-expression", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "abstract" }, Expression = Cs.Literal(1) }), "invalid: GEN046 Abstract property cannot have implementation or initializer.");
        yield return new("property.abstract-initializer", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "abstract" }, Accessors = new[] { new CsAccessor("get") }, Initializer = Cs.Literal(1) }), "invalid: GEN046 Abstract property cannot have implementation or initializer.");
        yield return new("property.required-static", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required", "static" }, Accessors = new[] { new CsAccessor("get"), new CsAccessor("set") } }), "invalid: GEN047 Required property must be a writable instance property.");
        yield return new("property.required-expression", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required" }, Expression = Cs.Literal(1) }), "invalid: GEN047 Required property must be a writable instance property.");
        yield return new("property.required-get-only", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required" }, Accessors = new[] { new CsAccessor("get") } }), "invalid: GEN047 Required property must be a writable instance property.");
        yield return new("property.required-init", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required" }, Accessors = new[] { new CsAccessor("get"), new CsAccessor("init") } }), "class C\n{\n    required int P { get; init; }\n}\n");
        yield return new("property.required-accessors-null", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required" }, Accessors = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("property.required-null-accessor", () => InClass(new CsProperty(Cs.Int, "P") { Modifiers = new[] { "required" }, Accessors = new CsAccessor[] { null!, new CsAccessor("set") } }), "invalid: GEN002 Required node or collection is null.");
        yield return new("property.interface-initializer", () => InType(TypeKind.Interface, new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get") }, Initializer = Cs.Literal(1) }), "invalid: GEN058 Interface instance property cannot have an initializer.");
        yield return new("property.interface-static-initializer", () => InType(TypeKind.Interface, new CsProperty(Cs.Int, "P") { Modifiers = new[] { "static" }, Accessors = new[] { new CsAccessor("get") }, Initializer = Cs.Literal(1) }), "interface C\n{\n    static int P { get; } = 1;\n}\n");

        // Fields.
        yield return new("field.fixed-without-modifier", () => InType(TypeKind.Struct, new CsField(Cs.Int, "F") { FixedSize = Cs.Literal(2) }), "invalid: GEN092 Fixed buffer needs the fixed modifier, a size and no initializer.");
        yield return new("field.fixed-without-size", () => InType(TypeKind.Struct, new CsField(Cs.Int, "F") { Modifiers = new[] { "unsafe", "fixed" } }), "invalid: GEN092 Fixed buffer needs the fixed modifier, a size and no initializer.");
        yield return new("field.fixed-initializer", () => InType(TypeKind.Struct, new CsField(Cs.Int, "F") { Modifiers = new[] { "unsafe", "fixed" }, FixedSize = Cs.Literal(2), Initializer = Cs.Literal(1) }), "invalid: GEN092 Fixed buffer needs the fixed modifier, a size and no initializer.");
        yield return new("field.ref-old", () => TopType(T(TypeKind.Struct) with { Modifiers = new[] { "ref" }, Members = new CsMember[] { new CsField(Cs.Int.Ref(), "F") } }).At(CsLanguageVersion.CSharp10), "invalid: GEN073 Ref fields requires C# 11.");
        yield return new("field.ref-null-type", () => InClass(new CsField(null!, "F")), "invalid: GEN002 Required node or collection is null.");
        yield return new("field.additional", () => InClass(new CsField(Cs.Int, "F") { AdditionalVariables = new[] { new CsVariableDeclarator("G", Cs.Literal(1)), new CsVariableDeclarator("1H") } }), "invalid: GEN003 Invalid identifier.");
        yield return new("field.additional-fixed", () => InType(TypeKind.Struct, new CsField(Cs.Int, "F") { Modifiers = new[] { "unsafe", "fixed" }, FixedSize = Cs.Literal(2), AdditionalVariables = new[] { new CsVariableDeclarator("G") { FixedSize = Cs.Literal(3) } } }), "struct C\n{\n    unsafe fixed int F[2], G[3];\n}\n");
        yield return new("field.interface-instance", () => InType(TypeKind.Interface, new CsField(Cs.Int, "F")), "invalid: GEN056 Interface field must be static or const.");
        yield return new("field.interface-const", () => InType(TypeKind.Interface, new CsField(Cs.Int, "F") { Modifiers = new[] { "const" }, Initializer = Cs.Literal(1) }), "interface C\n{\n    const int F = 1;\n}\n");
        yield return new("field.interface-static", () => InType(TypeKind.Interface, new CsField(Cs.Int, "F") { Modifiers = new[] { "static" } }), "interface C\n{\n    static int F;\n}\n");
        yield return new("field.const-readonly", () => InClass(new CsField(Cs.Int, "F") { Modifiers = new[] { "const", "readonly" }, Initializer = Cs.Literal(1) }), "invalid: GEN048 Invalid constant/readonly/volatile field combination.");

        // Methods.
        yield return new("method.abstract-body", () => InClass(M with { Modifiers = new[] { "abstract" } }), "invalid: GEN049 Abstract/extern method cannot have a body.");
        yield return new("method.extern-body", () => InClass(M with { Modifiers = new[] { "extern" } }), "invalid: GEN049 Abstract/extern method cannot have a body.");
        yield return new("method.interface-private", () => InType(TypeKind.Interface, M with { Modifiers = new[] { "private" }, Body = null }), "invalid: GEN060 Interface private method must have a body.");
        yield return new("method.interface-static", () => InType(TypeKind.Interface, M with { Modifiers = new[] { "static" }, Body = null }), "invalid: GEN061 Interface concrete static method must have a body.");
        yield return new("method.interface-static-abstract", () => InType(TypeKind.Interface, M with { Modifiers = new[] { "static", "abstract" }, Body = null }), "interface C\n{\n    static abstract void M();\n}\n");
        yield return new("method.interface-extern", () => InType(TypeKind.Interface, M with { Modifiers = new[] { "private", "extern" }, Body = null }), "interface C\n{\n    private extern void M();\n}\n");
        yield return new("method.interface-body", () => InType(TypeKind.Interface, M with { Modifiers = new[] { "private" } }), "interface C\n{\n    private void M()\n    {\n    }\n}\n");
        yield return new("method.interface-abstract", () => InType(TypeKind.Interface, M with { Body = null }), "interface C\n{\n    void M();\n}\n");
        yield return new("method.no-body", () => InClass(M with { Body = null }), "invalid: GEN050 Concrete method needs a body.");
        yield return new("method.partial-no-body", () => InClass(M with { Modifiers = new[] { "partial" }, Body = null }), "class C\n{\n    partial void M();\n}\n");

        // Constructors.
        yield return new("constructor.interface", () => InType(TypeKind.Interface, new CsConstructor("C")), "invalid: GEN057 Interface constructor must be static.");
        yield return new("constructor.interface-static", () => InType(TypeKind.Interface, new CsConstructor("C") { Modifiers = new[] { "static" } }), "interface C\n{\n    static C()\n    {\n    }\n}\n");
        yield return new("constructor.extern-body", () => InClass(new CsConstructor("C") { Modifiers = new[] { "extern" } }), "invalid: GEN049 Extern constructor cannot have a body.");
        yield return new("constructor.extern", () => InClass(new CsConstructor("C") { Modifiers = new[] { "extern" }, Body = null }), "class C\n{\n    extern C();\n}\n");
        yield return new("constructor.partial", () => InClass(new CsConstructor("C") { Modifiers = new[] { "partial" }, Body = null }), "class C\n{\n    partial C();\n}\n");
        yield return new("constructor.no-body", () => InClass(new CsConstructor("C") { Body = null }), "invalid: GEN002 Required node or collection is null.");
        yield return new("constructor.top-level", () => new CsDocument { Items = new CsNode[] { new CsConstructor("C") } }, "invalid: GEN075 Invalid document child kind.");
        yield return new("constructor.wrong-name", () => InClass(new CsConstructor("D")), "invalid: GEN051 Constructor name must match its declaring type.");
        yield return new("constructor.initializer-kind", () => InClass(new CsConstructor("C") { InitializerKind = "self" }), "invalid: GEN052 Constructor initializer must be base or this.");
        yield return new("constructor.initializer-this", () => InClass(new CsConstructor("C") { InitializerKind = "this", InitializerArguments = new[] { Cs.Literal(1) } }, new CsConstructor("C") { Parameters = new[] { P("x", "int") } }), "class C\n{\n    C() : this(1)\n    {\n    }\n\n    C(int x)\n    {\n    }\n}\n");
        yield return new("constructor.static-public", () => InClass(new CsConstructor("C") { Modifiers = new[] { "static", "public" } }), "invalid: GEN053 Static constructor cannot have accessibility, parameters, or initializer.");

        // Events.
        yield return new("event.custom-additional", () => InClass(new CsEvent("System.Action", "E") { Accessors = new[] { new CsAccessor("add") { Body = Cs.Body() }, new CsAccessor("remove") { Body = Cs.Body() } }, AdditionalVariables = new[] { new CsVariableDeclarator("F") } }), "invalid: GEN065 Custom event cannot contain additional declarators.");
        yield return new("event.additional", () => InClass(new CsEvent("System.Action", "E") { AdditionalVariables = new[] { new CsVariableDeclarator("F", Cs.Literal(null)), new CsVariableDeclarator("G") } }), "class C\n{\n    event System.Action E, F = null, G;\n}\n");
        yield return new("event.accessors-null-additional", () => InClass(new CsEvent("System.Action", "E") { Accessors = null!, AdditionalVariables = new[] { new CsVariableDeclarator("F") } }), "invalid: GEN002 Required node or collection is null.");
        yield return new("event.additional-null", () => InClass(new CsEvent("System.Action", "E") { AdditionalVariables = null! }), "invalid: GEN002 Required node or collection is null.");
        yield return new("event.interface-initializer", () => InType(TypeKind.Interface, new CsEvent("System.Action", "E") { Initializer = Cs.Literal(null) }), "invalid: GEN059 Interface instance event cannot have an initializer.");
        yield return new("event.interface-static-initializer", () => InType(TypeKind.Interface, new CsEvent("System.Action", "E") { Modifiers = new[] { "static" }, Initializer = Cs.Literal(null) }), "interface C\n{\n    static event System.Action E = null;\n}\n");
        yield return new("event.custom-initializer", () => InClass(new CsEvent("System.Action", "E") { Accessors = new[] { new CsAccessor("add") { Body = Cs.Body() }, new CsAccessor("remove") { Body = Cs.Body() } }, Initializer = Cs.Literal(null) }), "invalid: GEN054 Custom/abstract event cannot have an initializer.");
        yield return new("event.abstract-initializer", () => InClass(new CsEvent("System.Action", "E") { Modifiers = new[] { "abstract" }, Initializer = Cs.Literal(null) }), "invalid: GEN054 Custom/abstract event cannot have an initializer.");
        yield return new("event.initializer", () => InClass(new CsEvent("System.Action", "E") { Initializer = Cs.Literal(null) }), "class C\n{\n    event System.Action E = null;\n}\n");
        yield return new("event.accessors-null-initializer", () => InClass(new CsEvent("System.Action", "E") { Accessors = null!, Initializer = Cs.Literal(null) }), "invalid: GEN002 Required node or collection is null.");
    }
}
