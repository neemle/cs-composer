using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;
using Neemle.CSharpComposer.Rendering;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-24 pass 2: parser branch contracts. Each row is a source and what parsing it must give: the coverage
// and, when the parse is not typed, the message of the first diagnostic. Rejected forms (malformed directives,
// bad escapes, invalid raw-string layout, misplaced modifiers, ...) are checked as carefully as accepted ones.
// Every source, typed or recovered, must render back byte for byte.
[Trait("Category", "Unit")]
public sealed class ParserBranchContractTests
{
    static string M(string body) => "class C\n{\n    object M()\n    {\n        " + body + "\n    }\n}\n";
    static string E(string expression) => M("return " + expression + ";");

    public static TheoryData<string, ParseCoverage, string?> Sources => new()
    {
        { "class C { object F = new C; }", ParseCoverage.Partial, "Constructor arguments or initializer expected." },
        { "class C { object F = new int[,] { { 1 } }; }", ParseCoverage.Typed, null },
        { "class C { object F = new int[,,] { }; }", ParseCoverage.Typed, null },
        { "class C { object F = new int[2][,]; }", ParseCoverage.Typed, null },
        { "class C { object F = new[][] { }; }", ParseCoverage.Partial, "Inferred arrays cannot specify additional ranks." },
        { "class C { object F = new int[] { 1 }; }", ParseCoverage.Typed, null },
        { E("(T)@x"), ParseCoverage.Typed, null },
        { E("(T)\"s\""), ParseCoverage.Typed, null },
        { E("(T)'c'"), ParseCoverage.Typed, null },
        { E("(T)$\"s\""), ParseCoverage.Typed, null },
        { E("(T).5"), ParseCoverage.Typed, null },
        { E("(T)!x"), ParseCoverage.Typed, null },
        { E("(T)~x"), ParseCoverage.Typed, null },
        { E("(T)(x)"), ParseCoverage.Typed, null },
        { E("(int)+x"), ParseCoverage.Typed, null },
        { E("(int)-x"), ParseCoverage.Typed, null },
        { E("(int)++x"), ParseCoverage.Typed, null },
        { E("(int)--x"), ParseCoverage.Typed, null },
        { E("(int)&x"), ParseCoverage.Typed, null },
        { E("(int)*x"), ParseCoverage.Typed, null },
        { E("(int[])[1]"), ParseCoverage.Typed, null },
        { E("(T) as U"), ParseCoverage.Typed, null },
        { E("(T) is U"), ParseCoverage.Typed, null },
        { E("(T)_x"), ParseCoverage.Typed, null },
        { E("(T)1"), ParseCoverage.Typed, null },
        { E("(T)+x"), ParseCoverage.Typed, null },
        { "/// <see cref=\"M(int) x\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"C.implicit operator int\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"explicit operator checked int(C)\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"operator @\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"operator +(C)\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"operator checked +\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"this\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"C.this[int]\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"int[]\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"int[,][]\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"M[0]\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <see cref=\"extension(int).M\"/>\nclass C { }", ParseCoverage.Typed, null },
        { "static class E { extension(int) { } }", ParseCoverage.Typed, null },
        { "interface I { static abstract implicit operator int(I x); }", ParseCoverage.Typed, null },
        { "class C : I { static implicit I.operator int(C x) => 0; }", ParseCoverage.Typed, null },
        { "class C : I { static explicit I.M int(C x) => 0; }", ParseCoverage.Partial, "Explicit interface operator expected." },
        { "class C { public static C operator @(C a) => a; }", ParseCoverage.Partial, "Overloadable operator expected." },
        { "class C : A::B.I { void A::B.I.M() { } }", ParseCoverage.Typed, null },
        { "class C : N.I<int> { void N.I<int>.M() { } }", ParseCoverage.Typed, null },
        { "class C : I<int> { void I<int>.M() { } }", ParseCoverage.Typed, null },
        { "class C : I { int I<.M() { } }", ParseCoverage.Partial, "Identifier expected." },
        { "  #region x\nclass C { }\n#endregion", ParseCoverage.Typed, null },
        { "class C { } #region x\n#endregion\n", ParseCoverage.Opaque, "A directive must begin its line." },
        { "#pragma\nclass C { }", ParseCoverage.Partial, "Unknown preprocessor directive." },
        { "#pragma warning\nclass C { }", ParseCoverage.Partial, "Directive action expected." },
        { "#pragma warning disable\nclass C { }", ParseCoverage.Typed, null },
        { "#:sdk Microsoft.NET.Sdk\nclass C { }", ParseCoverage.Typed, null },
        { "#sdk x\nclass C { }", ParseCoverage.Partial, "Invalid file-based directive prefix." },
        { "#:region x\nclass C { }", ParseCoverage.Partial, "Invalid file-based directive prefix." },
        { "#define A // c\n#define B /* d */\nclass C { }", ParseCoverage.Typed, null },
        { "#if A B\n#endif\nclass C { }", ParseCoverage.Opaque, "Invalid preprocessor condition." },
        { "#nullable\nclass C { }", ParseCoverage.Partial, "Directive action expected." },
        { "#nullable Enable\nclass C { }", ParseCoverage.Partial, "Directive action expected." },
        { "#nullable bogus\nclass C { }", ParseCoverage.Partial, "Directive action expected." },
        { "#pragma warning restore CS1, CS2\nclass C { }", ParseCoverage.Typed, null },
        { "#pragma checksum \"a\" \"b\"\nclass C { }", ParseCoverage.Partial, "Checksum requires a file, algorithm and checksum." },
        { "#pragma checksum \"a\" \"{b}\" \"c\"\nclass C { }", ParseCoverage.Opaque, "Invalid checksum directive." },
        { "#line (1, 2) - (3, 4) 5 \"f\"\nclass C { }", ParseCoverage.Typed, null },
        { "#line (1, 2) - (3, 4) \"f\"\nclass C { }", ParseCoverage.Typed, null },
        { "#line (1, 2) - (3, 4) \"f\" x\nclass C { }", ParseCoverage.Partial, "Unexpected line directive arguments." },
        { "#line (1, 2) - (3, 4)\nclass C { }", ParseCoverage.Partial, "Incomplete line directive." },
        { "#line (x, 2) - (3, 4) \"f\"\nclass C { }", ParseCoverage.Partial, "Line directive requires an integer." },
        { "#line (1 2) - (3, 4) \"f\"\nclass C { }", ParseCoverage.Partial, "Invalid line directive." },
        { "#line 5\nclass C { }", ParseCoverage.Typed, null },
        { "#line 5 \"f\"\nclass C { }", ParseCoverage.Typed, null },
        { "#line 5 \"f\" x\nclass C { }", ParseCoverage.Partial, "Unexpected line directive arguments." },
        { "#line hidden\nclass C { }", ParseCoverage.Typed, null },
        { "#line default\nclass C { }", ParseCoverage.Typed, null },
        { "#r \"a\"\nclass C { }", ParseCoverage.Typed, null },
        { "#r a\nclass C { }", ParseCoverage.Partial, "Directive requires a quoted string." },
        { "#r \"\nclass C { }", ParseCoverage.Partial, "Directive requires a quoted string." },
        { "#r \"a\\q\"\nclass C { }", ParseCoverage.Partial, "Invalid directive string escape." },
        { "#load \"\\u0041\"\nclass C { }", ParseCoverage.Typed, null },
        { "#r a\"\nclass C { }", ParseCoverage.Partial, "Directive requires a quoted string." },
        { E("nameof(x)"), ParseCoverage.Typed, null },
        { E("+x"), ParseCoverage.Typed, null },
        { E("~x"), ParseCoverage.Typed, null },
        { E("^1"), ParseCoverage.Typed, null },
        { E("a?.b"), ParseCoverage.Typed, null },
        { E("a?[0]"), ParseCoverage.Typed, null },
        { E("a?x"), ParseCoverage.Partial, "Expected ':'." },
        { E("a ? b : c"), ParseCoverage.Typed, null },
        { M("a -= 1;"), ParseCoverage.Typed, null },
        { M("a *= 1;"), ParseCoverage.Typed, null },
        { M("a /= 1;"), ParseCoverage.Typed, null },
        { M("a %= 1;"), ParseCoverage.Typed, null },
        { M("a ^= 1;"), ParseCoverage.Typed, null },
        { M("a |= 1;"), ParseCoverage.Typed, null },
        { M("a &= 1;"), ParseCoverage.Typed, null },
        { M("a <<= 1;"), ParseCoverage.Typed, null },
        { M("a >>= 1;"), ParseCoverage.Typed, null },
        { M("a >>>= 1;"), ParseCoverage.Typed, null },
        { M("a ??= b;"), ParseCoverage.Typed, null },
        { E("x => a -= 1"), ParseCoverage.Typed, null },
        { E("x => a *= 1"), ParseCoverage.Typed, null },
        { E("x => a /= 1"), ParseCoverage.Typed, null },
        { E("x => a %= 1"), ParseCoverage.Typed, null },
        { E("x => a ^= 1"), ParseCoverage.Typed, null },
        { E("new"), ParseCoverage.Partial, "Identifier expected." },
        { E("typeof"), ParseCoverage.Partial, "Expected '('." },
        { E("default"), ParseCoverage.Typed, null },
        { E("this"), ParseCoverage.Typed, null },
        { E("base"), ParseCoverage.Typed, null },
        { E("delegate { }"), ParseCoverage.Typed, null },
        { E("delegate () => 1"), ParseCoverage.Partial, "Anonymous method requires a block." },
        { E("async async () => 1"), ParseCoverage.Partial, "Repeated async lambda modifier." },
        { E("static static () => 1"), ParseCoverage.Partial, "Repeated static lambda modifier." },
        { E("ref readonly int () => ref x"), ParseCoverage.Typed, null },
        { E("ref int () => ref x"), ParseCoverage.Typed, null },
        { E("(int a b) => a"), ParseCoverage.Partial, "Expected ')'." },
        { E("(a = 1) => a"), ParseCoverage.Typed, null },
        { E("(int a = 1) => a"), ParseCoverage.Typed, null },
        { E("([A] a) => a"), ParseCoverage.Typed, null },
        { "record class R;", ParseCoverage.Typed, null },
        { "record struct R;", ParseCoverage.Typed, null },
        { "class C\n{\n#if X\n    int a;\n#endif\n}", ParseCoverage.Typed, null },
        { "class C\n{\n#region r\n#endregion\n}", ParseCoverage.Typed, null },
        { "enum E { A = 1 B }", ParseCoverage.Opaque, "Enum may contain only enum members." },
        { "enum E { A, }", ParseCoverage.Typed, null },
        { "class C { delegate* <int> f; }", ParseCoverage.Typed, null },
        { "class C { delegate void D(); }", ParseCoverage.Typed, null },
        { "class C { ~C(); }", ParseCoverage.Partial, "Destructor requires a body." },
        { "class C { extern ~C(); }", ParseCoverage.Typed, null },
        { "class C { C(); }", ParseCoverage.Partial, "Constructor body required." },
        { "class C { partial C(); }", ParseCoverage.Typed, null },
        { "class C { extern C(); }", ParseCoverage.Typed, null },
        { "class C { event System.Action E => null; }", ParseCoverage.Partial, "Expression-bodied event is unsupported." },
        { E("checked(x)"), ParseCoverage.Typed, null },
        { E("ref x"), ParseCoverage.Typed, null },
        { E("sizeof(int)"), ParseCoverage.Typed, null },
        { E("typeof(List<>)"), ParseCoverage.Typed, null },
        { E("nameof(List<>.Count)"), ParseCoverage.Typed, null },
        { E("(var (a, b), c)"), ParseCoverage.Typed, null },
        { E("(var ((a, b), c), d)"), ParseCoverage.Typed, null },
        { E("F<int>(1)"), ParseCoverage.Typed, null },
        { E("F<int>)"), ParseCoverage.Partial, "Expected ';'." },
        { E("F<int>]"), ParseCoverage.Partial, "Expected ';'." },
        { E("F<int>}"), ParseCoverage.Partial, "Expected ';'." },
        { E("F<int>:"), ParseCoverage.Partial, "Expected ';'." },
        { E("F<int>;"), ParseCoverage.Typed, null },
        { E("F<int>,"), ParseCoverage.Partial, "Expected ';'." },
        { E("F<int>.x"), ParseCoverage.Typed, null },
        { E("F<int>?.x"), ParseCoverage.Typed, null },
        { E("F<int>?"), ParseCoverage.Partial, "Expression expected." },
        { E("F<int>==x"), ParseCoverage.Partial, "Expected ';'." },
        { E("F<int>!=x"), ParseCoverage.Typed, null },
        { E("F<int>|x"), ParseCoverage.Typed, null },
        { E("F<int>^x"), ParseCoverage.Typed, null },
        { E("F<int>&x"), ParseCoverage.Typed, null },
        { E("F<int>&&x"), ParseCoverage.Typed, null },
        { E("F<int>||x"), ParseCoverage.Typed, null },
        { E("F<int>=x"), ParseCoverage.Typed, null },
        { E("F<int> is x"), ParseCoverage.Typed, null },
        { E("F<int> as x"), ParseCoverage.Typed, null },
        { E("F<int>"), ParseCoverage.Typed, null },
        { E("F<int> + x"), ParseCoverage.Typed, null },
        { E("a > > b"), ParseCoverage.Partial, "Expected ';'." },
        { E("a >> b"), ParseCoverage.Typed, null },
        { E("a >>> b"), ParseCoverage.Typed, null },
        { E("x is int? ;"), ParseCoverage.Typed, null },
        { E("x is int?)"), ParseCoverage.Partial, "Expected ';'." },
        { E("(x is int?)"), ParseCoverage.Typed, null },
        { E("x is int? ]"), ParseCoverage.Partial, "Expected ';'." },
        { E("x is int? }"), ParseCoverage.Partial, "Expected ';'." },
        { E("x is int?, 1"), ParseCoverage.Partial, "Expected ';'." },
        { E("x is int? : 1"), ParseCoverage.Partial, "Expected ';'." },
        { E("x is int? && y"), ParseCoverage.Typed, null },
        { E("x is int? || y"), ParseCoverage.Typed, null },
        { E("x is int? == y"), ParseCoverage.Typed, null },
        { E("x is int? != y"), ParseCoverage.Typed, null },
        { E("x is int? & y"), ParseCoverage.Typed, null },
        { E("x is int? | y"), ParseCoverage.Typed, null },
        { E("x is int? ^ y"), ParseCoverage.Typed, null },
        { E("x is { : 1 }"), ParseCoverage.Partial, "Property pattern requires a member name." },
        { E("x is int when"), ParseCoverage.Partial, "Expected ';'." },
        { E("x switch { int with => 1 }"), ParseCoverage.Typed, null },
        { E("x is int and"), ParseCoverage.Partial, "Expression expected." },
        { E("x is int or"), ParseCoverage.Partial, "Expression expected." },
        { E("x is int is"), ParseCoverage.Partial, "Expression expected." },
        { E("x is int as"), ParseCoverage.Partial, "Identifier expected." },
        { E("x is int switch"), ParseCoverage.Partial, "Expected '{'." },
        { E("from x in y select"), ParseCoverage.Partial, "Expression expected." },
        { E("from x in y join z in w on a equals b select x"), ParseCoverage.Typed, null },
        { E("from x in y group x by x"), ParseCoverage.Typed, null },
        { E("from x in y"), ParseCoverage.Partial, "Query requires select or group." },
        { E("from x in y where x"), ParseCoverage.Partial, "Query requires select or group." },
        { E("from x in y orderby x ascending select x"), ParseCoverage.Typed, null },
        { M("try { }"), ParseCoverage.Partial, "try requires catch or finally." },
        { M("try ;"), ParseCoverage.Partial, "try requires a block." },
        { M("try { } catch ;"), ParseCoverage.Partial, "catch requires a block." },
        { M("try { } finally { }"), ParseCoverage.Typed, null },
        { M("if (a) ;"), ParseCoverage.Typed, null },
        { M("void L();"), ParseCoverage.Partial, "Expected ';'." },
        { M("extern void L();"), ParseCoverage.Typed, null },
        { M("int L() => 1;"), ParseCoverage.Typed, null },
        { "class C { void M() {\n#if X\n x();\n#endif\n} }", ParseCoverage.Typed, null },
        { "class C { void M() {\n#region r\n#endregion\n} }", ParseCoverage.Typed, null },
        { E("$\"{x:N2}\""), ParseCoverage.Typed, null },
        { E("$@\"{x:N2}\""), ParseCoverage.Typed, null },
        { E("$\"{x \n#if A\n}\""), ParseCoverage.Partial, "Directives cannot appear inside an interpolation." },
        { E("$\"{x y}\""), ParseCoverage.Partial, "Unexpected interpolation contents." },
        { E("\"\"\"\n  a\n  \"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\r\n  a\r\n  \"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\r  a\r  \"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\n  a  \"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\n  a\n x \"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\n\"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\n  a\n b\n  \"\"\""), ParseCoverage.Typed, null },
        { E("\"\"\"\n  a\n\n  \"\"\""), ParseCoverage.Typed, null },
        { E("\"\\\""), ParseCoverage.Partial, "Unterminated literal or comment." },
        { E("\"\\u12\""), ParseCoverage.Typed, null },
        { E("\"\\U0001F600\""), ParseCoverage.Typed, null },
        { E("\"\\U00110000\""), ParseCoverage.Typed, null },
        { E("\"\\x1\""), ParseCoverage.Typed, null },
        { E("\"\\x12345\""), ParseCoverage.Typed, null },
        { E("\"\\q\""), ParseCoverage.Typed, null },
        { E("\"\\a\\b\\f\\n\\r\\t\\v\\0\\'\\\"\\\\\""), ParseCoverage.Typed, null },
        { E("'\\u0041'"), ParseCoverage.Typed, null },
        { E("$\"{x:\\a\\b\\f\\n\\r\\t\\v\\0\\'\\\"\\\\}\""), ParseCoverage.Partial, "Unterminated literal or comment." },
        { E("$\"{x:\\u0041}\""), ParseCoverage.Typed, null },
        { E("$\"{x:\\U0001F600}\""), ParseCoverage.Typed, null },
        { E("$\"{x:\\U00110000}\""), ParseCoverage.Partial, "Invalid Unicode code point." },
        { E("$\"{x:\\x41}\""), ParseCoverage.Typed, null },
        { E("$\"{x:\\x}\""), ParseCoverage.Partial, "Invalid Unicode string escape." },
        { E("$\"{x:\\u12}\""), ParseCoverage.Partial, "Invalid Unicode string escape." },
        { E("$\"{x:\\q}\""), ParseCoverage.Partial, "Invalid string escape." },
        { E("$\"{x:\\\\\\}\""), ParseCoverage.Partial, "Incomplete string escape." },
        { E("$@\"{x:\"\"}\""), ParseCoverage.Typed, null },
        { E("$\"{x:{{}}}\""), ParseCoverage.Typed, null },
        { E("$\"\"\"{x:\\n}\"\"\""), ParseCoverage.Typed, null },
        { E("$\"{x,5:N}\""), ParseCoverage.Typed, null },
        { E("$\"{x,5}\""), ParseCoverage.Typed, null },
        { E("$\"\\q{x}\""), ParseCoverage.Partial, "Invalid string escape." },
        { E("$\"\\u12{x}\""), ParseCoverage.Partial, "Invalid Unicode string escape." },
        { E("$\"\\U00110000{x}\""), ParseCoverage.Partial, "Invalid Unicode code point." },
        { E("$\"\\U0001F600\\x41\\a\\b\\f\\n\\r\\t\\v\\0\\'\\\"\\\\{x}\""), ParseCoverage.Typed, null },
        { E("$\"\\u0041{x}\""), ParseCoverage.Typed, null },
        { E("$\"{{x}}\""), ParseCoverage.Typed, null },
        { E("$$\"\"\"{x}{{y}}\"\"\""), ParseCoverage.Typed, null },
        { E("$\"\\{x}\""), ParseCoverage.Partial, "Invalid string escape." },
        { E("$\"{x \\\"y}\""), ParseCoverage.Partial, "Unterminated literal or comment." },
        { M("switch (x) { case _: break; }"), ParseCoverage.Typed, null },
        { M("switch (x) { case 1: }"), ParseCoverage.Partial, "A switch section cannot fall through; use break, return, throw or goto." },
        { M("switch (x) { case 1: default: break; }"), ParseCoverage.Typed, null },
        { "[assembly: A]\n[B]\nclass C { }", ParseCoverage.Typed, null },
        { "[module: A]\nclass C { }", ParseCoverage.Typed, null },
        { "class C { void M(scoped ref int a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped in int a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped scoped a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped[] a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped? a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped* a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped.T a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped<int> a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped::T a) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped, int b) { } }", ParseCoverage.Partial, "Identifier expected." },
        { "class C { void M(scoped a, int b) { } }", ParseCoverage.Typed, null },
        { "class C { void M(scoped a = 1) { } }", ParseCoverage.Typed, null },
        { "static class E { static void M(this ref int a) { } }", ParseCoverage.Typed, null },
        { "static class E { static void M(this in int a) { } }", ParseCoverage.Typed, null },
        { "static class E { static void M(this scoped ref int a) { } }", ParseCoverage.Typed, null },
        { "class C<T> where T : class? { }", ParseCoverage.Typed, null },
        { "class C<T> where T : notnull { }", ParseCoverage.Typed, null },
        { "class C<T> where U : class { }", ParseCoverage.Partial, "Constraint has no matching type parameter." },
        { "class C<T> where T : unmanaged, default { }", ParseCoverage.Typed, null },
        { "class C { partial P<int> F; }", ParseCoverage.Partial, "Modifier 'partial' is not valid for this construct." },
        { "class C { async.X F; }", ParseCoverage.Typed, null },
        { "class C { partial::X F; }", ParseCoverage.Typed, null },
        { "class C { partial[] F; }", ParseCoverage.Typed, null },
        { "class C { partial? F; }", ParseCoverage.Typed, null },
        { "class C { partial* F; }", ParseCoverage.Typed, null },
        { "class C { required int F; }", ParseCoverage.Partial, "Modifier 'required' is not valid for this construct." },
        { "class C { file int F; }", ParseCoverage.Partial, "Only a top-level type can be file-local." },
        { "class C { partial C() { } }", ParseCoverage.Typed, null },
        { "class C { partial union U { } }", ParseCoverage.Partial, "Property needs an expression or accessors." },
        { "using X = ;\nclass C { }", ParseCoverage.Partial, "Identifier expected." },
        { "using static;\nclass C { }", ParseCoverage.Partial, "Identifier expected." },
        { "#if X\nclass A { }\n#endif\nclass C { }", ParseCoverage.Typed, null },
        { "global using X;", ParseCoverage.Typed, null },
        { "extern alias A;", ParseCoverage.Typed, null },
        { "/** <summary>x</summary> */\nclass C { }", ParseCoverage.Typed, null },
        { "///<summary>x</summary>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <!DOCTYPE x>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <a><![CDATA[x]]><!-- c --><?p d?></a>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <a>\nclass C { }", ParseCoverage.Typed, null },
        { "/// <a b:c=\"d\" xmlns:b=\"u\"/>\nclass C { }", ParseCoverage.Typed, null },
        { E("1e5"), ParseCoverage.Typed, null },
        { E("1E+5"), ParseCoverage.Typed, null },
        { E("1e-5"), ParseCoverage.Typed, null },
        { E("1.5e5"), ParseCoverage.Typed, null },
        { E("1e_5"), ParseCoverage.Typed, null },
        { E("0x_FF"), ParseCoverage.Typed, null },
        { E("0b_1"), ParseCoverage.Typed, null },
        { E("0X1"), ParseCoverage.Typed, null },
        { E("0B1"), ParseCoverage.Typed, null },
        { E("1ul"), ParseCoverage.Typed, null },
        { E("1LU"), ParseCoverage.Typed, null },
        { E("1Lu"), ParseCoverage.Typed, null },
        { E("1uL"), ParseCoverage.Typed, null },
        { E("1f"), ParseCoverage.Typed, null },
        { E("1m"), ParseCoverage.Typed, null },
        { E("1d"), ParseCoverage.Typed, null },
        { E("$\"{/* c */x}\""), ParseCoverage.Typed, null },
        { E("$\"{x // c\n}\""), ParseCoverage.Typed, null },
        { E("$\"{(x)}\""), ParseCoverage.Typed, null },
        { E("$\"{new { a = 1 }}\""), ParseCoverage.Typed, null },
        { E("$\"{x"), ParseCoverage.Partial, "Unterminated literal or comment." },
        { E("$$\"\"\"{{x}}\"\"\""), ParseCoverage.Typed, null },
        { E("@$\"x\""), ParseCoverage.Typed, null },
        { E("$@\"{x}\""), ParseCoverage.Typed, null },
        { E("@x"), ParseCoverage.Typed, null },
        { "class C { } /* x", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "class C { }\n  #if X\n class D { }\n  #endif\n", ParseCoverage.Typed, null },
        { E("\"\"\"a\"\"\""), ParseCoverage.Typed, null },
        { E("u8\"\"u8"), ParseCoverage.Partial, "Expected ';'." },
        { E("$\"{\"a\"}\""), ParseCoverage.Typed, null },
        { E("$\"{'a'}\""), ParseCoverage.Typed, null },
        { "#if A\n#elif B\nclass C { }\n#else\n#endif", ParseCoverage.Typed, null },
        { "#if (A == B)\nclass C { }\n#endif", ParseCoverage.Typed, null },
        { "#if A != B\nclass C { }\n#endif", ParseCoverage.Typed, null },
        { "#if A && B || !C\nclass C { }\n#endif", ParseCoverage.Typed, null },
        { "#elif A\nclass C { }", ParseCoverage.Opaque, "Elif requires a preceding if and cannot follow else." },
        { "#else\nclass C { }", ParseCoverage.Opaque, "Else requires a preceding if and cannot repeat." },
        { "#endif\nclass C { }", ParseCoverage.Opaque, "Endif has no matching if." },
        { "#if true\n#elif true\n#else\n#endif\nclass C { }", ParseCoverage.Typed, null },
        { "for (int i = 0, j = 1; i < j; i++) { }", ParseCoverage.Typed, null },
        { "{ F(); }", ParseCoverage.Typed, null },
        { "using (var a = b) { }", ParseCoverage.Typed, null },
        { "using var a = b;", ParseCoverage.Typed, null },
        { "await using var a = b;", ParseCoverage.Typed, null },
        { "using (a) { }", ParseCoverage.Typed, null },
        { "using (var a = b, c = d) { }", ParseCoverage.Typed, null },
        { "label: F();", ParseCoverage.Typed, null },
        { "switch (x) { }", ParseCoverage.Typed, null },
        { "lock (x) { }", ParseCoverage.Typed, null },
        { "goto a;", ParseCoverage.Typed, null },
        { "yield return 1;", ParseCoverage.Typed, null },
        { "checked { }", ParseCoverage.Typed, null },
        { "unsafe { }", ParseCoverage.Typed, null },
        { "fixed (int* p = &x) { }", ParseCoverage.Typed, null },
        { "foreach (var (a, b) in c) { }", ParseCoverage.Typed, null },
        { "for (a = 1, b = 2; ; ) { }", ParseCoverage.Typed, null },
        { "for (; ; ) { }", ParseCoverage.Typed, null },
        { "int[] a = { 1 };", ParseCoverage.Typed, null },
        { "const int a = 1;", ParseCoverage.Typed, null },
        { "ref int a = ref b;", ParseCoverage.Typed, null },
        { "int a, b = 1;", ParseCoverage.Typed, null },
        { "#if X\nclass A { a # b }\n#endif\nclass C { }", ParseCoverage.Typed, null },
        { "var s = @@x;", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "var s = $$x;", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "var s = @$", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "var s = $\"{x", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "var s = $\"{x /* c", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "var s = $\"{x // c", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "}\nclass C { }", ParseCoverage.Partial, "Expression expected." },
        { "class C { } }", ParseCoverage.Partial, "Expression expected." },
        { "namespace N\n{\n#region r\n    class C { }\n#endregion\n}", ParseCoverage.Typed, null },
        { "namespace N\n{\n#if X\n    class D { }\n#endif\n    class C { }\n}", ParseCoverage.Typed, null },
        { "#if A\nclass C { }\n#elif B\nclass D { }\n#elif C\n#else\n#endif", ParseCoverage.Typed, null },
        { "#if A\n#else\n#else\n#endif", ParseCoverage.Opaque, "Else requires a preceding if and cannot repeat." },
        { "#if X\n#if Y\n#endif\n#else\nclass C { }\n#endif", ParseCoverage.Typed, null },
        { "#define A\n#if A\n#undef A\n#endif\n#if A\nclass C { }\n#endif", ParseCoverage.Typed, null },
        { "#if X\n#define A\n#undef B\n#endif\nclass C { }", ParseCoverage.Typed, null },
        { "class C { void M() { for (int i = 0, ; ; ) { } } }", ParseCoverage.Partial, "Expected ';'." },
        { "class C { void M() { for (int i = 0 j; ; ) { } } }", ParseCoverage.Partial, "Expected ';'." },
        { "class C { void M() { for (int i; ; ) { } } }", ParseCoverage.Typed, null },
        { "class C { object M() => from x in y select x into z select z; }", ParseCoverage.Typed, null },
        { "class C { object M() => from x in y join z in w on a equals b into g select g; }", ParseCoverage.Typed, null },
        { "class C { object M() => from x in y let z = x select z; }", ParseCoverage.Typed, null },
        { "class C { object M() => from int x in y select x; }", ParseCoverage.Typed, null },
        { "class C { object M() => from x in y orderby x descending, y select x; }", ParseCoverage.Typed, null },
        { "class C { void M() { int P { get; } } }", ParseCoverage.Partial, "Expected ';'." },
        { "class C { void M() { int x; } }", ParseCoverage.Typed, null },
        { "class C { void M() { if (a) { } else { } } }", ParseCoverage.Typed, null },
        { "class C { void M() { if (a) F(); } }", ParseCoverage.Typed, null },
        { "class C { object M() => typeof(List<>[]); }", ParseCoverage.Typed, null },
        { "class C { object M() => typeof(List<int>.E<>); }", ParseCoverage.Typed, null },
        { "class C { object M() => nameof(A<>.B<int>); }", ParseCoverage.Typed, null },
        { "class C { object M() => nameof(x.y); }", ParseCoverage.Typed, null },
        { "class C { object M() => sizeof(x); }", ParseCoverage.Typed, null },
        { "class C { object M() => stackalloc int[1]; }", ParseCoverage.Typed, null },
        { "class C { object M() => unchecked(x); }", ParseCoverage.Typed, null },
        { "class C { object M() => delegate ;", ParseCoverage.Partial, "Anonymous method requires a block." },
        { "class C { object M() => async delegate { }; }", ParseCoverage.Typed, null },
        { "class C { object M() => async x => x; }", ParseCoverage.Typed, null },
        { "class C { object M() => static x => x; }", ParseCoverage.Typed, null },
        { "class C { object M() => [A] x => x; }", ParseCoverage.Typed, null },
        { "class C { object M() => (int x) => x; }", ParseCoverage.Typed, null },
        { "class C { object M() => int (x) => x; }", ParseCoverage.Typed, null },
        { "class C { object M() => (int, int) (x) => x; }", ParseCoverage.Typed, null },
        { "class C { object M() => (ref int a) => a; }", ParseCoverage.Typed, null },
        { "/// <a>" + string.Concat(Enumerable.Repeat("<a>", 130)) + string.Concat(Enumerable.Repeat("</a>", 130)) + "</a>\nclass C { }", ParseCoverage.Typed, null },
        { "class C\n{\n    /// x\r\n    /// y\n    void M() { } // t\r\n}\r\n", ParseCoverage.Typed, null },
        { "class C\r\n{\r\n    // a\r\n\r\n    // b\r\n    void M() { }\r\n}\r\n", ParseCoverage.Typed, null },
        { "class C\n{\n    void M() { } /* a */ // b\n}\n", ParseCoverage.Typed, null },
        { "x = 1; // t\r\ny = 2;\r\n", ParseCoverage.Typed, null },
        { "{\n}\n", ParseCoverage.Typed, null },
        { "for (var i = 0; ; ) { }", ParseCoverage.Typed, null },
        { "for (int i; ; ) { }", ParseCoverage.Typed, null },
        { "foreach (int x in y) { }", ParseCoverage.Typed, null },
        { "while (x) { }", ParseCoverage.Typed, null },
        { "do { } while (x);", ParseCoverage.Typed, null },
        { "return;", ParseCoverage.Typed, null },
        { "throw x;", ParseCoverage.Typed, null },
        { "throw;", ParseCoverage.Partial, "Rethrow requires an enclosing catch outside finally." },
        { "break;", ParseCoverage.Partial, "Break requires an enclosing loop or switch outside finally." },
        { "continue;", ParseCoverage.Partial, "Continue requires an enclosing loop outside finally." },
        { ";", ParseCoverage.Typed, null },
        { "x => x;", ParseCoverage.Partial, "This symbol is a value; it needs an expression context, not a standalone statement." },
        { "F(delegate { });", ParseCoverage.Typed, null },
        { "F(async () => 1);", ParseCoverage.Typed, null },
        { "F(\"s\");", ParseCoverage.Typed, null },
        { "F($\"s{x}\");", ParseCoverage.Typed, null },
        { "F(1..2);", ParseCoverage.Typed, null },
        { "F(..);", ParseCoverage.Typed, null },
        { "F(x is int);", ParseCoverage.Typed, null },
        { "F(new { a = 1 });", ParseCoverage.Typed, null },
        { "F((1, 2));", ParseCoverage.Typed, null },
        { "F([1]);", ParseCoverage.Typed, null },
        { "F(default);", ParseCoverage.Typed, null },
        { "F(this);", ParseCoverage.Typed, null },
        { "F(sizeof(int));", ParseCoverage.Typed, null },
        { "F(typeof(int));", ParseCoverage.Typed, null },
        { "F(from x in y select x);", ParseCoverage.Typed, null },
        { "F(x switch { _ => 1 });", ParseCoverage.Typed, null },
        { "F(stackalloc int[1]);", ParseCoverage.Typed, null },
        { "var (a, b) = (1, 2);", ParseCoverage.Typed, null },
        { "goto case 1;", ParseCoverage.Partial, "Switch jump requires an enclosing switch outside finally." },
        { "goto default;", ParseCoverage.Partial, "Switch jump requires an enclosing switch outside finally." },
        { "yield break;", ParseCoverage.Typed, null },
        { "label: ;", ParseCoverage.Typed, null },
        { "try { } catch { }", ParseCoverage.Typed, null },
        { "switch (x) { case 1: break; }", ParseCoverage.Typed, null },
        { "int L() => 1;", ParseCoverage.Typed, null },
        { "class C { #region x\n}", ParseCoverage.Partial, "A directive must begin its line." },
        { "class C\n{\n#pragma\n}", ParseCoverage.Partial, "Unknown preprocessor directive." },
        { "class C { void M() { #region x\n} }", ParseCoverage.Partial, "A directive must begin its line." },
        { "class C\n{\n    void M()\n    {\n#pragma\n    }\n}", ParseCoverage.Partial, "Unknown preprocessor directive." },
        { "var s = $;", ParseCoverage.Partial, "Expected ';'." },
        { "var s = $ x;", ParseCoverage.Partial, "Expected ';'." },
        { "class C { string F = $$x; }", ParseCoverage.Partial, "Unterminated literal or comment." },
        { "class C { object M() => c ? ref a : ref b; }", ParseCoverage.Typed, null },
        { "class C { void M() { ref int r = ref x; } }", ParseCoverage.Typed, null },
        { "class C { object M() => 0b10 + 0xAb; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\n  a\n x \"\"\"; }", ParseCoverage.Partial, "Raw string closing delimiter must occupy its own line." },
        { "class C { object M() => $\"\"\"\n  a{x}\n  \"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\r\n  a{x}\r\n  \"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\r  a{x}\r  \"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\n\"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\n  a\n b{x}\n  \"\"\"; }", ParseCoverage.Partial, "Raw string indentation is inconsistent." },
        { "class C { object M() => $\"\"\"\n  a{x}\n\n  \"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\n\t a{x}\n\t \"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\n  a{x}\n\t\"\"\"; }", ParseCoverage.Partial, "Raw string indentation is inconsistent." },
        { "class C { object M() => $\"\"\"\n  a{x}\"\"\"; }", ParseCoverage.Partial, "Raw string closing delimiter must occupy its own line." },
        { "class C { object M() => $\"\"\"a{x}\"\"\"; }", ParseCoverage.Typed, null },
        { "class C { object M() => $\"\"\"\n  a{x}\n  \r\n  \"\"\"; }", ParseCoverage.Typed, null },
        { "#if X\n#if Y\n#else\n#endif\n#endif\nclass C { }", ParseCoverage.Typed, null },
        { "#if X\nclass A { }\n\t#endif\nclass C { }", ParseCoverage.Typed, null },
        { "static class E { static void M(this int a) { } }", ParseCoverage.Typed, null },
        { "class C { ref int M() => ref x; }", ParseCoverage.Typed, null },
        { "class C { void M() { F(x => ref y); } }", ParseCoverage.Typed, null },
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void Source_parses_to_the_expected_outcome_and_renders_back(string source, ParseCoverage coverage, string? message)
    {
        var parsed = Cs.Parse(source);
        Assert.Equal(coverage, parsed.Coverage);
        if (message == null) Assert.Empty(parsed.Diagnostics);
        else Assert.Equal(message, parsed.Diagnostics[0].Message);
        FormatCheck.FixedPoint(parsed.Root);
    }

    // Top-level statements through the symbol tree (Cs.ParseTree): typed, and rendered back unchanged.
    public static TheoryData<string> TreeSources => new()
    {
        "#if A\nclass C { }\n#elif B\nclass D { }\n#elif C\n#else\n#endif",
        "#define A\n#if A\n#undef A\n#endif\n#if A\nclass C { }\n#endif",
        "x = 1; // t\r\ny = 2;\r\n",
        "{\n}\n",
        "for (var i = 0; ; ) { }",
        "for (int i; ; ) { }",
        "foreach (int x in y) { }",
        "while (x) { }",
        "do { } while (x);",
        "return;",
        "throw x;",
        ";",
        "F(delegate { });",
        "F(async () => 1);",
        "F(\"s\");",
        "F($\"s{x}\");",
        "F(1..2);",
        "F(..);",
        "F(x is int);",
        "F(new { a = 1 });",
        "F((1, 2));",
        "F([1]);",
        "F(default);",
        "F(this);",
        "F(sizeof(int));",
        "F(typeof(int));",
        "F(from x in y select x);",
        "F(x switch { _ => 1 });",
        "F(stackalloc int[1]);",
        "var (a, b) = (1, 2);",
        "yield break;",
        "label: ;",
        "try { } catch { }",
        "switch (x) { case 1: break; }",
        "int L() => 1;",
    };

    [Theory]
    [MemberData(nameof(TreeSources))]
    public void Statement_source_parses_to_a_typed_tree_and_renders_back(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Empty(parsed.Diagnostics);
        FormatCheck.FixedPoint(parsed.Root);
    }

    // A `$` that opens no string used to reach the interpolated-string reader and throw
    // ArgumentOutOfRangeException out of Cs.Parse. It is now a recovered statement or member.
    [Theory]
    [InlineData("var s = $$x;\n")]
    [InlineData("var s = $;\n")]
    [InlineData("class C { string F = $$x; }")]
    [InlineData("class C { object M() => $x; }")]
    public void A_dollar_that_opens_no_string_is_recovered_not_thrown(string source)
    {
        var parsed = Cs.Parse(source);
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        Assert.NotEmpty(parsed.Diagnostics);
        FormatCheck.FixedPoint(parsed.Root);
    }

    static CsXmlAttribute CrefAttribute(string cref) =>
        Cs.Parse("/// <see cref=\"" + cref + "\"/>\nclass C { }").Root.Descendants().Select(n => n.Node).OfType<CsXmlAttribute>().Single();

    [Theory]
    [InlineData("C.implicit operator int", CrefKind.Conversion, "C", false, -1)]
    [InlineData("explicit operator checked int(C)", CrefKind.Conversion, null, true, 1)]
    [InlineData("operator +(C)", CrefKind.Operator, null, false, 1)]
    [InlineData("operator checked +", CrefKind.Operator, null, true, -1)]
    [InlineData("this", CrefKind.Indexer, null, false, -1)]
    [InlineData("C.this[int]", CrefKind.Indexer, "C", false, 1)]
    [InlineData("int[]", CrefKind.Name, null, false, -1)]
    [InlineData("int[,][]", CrefKind.Name, null, false, -1)]
    [InlineData("extension(int).M", CrefKind.Extension, null, false, 1)]
    public void A_cref_reads_its_kind_qualifier_check_and_parameters(string text, CrefKind kind, string? qualifier, bool isChecked, int parameters)
    {
        var reference = CrefAttribute(text).Reference;
        Assert.NotNull(reference);
        Assert.Equal(kind, reference.Kind);
        Assert.Equal(qualifier, reference.Qualifier?.Name);
        Assert.Equal(isChecked, reference.IsChecked);
        Assert.Equal(parameters, reference.Parameters?.Count ?? -1);
    }

    [Fact]
    public void Cref_array_ranks_and_members_are_kept()
    {
        Assert.Equal(TypeForm.Array, CrefAttribute("int[,][]").Reference!.Type!.Form);
        Assert.Equal(OverloadableOperator.Plus, CrefAttribute("operator +(C)").Reference!.Operator);
        Assert.Equal(OverloadableOperator.Implicit, CrefAttribute("C.implicit operator int").Reference!.Operator);
        Assert.Equal("int", CrefAttribute("C.implicit operator int").Reference!.Type!.Name);
        Assert.Equal("M", CrefAttribute("extension(int).M").Reference!.Member!.Type!.Name);
    }

    // Text that is not a whole cref (trailing tokens, an unknown operator, an element access) stays plain text.
    [Theory]
    [InlineData("M(int) x")]
    [InlineData("operator @")]
    [InlineData("M[0]")]
    public void A_partial_cref_stays_plain_attribute_text(string text)
    {
        var attribute = CrefAttribute(text);
        Assert.Null(attribute.Reference);
        Assert.Equal(text, attribute.Value);
    }

    [Theory]
    [InlineData("-=", CsOperator.Subtract)]
    [InlineData("*=", CsOperator.Multiply)]
    [InlineData("/=", CsOperator.Divide)]
    [InlineData("%=", CsOperator.Modulo)]
    [InlineData("^=", CsOperator.ExclusiveOr)]
    [InlineData("|=", CsOperator.BitwiseOr)]
    [InlineData("&=", CsOperator.BitwiseAnd)]
    [InlineData("<<=", CsOperator.LeftShift)]
    [InlineData(">>=", CsOperator.RightShift)]
    [InlineData(">>>=", CsOperator.UnsignedRightShift)]
    [InlineData("??=", CsOperator.Coalesce)]
    public void A_compound_assignment_in_a_lambda_body_reads_its_operator(string token, CsOperator expected)
    {
        var parsed = Cs.Parse(E("x => a " + token + " b"));
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        var assignment = parsed.Root.Descendants().Select(n => n.Node).OfType<CsExpression>().Single(e => e.Kind == ExpressionKind.Assignment);
        Assert.Equal(expected, assignment.Operator);
    }

    [Theory]
    [InlineData("+x", CsOperator.UnaryPlus)]
    [InlineData("-x", CsOperator.Negate)]
    [InlineData("!x", CsOperator.Not)]
    [InlineData("~x", CsOperator.Complement)]
    [InlineData("&x", CsOperator.AddressOf)]
    [InlineData("*x", CsOperator.Dereference)]
    [InlineData("^x", CsOperator.FromEnd)]
    public void A_prefix_operator_reads_its_operator(string expression, CsOperator expected)
    {
        var parsed = Cs.Parse(E(expression));
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        Assert.Equal(expected, parsed.Root.Descendants().Select(n => n.Node).OfType<CsExpression>().First(e => e.Kind == ExpressionKind.Unary).Operator);
    }

    // Each numeric form is one literal token, suffixes and separators included.
    [Theory]
    [InlineData("1e5")]
    [InlineData("1E+5")]
    [InlineData("1e-5")]
    [InlineData("1.5e5")]
    [InlineData("0x_FF")]
    [InlineData("0b_1")]
    [InlineData("0X1")]
    [InlineData("0B10")]
    [InlineData("0xAb")]
    [InlineData("1ul")]
    [InlineData("1LU")]
    [InlineData("1Lu")]
    [InlineData("1uL")]
    [InlineData("1f")]
    [InlineData("1m")]
    [InlineData("1d")]
    public void A_numeric_literal_is_one_token(string literal)
    {
        var parsed = Cs.Parse(E(literal));
        Assert.Equal(ParseCoverage.Typed, parsed.Coverage);
        var value = parsed.Root.Descendants().Select(n => n.Node).OfType<CsExpression>().Single(e => e.Kind == ExpressionKind.Literal);
        Assert.Equal(literal, value.Text);
    }

    // A file parsed for an older language version keeps the newer construct and reports the version limit.
    [Theory]
    [InlineData("record class R;", "Explicit record class syntax requires C# 10.")]
    [InlineData("class C { void M() { var f = static x => x; } }", "Natural lambda type inference requires C# 10.")]
    [InlineData("global using X;", "Global using requires C# 10.")]
    [InlineData("file class C { }", "File-local type requires C# 11.")]
    public void A_newer_construct_reports_the_language_version_it_needs(string source, string message)
    {
        var parsed = Cs.Parse(source, new ParseOptions { LanguageVersion = CsLanguageVersion.CSharp9 });
        Assert.Equal(ParseCoverage.Partial, parsed.Coverage);
        var diagnostic = Assert.Single(parsed.Diagnostics);
        Assert.Equal(("GEN073", DiagnosticSeverity.Error, message), (diagnostic.Code, diagnostic.Severity, diagnostic.Message));
        Assert.Empty(Cs.Parse(source).Diagnostics);
    }

    static readonly CsComment Note = new("// c");

    public static TheoryData<string, CsNode, ParseContext, Type, int> Fragments => new()
    {
        { "expression", new CsRaw("a + b"), new(ParseKind.Expression), typeof(CsExpression), 0 },
        { "expression with a comment", new CsRaw("a + b") { Comments = new[] { Note } }, new(ParseKind.Expression), typeof(CsExpression), 1 },
        { "member", new CsRaw("int F;"), new(ParseKind.Member), typeof(CsField), 0 },
        { "raw member in a struct", new CsRawMember("int F;"), new(ParseKind.Member, ContainingType: "S", ContainingKind: TypeKind.Struct), typeof(CsField), 0 },
        { "raw body", new CsBody { Raw = "F(); G();" }, new(ParseKind.Statements), typeof(CsBody), 0 },
        { "raw statement", new CsStatement(StatementKind.Raw, Text: "F();"), new(ParseKind.Statements), typeof(CsBody), 0 },
        { "statements with a comment", new CsRaw("F();") { Comments = new[] { Note } }, new(ParseKind.Statements), typeof(CsBody), 1 },
        { "raw expression", new CsExpression(ExpressionKind.Raw, "a", Values<CsExpression>.Empty), new(ParseKind.Expression), typeof(CsExpression), 0 },
        { "raw type", new CsType("int") { Form = TypeForm.Raw }, new(ParseKind.Expression), typeof(CsExpression), 0 },
        { "document", new CsRaw("class C { }"), new(ParseKind.Document), typeof(CsDocument), 0 },
        { "document with a comment", new CsRaw("class C { }") { Comments = new[] { Note } }, new(ParseKind.Document), typeof(CsDocument), 1 },
        // A fragment parse reports language-version limits only; `break` outside a loop is the caller's to validate.
        { "statement the full validator rejects", new CsRaw("break;"), new(ParseKind.Statements), typeof(CsBody), 0 },
    };

    [Theory]
    [MemberData(nameof(Fragments))]
    public void A_raw_node_parses_in_its_context(string name, CsNode raw, ParseContext context, Type expected, int comments)
    {
        var result = raw.Parse(context);
        Assert.True(result.Coverage == ParseCoverage.Typed && result.Diagnostics.Count == 0, name);
        Assert.IsAssignableFrom(expected, result.Root);
        Assert.Equal(comments, result.Root.Comments.Count);
    }

    [Theory]
    [InlineData("a +", ParseKind.Expression, CsLanguageVersion.CSharp15, "PARSE001", "Expression expected.")]
    [InlineData("a b", ParseKind.Expression, CsLanguageVersion.CSharp15, "PARSE001", "Unexpected trailing tokens.")]
    [InlineData("x is not null", ParseKind.Expression, CsLanguageVersion.CSharp7_3, "GEN073", "Type, relational and logical patterns requires C# 9.")]
    [InlineData("public required int F { get; init; }", ParseKind.Member, CsLanguageVersion.CSharp7_3, "GEN073", "Required member requires C# 11.")]
    [InlineData("var f = static x => x;", ParseKind.Statements, CsLanguageVersion.CSharp7_3, "GEN073", "Natural lambda type inference requires C# 10.")]
    public void A_rejected_fragment_keeps_the_raw_node(string text, ParseKind kind, CsLanguageVersion version, string code, string message)
    {
        var raw = new CsRaw(text);
        var result = raw.Parse(new ParseContext(kind, version));
        Assert.NotEqual(ParseCoverage.Typed, result.Coverage);
        Assert.Same(raw, result.Root);
        Assert.Equal((code, message), (result.Diagnostics[0].Code, result.Diagnostics[0].Message));
    }

    [Fact]
    public void A_too_deep_fragment_is_kept_raw_with_the_nesting_warning()
    {
        var raw = new CsRaw(new string('(', 200) + "a" + new string(')', 200));
        var result = raw.Parse(new ParseContext(ParseKind.Expression));
        Assert.Equal(ParseCoverage.Opaque, result.Coverage);
        Assert.Same(raw, result.Root);
        Assert.Equal(("PARSE002", "Nesting exceeds 128."), (result.Diagnostics[0].Code, result.Diagnostics[0].Message));
    }

    [Fact]
    public void Fragment_parse_rejects_a_typed_node_and_an_undefined_kind()
    {
        var typed = Assert.Throws<InvalidOperationException>(() => Cs.Id("a").Parse(new ParseContext(ParseKind.Expression)));
        Assert.Equal("Explicit Parse requires a raw node and a matching context.", typed.Message);
        var kind = Assert.Throws<ArgumentOutOfRangeException>(() => new CsRaw("a").Parse(new ParseContext((ParseKind)99)));
        Assert.Equal("context", kind.ParamName);
    }

    // Cs.ParseTree keeps source it cannot type (a declaration, a syntax error, a statement validation rejects, a
    // too-deep input) as one raw item. Rendering that tree used to throw SYM001 ("An expression value must be a
    // symbol or expression"); the untouched tree now renders its source byte for byte.
    [Theory]
    [InlineData("class C { }")]
    [InlineData("namespace N\r\n{\r\n    class C { }\r\n}\r\n")]
    [InlineData("F(;\n")]
    [InlineData("break;")]
    [InlineData("x => x;\n")]
    [InlineData("goto case 1;")]
    [InlineData("#if A\n#else\n#else\n#endif")]
    public void An_opaque_tree_renders_its_source_verbatim(string source)
    {
        var parsed = Cs.ParseTree(source);
        Assert.Equal(ParseCoverage.Opaque, parsed.Coverage);
        Assert.NotEmpty(parsed.Diagnostics);
        Assert.Equal(source, Assert.IsType<CsRaw>(Assert.Single(parsed.Root.Items)).Text);
        FormatCheck.FixedPoint(parsed.Root);
        Assert.Equal(source, parsed.Root.Render(new RenderOptions { Indent = "\t" }));
    }

    // An Opaque tree checks its render options exactly as a typed tree does: an invalid Indent is rejected on
    // both paths with the same error, instead of the Opaque tree ignoring it and returning its source. The error is
    // the public ArgumentException CsDocument.Render throws, never the internal InvalidModel (GENE-27 D9).
    [Theory]
    [InlineData("xx")]
    [InlineData(" x")]
    [InlineData(null)]
    public void An_opaque_tree_rejects_invalid_render_options_like_a_typed_tree(string? indent)
    {
        var opaque = Cs.ParseTree("class C { }");
        var typed = Cs.ParseTree("x = 1;");
        Assert.Equal(ParseCoverage.Opaque, opaque.Coverage);
        Assert.Equal(ParseCoverage.Typed, typed.Coverage);
        var options = new RenderOptions { Indent = indent! };
        var typedError = Assert.ThrowsAny<Exception>(() => typed.Root.Render(options));
        var opaqueError = Assert.ThrowsAny<Exception>(() => opaque.Root.Render(options));
        Assert.Equal(typedError.GetType(), opaqueError.GetType());
        Assert.Equal("options", Assert.IsType<ArgumentException>(opaqueError).ParamName);
        Assert.Equal("Indent must contain only spaces or tabs. (Parameter 'options')", opaqueError.Message);
        Assert.Equal(typedError.Message, opaqueError.Message);
    }

    [Fact]
    public void A_too_deep_tree_is_opaque_and_renders_its_source()
    {
        string source = "x = " + new string('(', 200) + "a" + new string(')', 200) + ";";
        var parsed = Cs.ParseTree(source);
        Assert.Equal(ParseCoverage.Opaque, parsed.Coverage);
        FormatCheck.FixedPoint(parsed.Root);
    }
}
