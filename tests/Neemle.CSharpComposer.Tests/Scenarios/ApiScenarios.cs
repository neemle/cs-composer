using Neemle.CSharpComposer.Builders;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using static Neemle.CSharpComposer.Tests.Scenarios.Build;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Cs factory methods, CLR type mapping, literal text and the fluent builders.
public static class ApiScenarios
{
    public sealed class Outer<T> { public sealed class Inner<U> { } public sealed class Plain { } }

    public sealed class Holder { public sealed class Nested { } }

    // A builder declared with another builder type as its TSelf.
    public sealed class Misdeclared : MemberBuilder<FieldBuilder<FileBuilder>> { }

    static CsDocument Literals(params object?[] values) =>
        InClass(values.Select((value, i) => (CsMember)new CsField(Cs.Type("object"), "F" + i) { Initializer = Cs.Literal(value) }).ToArray());

    public static IEnumerable<Scenario> All()
    {
        // CLR type mapping.
        yield return new("api.type.byref", () => Typed(Cs.Type(typeof(int).MakeByRefType())), "class C\n{\n    ref int F;\n}\n");
        yield return new("api.type.pointer", () => Typed(Cs.Type(typeof(int*))), "class C\n{\n    int* F;\n}\n");
        yield return new("api.type.array", () => Typed(Cs.Type(typeof(string[,]))), "class C\n{\n    string[,] F;\n}\n");
        yield return new("api.type.generic-parameter", () => Typed(Cs.Type(typeof(List<>).GetGenericArguments()[0])), "class C\n{\n    T F;\n}\n");
        yield return new("api.type.nullable", () => Typed(Cs.Type<int?>()), "class C\n{\n    int? F;\n}\n");
        yield return new("api.type.generic", () => Typed(Cs.Type<Dictionary<string, List<int>>>()), "class C\n{\n    global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<int>> F;\n}\n");
        yield return new("api.type.no-namespace", () => Typed(Cs.Type(typeof(ScenarioGlobalProbe))), "class C\n{\n    global::ScenarioGlobalProbe F;\n}\n");
        yield return new("api.type.nested", () => Typed(Cs.Type<Holder.Nested>()), "class C\n{\n    global::Neemle.CSharpComposer.Tests.Scenarios.ApiScenarios.Holder.Nested F;\n}\n");
        yield return new("api.type.nested-generic", () => Typed(Cs.Type<Outer<int>.Inner<string>>()), "class C\n{\n    global::Neemle.CSharpComposer.Tests.Scenarios.ApiScenarios.Outer<int>.Inner<string> F;\n}\n");
        yield return new("api.type.nested-in-generic", () => Typed(Cs.Type<Outer<long>.Plain>()), "class C\n{\n    global::Neemle.CSharpComposer.Tests.Scenarios.ApiScenarios.Outer<long>.Plain F;\n}\n");
        yield return new("api.type.primitives", () => InClass(new CsField(Cs.Bool, "A"), new CsField(Cs.Generic("List", Cs.Int), "B"), new CsField(Cs.Nullable(Cs.Int), "C"), new CsField(Cs.Array(Cs.Int, 2), "D"), new CsField(CsType.Int32, "E"), new CsField(CsType.String, "G"), new CsField(CsType.Boolean, "H")), "class C\n{\n    bool A;\n\n    List<int> B;\n\n    int? C;\n\n    int[,] D;\n\n    int E;\n\n    string G;\n\n    bool H;\n}\n");
        yield return new("api.type.void", () => InClass(new CsMethod(CsType.Void, "M") { Body = Cs.Body() }), "class C\n{\n    void M()\n    {\n    }\n}\n");

        // Literals.
        yield return new("api.literal.numbers", () => Literals(1.5f, 2.25d, 3.5m, 4UL, 5L, 6U, (byte)7, (sbyte)-8, (short)9, (ushort)10, 11), "class C\n{\n    object F0 = 1.5F;\n\n    object F1 = 2.25D;\n\n    object F2 = 3.5M;\n\n    object F3 = 4UL;\n\n    object F4 = 5L;\n\n    object F5 = 6U;\n\n    object F6 = 7;\n\n    object F7 = -8;\n\n    object F8 = 9;\n\n    object F9 = 10;\n\n    object F10 = 11;\n}\n");
        yield return new("api.literal.text", () => Literals("a\\b\"c'd\ne\rf\tg\0h\u0001i" + (char)0x2028 + "j" + (char)0x2029 + "k" + (char)0xD800, 'x', '\'', '"', '\\', true, false, null), "class C\n{\n    object F0 = \"a\\\\b\\\"c'd\\ne\\rf\\tg\\0h\\u0001i\\u2028j\\u2029k\\ud800\";\n\n    object F1 = 'x';\n\n    object F2 = '\\'';\n\n    object F3 = '\"';\n\n    object F4 = '\\\\';\n\n    object F5 = true;\n\n    object F6 = false;\n\n    object F7 = null;\n}\n");
        yield return new("api.literal.unsupported", () => Cs.Literal(new object()), "throws ArgumentException: Unsupported literal type; use an explicit raw expression. (Parameter 'value')");
        yield return new("api.literal.float-nan", () => Cs.Literal(float.NaN), "throws ArgumentException: Non-finite float needs an explicit expression.");
        yield return new("api.literal.float-infinity", () => Cs.Literal(float.PositiveInfinity), "throws ArgumentException: Non-finite float needs an explicit expression.");
        yield return new("api.literal.double-nan", () => Cs.Literal(double.NaN), "throws ArgumentException: Non-finite double needs an explicit expression.");
        yield return new("api.literal.double-infinity", () => Cs.Literal(double.NegativeInfinity), "throws ArgumentException: Non-finite double needs an explicit expression.");

        // Expression and statement factories.
        yield return new("api.factories", () => InClass(new CsMethod(Cs.Void, "M")
        {
            Body = Cs.Body(
                Cs.Var("a", Cs.String("s")),
                Cs.Statement(Cs.Call(Cs.Member(Cs.Id("a"), "Trim"))),
                Cs.Statement(Cs.RawExpression("Run()")),
                Cs.RawStatement("Stop();"),
                Cs.Try(Cs.Body(Cs.Using(Cs.Id("r"), Cs.Body())), new[] { Cs.Catch(Cs.Type("System.Exception"), Cs.Body(Cs.Rethrow()), "e"), Cs.CatchAll(Cs.Body()) }),
                Cs.Using(Cs.Var("d", Cs.Id("r")), Cs.Body(Cs.Empty())),
                Cs.For(Cs.Body(Cs.Break())),
                Cs.Statement(Cs.AsyncLambda(Cs.Literal(1))))
        }), "class C\n{\n    void M()\n    {\n        var a = \"s\";\n        a.Trim();\n        Run();\n        Stop();\n        try\n        {\n            using (r)\n            {\n            }\n        }\n        catch (System.Exception e)\n        {\n            throw;\n        }\n        catch\n        {\n        }\n        using (var d = r)\n        {\n            ;\n        }\n        for (; ; )\n        {\n            break;\n        }\n        async () => 1;\n    }\n}\n");
        yield return new("api.raw-body", () => InClass(new CsMethod(Cs.Void, "M") { Body = Cs.RawBody("Run();") }), "class C\n{\n    void M()\n    {\n        Run();\n    }\n}\n");

        // Builders.
        yield return new("api.builder.file", () => Cs.Document()
            .Header("// header")
            .Namespace("N")
            .Nullable(NullableMode.Enable)
            .Using("System")
            .Using("System.Text", isGlobal: true)
            .Attribute(Cs.Attribute("System.CLSCompliant", Cs.Arg(Cs.Literal(true))) with { Target = "assembly" })
            .When(true, f => f.Using("System.IO"))
            .When(false, f => f.Using("System.Net"))
            .AddRange(new[] { new CsTypeDeclaration(TypeKind.Interface, "I") })
            .Class("C").Public().Partial()
                .Attribute(Cs.Attribute("System.Serializable"))
                .Base(Cs.Type("B")).Generic("T", Cs.Constraint("class")).Generic(new TypeParameter("U"))
                .Field<int>("count").Private().ReadOnly().Initialize(Cs.Literal(1)).End()
                .Property<string>("Name").Public().Get().Set("private").End()
                .Property(Cs.Int, "Size").Public().Init().End()
                .Property(Cs.Int, "Twice").Public().Expression(Cs.Literal(2)).End()
                .Property(Cs.Int, "Seed").Public().Get().Initialize(Cs.Literal(3)).End()
                .Method<int>("Get").Public().Static().Parameter<int>("x").Parameter(Cs.Int, "y", "in", Cs.Literal(0)).Generic("V").Expression(Cs.Id("x")).End()
                .Method(Cs.Void, "Run").Protected().Virtual().Body(Cs.Return()).End()
                .Method(Cs.Void, "Raw").Internal().RawBody("Run();").End()
                .Method(Cs.Void, "Tree").Body(Cs.ParseTree("Run();").Root).End()
                .Method(Cs.Void, "Ext").Extern().Static().End()
                .Method(Cs.Void, "Parametered").Parameter(new CsParameter(Cs.Int, "z")).Body(Cs.Body()).End()
                .Constructor().Public().Parameter(Cs.Int, "x").Parameter(new CsParameter(Cs.Int, "y") { Modifier = "in" }).This().Body(Cs.Return()).End()
                .Constructor().Private().Base(Cs.Literal(1)).RawBody("Run();").End()
                .Event(Cs.Type("System.Action"), "Changed").Public().End()
                .Event(Cs.Type("System.Action"), "Custom").Add(Cs.Body()).Remove(Cs.Body()).End()
                .Event(Cs.Type("System.Action"), "Initialized").Initialize(Cs.Literal(null)).End()
                .Class("Nested").Sealed().New().End()
                .Struct("S").ReadOnly().End()
                .Record("R").Parameter(Cs.Int, "X").End()
                .RecordClass("RC").Abstract().End()
                .RecordStruct("RS").End()
                .Interface("J").Internal().End()
                .Enum("E").EnumMember("A", Cs.Literal(1)).EnumMember("B").End()
                .Type(TypeKind.Class, "K").Unsafe().Static().End()
                .AddRange(new CsMember[] { new CsField(Cs.Int, "extra") { Modifiers = new[] { "volatile" } } })
                .Field<int>("constant").Const().Initialize(Cs.Literal(4)).End()
                .Method(Cs.Void, "Async").Async().Override().Body(Cs.Body()).End()
                .Property(Cs.Int, "Required").Required().Get().Set().End()
            .End()
            .Struct("S").Ref().End()
            .Record("R").End()
            .RecordClass("RC").End()
            .RecordStruct("RS").End()
            .Interface("J").End()
            .Enum("E").End()
            .Build(), "// header\n\n#nullable enable\n\nglobal using System.Text;\nusing System.IO;\nusing System;\n\n[assembly: System.CLSCompliant(true)]\n\nnamespace N;\n\ninterface I\n{\n}\n\n[System.Serializable]\npublic partial class C<T, U> : B\n    where T : class\n{\n    private readonly int count = 1;\n\n    public string Name { get; private set; }\n\n    public int Size { init; }\n\n    public int Twice => 2;\n\n    public int Seed { get; } = 3;\n\n    public static int Get<V>(int x, in int y = 0) => x;\n\n    protected virtual void Run()\n    {\n        return;\n    }\n\n    internal void Raw()\n    {\n        Run();\n    }\n\n    void Tree()\n    {\n        Run();\n    }\n\n    static extern void Ext();\n\n    void Parametered(int z)\n    {\n    }\n\n    public C(int x, in int y) : this()\n    {\n        return;\n    }\n\n    private C() : base(1)\n    {\n        Run();\n    }\n\n    public event System.Action Changed;\n\n    event System.Action Custom\n    {\n        add\n        {\n        }\n        remove\n        {\n        }\n    }\n\n    event System.Action Initialized = null;\n\n    new sealed class Nested\n    {\n    }\n\n    readonly struct S\n    {\n    }\n\n    record R(int X)\n    {\n    }\n\n    abstract record class RC\n    {\n    }\n\n    record struct RS\n    {\n    }\n\n    internal interface J\n    {\n    }\n\n    enum E\n    {\n        A = 1,\n        B,\n    }\n\n    static unsafe class K\n    {\n    }\n\n    volatile int extra;\n\n    const int constant = 4;\n\n    override async void Async()\n    {\n    }\n\n    required int Required { get; set; }\n}\n\nref struct S\n{\n}\n\nrecord R\n{\n}\n\nrecord class RC\n{\n}\n\nrecord struct RS\n{\n}\n\ninterface J\n{\n}\n\nenum E\n{\n}\n");
        yield return new("api.builder.render", () => Cs.Document().Class("C").End().Render(), "class C\n{\n}\n");
        yield return new("api.builder.implicit", () => { CsDocument document = Cs.Document(CsLanguageVersion.CSharp12).Struct("S"). End(); return document; }, "struct S\n{\n}\n");
        yield return new("api.builder.when", () => Cs.Document().Class("C").When(true, t => t.Public()).When(false, t => t.Static()).End().Build(), "public class C\n{\n}\n");
        yield return new("api.builder.old-nullable", () => Cs.Document(CsLanguageVersion.CSharp7_3).Nullable(NullableMode.Enable), "throws GenValidationException: GEN073 NullableContext: Nullable context requires C# 8.");
        yield return new("api.builder.old-nullable-omit", () => Cs.Document(CsLanguageVersion.CSharp7_3).Nullable(NullableMode.Omit).Build(), "\n");
        yield return new("api.builder.old-global-using", () => Cs.Document(CsLanguageVersion.CSharp9).Using("System", isGlobal: true), "throws GenValidationException: GEN073 Usings: Global using requires C# 10.");
        yield return new("api.builder.unsupported-version", () => Cs.Document((CsLanguageVersion)1), "throws GenValidationException: GEN072 LanguageVersion: Unsupported C# language version. Supported: 7.3, 8, 9, 10, 11, 12, 13, 14, 15, preview.");
        yield return new("api.builder.old-init", () => Cs.Document(CsLanguageVersion.CSharp8).Class("C").Property(Cs.Int, "P").Init(), "throws GenValidationException: GEN073 Accessors: Init accessor requires C# 9.");
        yield return new("api.builder.old-file-modifier", () => Cs.Document(CsLanguageVersion.CSharp10).Class("C").Modifier("file"), "throws GenValidationException: GEN073 Types[0]: File-local type requires C# 11.");
        yield return new("api.builder.old-modifier-kept", () =>
        {
            var builder = Cs.Document(CsLanguageVersion.CSharp10).Class("C");
            try { builder.Modifier("file"); } catch (Validation.GenValidationException) { }
            return builder.End().Build();
        }, "class C\n{\n}\n");
        yield return new("api.builder.old-struct-primary", () => Cs.Document(CsLanguageVersion.CSharp11).Struct("S").Parameter(Cs.Int, "x").End().Build(), "throws GenValidationException: GEN073 Types[0]: Primary constructor requires C# 12.");
        yield return new("api.builder.old-generic", () => Cs.Document(CsLanguageVersion.CSharp12).Class("C").Generic("T", Cs.Constraint("allows ref struct")), "throws GenValidationException: GEN073 Types[0].TypeParameters[0].Constraints[0]: Allows ref struct constraint requires C# 13.");
        yield return new("api.builder.old-generic-parameter", () => Cs.Document(CsLanguageVersion.CSharp8).Class("C").Generic(new TypeParameter("T") { Constraints = new[] { Cs.Constraint("default") } }), "throws GenValidationException: GEN073 Types[0].TypeParameters[0].Constraints[0]: Default constraint requires C# 9.");
        yield return new("api.builder.old-base", () => Cs.Document(CsLanguageVersion.CSharp11).Class("C").Base(Cs.Type("B")).End().Build(), "class C : B\n{\n}\n");
        yield return new("api.builder.old-add-member", () => Cs.Document(CsLanguageVersion.CSharp12).Class("C").Add(new CsProperty(Cs.Int, "P") { Accessors = new[] { new CsAccessor("get"), new CsAccessor("set") }, Modifiers = new[] { "required" } }).End().Build(), "class C\n{\n    required int P { get; set; }\n}\n");
        yield return new("api.builder.old-add-type", () => Cs.Document(CsLanguageVersion.CSharp9).Add(new CsTypeDeclaration(TypeKind.RecordStruct, "R")), "throws GenValidationException: GEN073 Types[0].Kind: Record struct requires C# 10.");
        yield return new("api.builder.old-method-parameter", () => Cs.Document(CsLanguageVersion.CSharp11).Class("C").Method(Cs.Void, "M").Parameter(Cs.Int, "x", "ref readonly"), "throws GenValidationException: GEN073 Types[0].Members[0].Parameters[0].Modifier: Ref readonly parameter requires C# 12.");
        yield return new("api.builder.old-method-generic", () => Cs.Document(CsLanguageVersion.CSharp8).Class("C").Method(Cs.Void, "M").Generic("T", Cs.Constraint("default")), "throws GenValidationException: GEN073 Types[0].Members[0].TypeParameters[0].Constraints[0]: Default constraint requires C# 9.");
        yield return new("api.builder.old-method-body", () => Cs.Document(CsLanguageVersion.CSharp8).Class("C").Method(Cs.Void, "M").Body(Cs.Body(Cs.Statement(Cs.Assign(Cs.Id("x"), Cs.Id("y")) with { Operator = CsOperator.Coalesce }))).End().End().Build(), "class C\n{\n    void M()\n    {\n        x ??= y;\n    }\n}\n");
        yield return new("api.builder.old-event", () => Cs.Document(CsLanguageVersion.CSharp12).Interface("I").Event(Cs.Type("System.Action"), "E").Static().Abstract().Add(Cs.Body()).Remove(Cs.Body()).End().End().Build(), "invalid: GEN027 Abstract accessor cannot have a body.");
        yield return new("api.builder.old-event-remove", () => Cs.Document(CsLanguageVersion.CSharp12).Class("C").Event(Cs.Type("System.Action"), "E").Remove(Cs.Body()).Add(Cs.Body()).End().Build(), "CsTypeDeclaration");
        yield return new("api.builder.old-field", () => Cs.Document(CsLanguageVersion.CSharp10).Struct("S").Ref().Field(Cs.Int.Ref(), "F").Modifier("readonly"), "throws GenValidationException: GEN073 Types[0].Members[0]: Ref fields requires C# 11.");
        yield return new("api.builder.old-constructor", () => Cs.Document(CsLanguageVersion.CSharp12).Class("C").Constructor().Modifier("file").End().End().Build(), "invalid: GEN034 File-local type cannot be nested or have accessibility modifiers. | GEN031 Modifier 'file' is not valid for this construct.");
        yield return new("api.builder.old-type", () => Cs.Document(CsLanguageVersion.CSharp8).Record("R").End(), "throws GenValidationException: GEN073 Types[0].Kind: Record class requires C# 9.");
        yield return new("api.builder.old-nested-type", () => Cs.Document(CsLanguageVersion.CSharp12).Class("C").Class("N").End().End().Build(), "class C\n{\n    class N\n    {\n    }\n}\n");
        yield return new("api.builder.misdeclared-self", () => new Misdeclared().Public(), "throws InvalidOperationException: Builder 'Misdeclared' must use its own type as TSelf; 'FieldBuilder`1' was supplied.");
    }
}
