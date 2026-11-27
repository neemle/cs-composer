using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Parsing of source text: lexical forms, directives, strings, declarations, statements, expressions and patterns,
// recovery of what the parser or validation rejects, nesting past the limit, and explicit fragment parses.
public static class ParserScenarios
{
    static ParseResult<CsDocument> P(string source, CsLanguageVersion version = CsLanguageVersion.CSharp15, params string[] symbols) =>
        Cs.Parse(source, new ParseOptions { LanguageVersion = version, PreprocessorSymbols = symbols });

    static ParseResult<CsDocument> Body(string statements, CsLanguageVersion version = CsLanguageVersion.CSharp15) =>
        P("class C { void M() { " + statements + " } }", version);

    static ParseResult<CsDocument> Member(string member, CsLanguageVersion version = CsLanguageVersion.CSharp15) => P("class C { " + member + " }", version);

    static string Repeat(string text, int count, string separator = "") => string.Join(separator, Enumerable.Repeat(text, count));

    // A long source in brief: coverage, diagnostics, the kinds of the top items and whether it renders back unchanged.
    static string Brief(string source) => Brief(source, Cs.Parse(source));

    static string Brief(string source, ParseResult<CsDocument> parsed) =>
        parsed.Coverage + "; " + string.Join(" | ", parsed.Diagnostics.Select(d => d.Code + " " + d.Message + " @" + d.Offset + "+" + d.Length)) +
        "; items " + string.Join(" ", parsed.Root.Items.Select(Kinds)) + "; " + (parsed.Root.Render() == source ? "round trip" : "changed");

    static string Kinds(CsNode node) => node switch
    {
        CsTypeDeclaration type => "type(" + string.Join(" ", type.Members.Select(m => m.GetType().Name)) + ")",
        CsNamespace space => "namespace(" + space.Items.Count + ")",
        _ => node.GetType().Name,
    };

    static string BriefTree(string source)
    {
        var parsed = Cs.ParseTree(source);
        return parsed.Coverage + "; " + string.Join(" | ", parsed.Diagnostics.Select(d => d.Code + " " + d.Message)) + "; items " +
            string.Join(" ", parsed.Root.Items.Select(i => i.GetType().Name)) + "; " + (parsed.Root.Render() == source ? "round trip" : "changed");
    }

    static ParseResult<CsNode> Fragment(CsNode raw, ParseKind kind, CsLanguageVersion version = CsLanguageVersion.CSharp15) =>
        raw.Parse(new ParseContext(kind, version));

    static string BriefFragment(string text, ParseKind kind)
    {
        var parsed = new CsRaw(text).Parse(new ParseContext(kind));
        return parsed.Coverage + "; " + string.Join(" | ", parsed.Diagnostics.Select(d => d.Code + " " + d.Message)) + "; " + parsed.Root.GetType().Name;
    }

    public static IEnumerable<Scenario> All()
    {
        // Lexical forms.
        yield return new("parse.lex.unterminated-string", () => P("class C { string s = \"abc; }"), "Partial; diagnostics: PARSE001 Error @21+7 1:22 Unterminated literal or comment. | PARSE002 Warning @10+18 1:11 Expected ';'. | PARSE001 Error @0+28 1:1 Expected '}'.; class C { string s = \"abc; }");
        yield return new("parse.lex.unterminated-comment", () => P("class C { } /* open"), "Partial; diagnostics: PARSE001 Error @12+7 1:13 Unterminated literal or comment.; class C { } /* open");
        yield return new("parse.lex.disabled-hash", () => P("#if X\nint a = b # c;\n#endif\nclass C { }\n"), "Typed; diagnostics: ; #if X\nint a = b # c;\n#endif\nclass C { }\n");
        yield return new("parse.lex.documentation-lines", () => P("/// <summary>\r\n   ///   a\r\n/// </summary>\r\nclass C { }\r\n"), "Typed; diagnostics: ; /// <summary>\r\n   ///   a\r\n/// </summary>\r\nclass C { }\r\n");
        yield return new("parse.lex.documentation-at-end", () => P("class C { }\n/// trailing"), "Typed; diagnostics: ; class C { }\n/// trailing");
        yield return new("parse.lex.documentation-cr", () => P("/// <summary>a</summary>\r   /// <remarks>b</remarks>\rclass C { }\r"), "Typed; diagnostics: ; /// <summary>a</summary>\r   /// <remarks>b</remarks>\rclass C { }\r");
        yield return new("parse.lex.numbers", () => Member("double[] F = { 0x1F_a, 0B1_0, 0b0, 1e10, 1.5E-3, 2e+5, 1_0e1_0, 3E, 1d, 2m, 3f, 4ul, 5LU, 6u, 7l };"), "Typed; diagnostics: ; class C { double[] F = { 0x1F_a, 0B1_0, 0b0, 1e10, 1.5E-3, 2e+5, 1_0e1_0, 3E, 1d, 2m, 3f, 4ul, 5LU, 6u, 7l }; }");
        yield return new("parse.lex.dollar-without-quote", () => P("class C { object F = $@x; }"), "Partial; diagnostics: PARSE001 Error @21+1 1:22 Unterminated literal or comment. | PARSE002 Warning @10+15 1:11 Expression expected.; class C { object F = $@x; }");
        yield return new("parse.lex.at-dollar-at-end", () => P("class C { } @$"), "Partial; diagnostics: PARSE001 Error @12+1 1:13 Unterminated literal or comment. | PARSE001 Error @12+2 1:13 Expected ';'.; class C { } @$");
        yield return new("parse.lex.interpolation-quote", () => Member("string F = $\"{ \"a\" + 'b' }\";"), "Typed; diagnostics: ; class C { string F = $\"{ \"a\" + 'b' }\"; }");
        yield return new("parse.lex.interpolation-open-quote", () => Member("string F = $\"{ \"a }\";"), "Partial; diagnostics: PARSE001 Error @21+12 1:22 Unterminated literal or comment. | PARSE002 Warning @10+23 1:11 Unterminated interpolation. | PARSE001 Error @0+33 1:1 Expected '}'.; class C { string F = $\"{ \"a }\"; }");
        yield return new("parse.lex.interpolation-comment", () => Member("string F = $\"{ /* c */ 1 }\";"), "Typed; diagnostics: ; class C { string F = $\"{ /* c */ 1 }\"; }");
        yield return new("parse.lex.interpolation-open-comment", () => Member("string F = $\"{ /* c 1 }\";"), "Partial; diagnostics: PARSE001 Error @21+16 1:22 Unterminated literal or comment. | PARSE002 Warning @10+27 1:11 Unterminated interpolation. | PARSE001 Error @0+37 1:1 Expected '}'.; class C { string F = $\"{ /* c 1 }\"; }");
        yield return new("parse.lex.interpolation-line-comment", () => Member("string F = $\"\"\"\n    { 1 // c\n    }\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = $\"\"\"\n    { 1 // c\n    }\n    \"\"\"; }");
        yield return new("parse.lex.interpolation-braces", () => Member("string F = $\"{ new { A = 1 } }\";"), "Typed; diagnostics: ; class C { string F = $\"{ new { A = 1 } }\"; }");
        yield return new("parse.lex.interpolation-open-hole", () => P("class C { string F = $\"{ 1"), "Partial; diagnostics: PARSE001 Error @21+5 1:22 Unterminated literal or comment. | PARSE002 Warning @10+16 1:11 Unterminated interpolation. | PARSE001 Error @0+26 1:1 Expected '}'.; class C { string F = $\"{ 1");
        yield return new("parse.lex.interpolation-escaped-braces", () => Member("string F = $\"{{a}} {1}\";"), "Typed; diagnostics: ; class C { string F = $\"{{a}} {1}\"; }");
        yield return new("parse.lex.raw-interpolation", () => Member("string F = $$\"\"\"{a} {{1}}\"\"\";"), "Typed; diagnostics: ; class C { string F = $$\"\"\"{a} {{1}}\"\"\"; }");
        yield return new("parse.lex.deep-interpolation", () => Brief("class C { string M() => " + Repeat("$\"{", 70) + "1" + Repeat("}\"", 70) + "; }"), "Partial; PARSE001 Unterminated literal or comment. @24+354 | PARSE002 Unterminated interpolation. @10+368 | PARSE001 Expected '}'. @0+378; items CsRaw; round trip");

        // Directives.
        yield return new("parse.directive.mid-line", () => P("class C { } #region R\n"), "Partial; diagnostics: PARSE001 Error @12+10 1:13 A directive must begin its line.; class C { } #region R\n");
        yield return new("parse.directive.unknown", () => P("#foo\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+5 1:1 Unknown preprocessor directive.; #foo\nclass C { }\n");
        yield return new("parse.directive.pragma-alone", () => P("#pragma\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+8 1:1 Unknown preprocessor directive.; #pragma\nclass C { }\n");
        yield return new("parse.directive.pragma-warning", () => P("#pragma warning disable CS1 // why\nclass C { }\n"), "Typed; diagnostics: ; #pragma warning disable CS1 // why\nclass C { }\n");
        yield return new("parse.directive.pragma-warning-action", () => P("#pragma warning foo\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+20 1:1 Directive action expected.; #pragma warning foo\nclass C { }\n");
        yield return new("parse.directive.checksum", () => P("#pragma checksum \"f.cs\" \"{406ea660-64cf-4c82-b6f0-42d48172a799}\" \"ab\"\nclass C { }\n"), "Typed; diagnostics: ; #pragma checksum \"f.cs\" \"{406ea660-64cf-4c82-b6f0-42d48172a799}\" \"ab\"\nclass C { }\n");
        yield return new("parse.directive.checksum-short", () => P("#pragma checksum \"f.cs\"\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+24 1:1 Checksum requires a file, algorithm and checksum.; #pragma checksum \"f.cs\"\nclass C { }\n");
        yield return new("parse.directive.file-based", () => P("#:package A@1\nclass C { }\n"), "Typed; diagnostics: ; #:package A@1\nclass C { }\n");
        yield return new("parse.directive.file-based-region", () => P("#:region R\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+11 1:1 Invalid file-based directive prefix.; #:region R\nclass C { }\n");
        yield return new("parse.directive.sdk-without-prefix", () => P("#sdk A\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+7 1:1 Invalid file-based directive prefix.; #sdk A\nclass C { }\n");
        yield return new("parse.directive.condition-comments", () => P("#if X // c\nclass A { }\n#elif Y /* d */\nclass B { }\n#endif\n"), "Typed; diagnostics: ; #if X // c\nclass A { }\n#elif Y /* d */\nclass B { }\n#endif\n");
        yield return new("parse.directive.condition-invalid", () => P("#if X Y\nclass C { }\n#endif\n"), "Opaque; diagnostics: PARSE001 Error @0+8 1:1 Invalid preprocessor condition. | PARSE002 Warning @0+27 1:1 Endif has no matching if.; #if X Y\nclass C { }\n#endif\n");
        yield return new("parse.directive.condition-deep", () => Brief("#if " + Repeat("(", 200) + "X" + Repeat(")", 200) + "\nclass C { }\n#endif\n"), "Opaque; PARSE002 Endif has no matching if. @0+425; items CsRaw; round trip");
        yield return new("parse.directive.elif-after-taken", () => P("#if true\nclass A { }\n#elif true\nclass B { }\n#else\nclass D { }\n#endif\n"), "Typed; diagnostics: ; #if true\nclass A { }\n#elif true\nclass B { }\n#else\nclass D { }\n#endif\n");
        yield return new("parse.directive.else-taken-symbol", () => P("#if X\nclass A { }\n#else\nclass B { }\n#endif\n", CsLanguageVersion.CSharp15, "X"), "Typed; diagnostics: ; #if X\nclass A { }\n#else\nclass B { }\n#endif\n");
        yield return new("parse.directive.nullable-alone", () => P("#nullable\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+10 1:1 Directive action expected.; #nullable\nclass C { }\n");
        yield return new("parse.directive.nullable-unknown", () => P("#nullable foo\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+14 1:1 Directive action expected.; #nullable foo\nclass C { }\n");
        yield return new("parse.directive.nullable-case", () => P("#nullable Enable\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+17 1:1 Directive action expected.; #nullable Enable\nclass C { }\n");
        yield return new("parse.directive.line-incomplete", () => P("#line\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+6 1:1 Incomplete line directive.; #line\nclass C { }\n");
        yield return new("parse.directive.line-not-number", () => P("#line x\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+8 1:1 Line directive requires an integer.; #line x\nclass C { }\n");
        yield return new("parse.directive.line-number", () => P("#line 10\nclass C { }\n"), "Typed; diagnostics: ; #line 10\nclass C { }\n");
        yield return new("parse.directive.line-file", () => P("#line 10 \"f.cs\"\nclass C { }\n"), "Typed; diagnostics: ; #line 10 \"f.cs\"\nclass C { }\n");
        yield return new("parse.directive.line-file-extra", () => P("#line 10 \"f.cs\" x\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+18 1:1 Unexpected line directive arguments.; #line 10 \"f.cs\" x\nclass C { }\n");
        yield return new("parse.directive.line-unquoted", () => P("#line 10 f\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+11 1:1 Directive requires a quoted string.; #line 10 f\nclass C { }\n");
        yield return new("parse.directive.line-bad-escape", () => P("#line 10 \"\\q\"\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+14 1:1 Invalid directive string escape.; #line 10 \"\\q\"\nclass C { }\n");
        yield return new("parse.directive.line-span", () => P("#line (1, 1) - (2, 5) \"f.cs\"\nclass C { }\n"), "Typed; diagnostics: ; #line (1, 1) - (2, 5) \"f.cs\"\nclass C { }\n");
        yield return new("parse.directive.line-span-offset", () => P("#line (1, 1) - (2, 5) 3 \"f.cs\"\nclass C { }\n"), "Typed; diagnostics: ; #line (1, 1) - (2, 5) 3 \"f.cs\"\nclass C { }\n");
        yield return new("parse.directive.line-span-punctuation", () => P("#line (1, 1) (2, 5) \"f.cs\"\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+27 1:1 Invalid line directive.; #line (1, 1) (2, 5) \"f.cs\"\nclass C { }\n");
        yield return new("parse.directive.line-span-extra", () => P("#line (1, 1) - (2, 5) \"f.cs\" x\nclass C { }\n"), "Partial; diagnostics: PARSE001 Error @0+31 1:1 Unexpected line directive arguments.; #line (1, 1) - (2, 5) \"f.cs\" x\nclass C { }\n");
        yield return new("parse.directive.line-default", () => P("#line default\nclass C { }\n"), "Typed; diagnostics: ; #line default\nclass C { }\n");
        yield return new("parse.directive.in-member-list", () => P("class C\n{\n#foo\n    int F;\n}\n"), "Partial; diagnostics: PARSE002 Warning @10+5 3:1 Unknown preprocessor directive.; class C\n{\n#foo\n    int F;\n}\n");
        yield return new("parse.directive.in-body", () => P("class C\n{\n    void M()\n    {\n#foo\n        F();\n    }\n}\n"), "Partial; diagnostics: PARSE002 Warning @29+5 5:1 Unknown preprocessor directive.; class C\n{\n    void M()\n    {\n#foo\n        F();\n    }\n}\n");
        yield return new("parse.directive.in-namespace", () => P("namespace N\n{\n#region R\n    class C { }\n#endregion\n}\n"), "Typed; diagnostics: ; namespace N\n{\n#region R\n    class C { }\n#endregion\n}\n");
        yield return new("parse.directive.in-file-namespace", () => P("namespace N;\n#region R\nclass C { }\n#endregion\n"), "Typed; diagnostics: ; namespace N;\n#region R\nclass C { }\n#endregion\n");
        yield return new("parse.directive.trivia-in-string-hole", () => Member("string F = $@\"{\n#if X\n1\n#endif\n}\";"), "Partial; diagnostics: PARSE002 Warning @10+34 1:11 Directives cannot appear inside an interpolation.; class C { string F = $@\"{\n#if X\n1\n#endif\n}\"; }");
        yield return new("parse.directive.disabled-statement", () => BriefTree("#if X\nRun();\n#endif\nStop();\n"), "Typed; ; items CsDirective CsDisabledText CsDirective CsSymbol; round trip");
        yield return new("parse.directive.statement", () => BriefTree("#region R\nRun();\n#endregion\n"), "Typed; ; items CsDirective CsSymbol CsDirective; round trip");

        // Strings.
        yield return new("parse.string.format-verbatim", () => Member("string F = $@\"{1:\\d}\";"), "Typed; diagnostics: ; class C { string F = $@\"{1:\\d}\"; }");
        yield return new("parse.string.format-raw", () => Member("string F = $\"\"\"{1:N2}\"\"\";"), "Typed; diagnostics: ; class C { string F = $\"\"\"{1:N2}\"\"\"; }");
        yield return new("parse.string.format-regular", () => Member("string F = $\"{1:N\\x41}\";"), "Typed; diagnostics: ; class C { string F = $\"{1:N\\x41}\"; }");
        yield return new("parse.string.interpolation-trailing", () => Member("string F = $\"{1 2}\";"), "Partial; diagnostics: PARSE002 Warning @10+20 1:11 Unexpected interpolation contents.; class C { string F = $\"{1 2}\"; }");
        yield return new("parse.string.raw-crlf", () => Member("string F = \"\"\"\r\n    a\r\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = \"\"\"\r\n    a\r\n    \"\"\"; }");
        yield return new("parse.string.raw-cr", () => Member("string F = \"\"\"\r    a\r    \"\"\";"), "Typed; diagnostics: ; class C { string F = \"\"\"\r    a\r    \"\"\"; }");
        yield return new("parse.string.raw-empty", () => Member("string F = \"\"\"\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = \"\"\"\n    \"\"\"; }");
        yield return new("parse.string.raw-closing-inline", () => Member("string F = \"\"\"\n    a\n    x\"\"\";"), "Typed; diagnostics: ; class C { string F = \"\"\"\n    a\n    x\"\"\"; }");
        yield return new("parse.string.raw-indentation", () => Member("string F = \"\"\"\n    a\n  b\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = \"\"\"\n    a\n  b\n    \"\"\"; }");
        yield return new("parse.string.raw-blank-line", () => Member("string F = \"\"\"\n    a\n\n    b\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = \"\"\"\n    a\n\n    b\n    \"\"\"; }");
        yield return new("parse.string.escapes", () => Member("string F = \"\\\\ \\\" \\' \\0 \\a \\b \\f \\n \\r \\t \\v\";"), "Typed; diagnostics: ; class C { string F = \"\\\\ \\\" \\' \\0 \\a \\b \\f \\n \\r \\t \\v\"; }");
        yield return new("parse.string.unknown-escape", () => Member("string F = \"\\q\";"), "Typed; diagnostics: ; class C { string F = \"\\q\"; }");
        yield return new("parse.string.unicode-escapes", () => Member("string F = \"\\u0041\\U0001F600\\x41\\x4142\\x7\";"), "Typed; diagnostics: ; class C { string F = \"\\u0041\\U0001F600\\x41\\x4142\\x7\"; }");
        yield return new("parse.string.short-unicode", () => Member("string F = \"\\u41\";"), "Typed; diagnostics: ; class C { string F = \"\\u41\"; }");
        yield return new("parse.string.hex-without-digits", () => Member("string F = \"\\xZ\";"), "Typed; diagnostics: ; class C { string F = \"\\xZ\"; }");
        yield return new("parse.string.too-large-code-point", () => Member("string F = \"\\U00110000\";"), "Typed; diagnostics: ; class C { string F = \"\\U00110000\"; }");
        yield return new("parse.string.char-escape", () => Member("char F = '\\u0041';"), "Typed; diagnostics: ; class C { char F = '\\u0041'; }");
        yield return new("parse.string.verbatim", () => Member("string F = @\"a\"\"b\\c\";"), "Typed; diagnostics: ; class C { string F = @\"a\"\"b\\c\"; }");

        yield return new("parse.string.interpolated-raw-closing-inline", () => Member("string F = $\"\"\"\n    a{1}\n    x\"\"\";"), "Partial; diagnostics: PARSE002 Warning @10+34 1:11 Raw string closing delimiter must occupy its own line.; class C { string F = $\"\"\"\n    a{1}\n    x\"\"\"; }");
        yield return new("parse.string.interpolated-raw-indentation", () => Member("string F = $\"\"\"\n    a{1}\n  b\n    \"\"\";"), "Partial; diagnostics: PARSE002 Warning @10+37 1:11 Raw string indentation is inconsistent.; class C { string F = $\"\"\"\n    a{1}\n  b\n    \"\"\"; }");
        yield return new("parse.string.interpolated-raw-blank-line", () => Member("string F = $\"\"\"\n    a{1}\n\n    b\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = $\"\"\"\n    a{1}\n\n    b\n    \"\"\"; }");
        yield return new("parse.string.interpolated-raw-empty", () => Member("string F = $\"\"\"\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = $\"\"\"\n    \"\"\"; }");
        yield return new("parse.string.interpolated-raw-crlf", () => Member("string F = $\"\"\"\r\n    a{1}\r\n    \"\"\";"), "Typed; diagnostics: ; class C { string F = $\"\"\"\r\n    a{1}\r\n    \"\"\"; }");
        yield return new("parse.string.interpolated-raw-cr", () => Member("string F = $\"\"\"\r    a{1}\r    \"\"\";"), "Typed; diagnostics: ; class C { string F = $\"\"\"\r    a{1}\r    \"\"\"; }");
        yield return new("parse.string.interpolated-escapes", () => Member("string F = $\"\\\\ \\\" \\' \\0 \\a \\b \\f \\n \\r \\t \\v {1}\";"), "Typed; diagnostics: ; class C { string F = $\"\\\\ \\\" \\' \\0 \\a \\b \\f \\n \\r \\t \\v {1}\"; }");
        yield return new("parse.string.interpolated-unknown-escape", () => Member("string F = $\"\\q{1}\";"), "Partial; diagnostics: PARSE002 Warning @10+20 1:11 Invalid string escape.; class C { string F = $\"\\q{1}\"; }");
        yield return new("parse.string.interpolated-unicode", () => Member("string F = $\"\\u0041\\U0001F600\\x41\\x4142\\x7{1}\";"), "Typed; diagnostics: ; class C { string F = $\"\\u0041\\U0001F600\\x41\\x4142\\x7{1}\"; }");
        yield return new("parse.string.interpolated-short-unicode", () => Member("string F = $\"\\u41{1}\";"), "Partial; diagnostics: PARSE002 Warning @10+22 1:11 Invalid Unicode string escape.; class C { string F = $\"\\u41{1}\"; }");
        yield return new("parse.string.interpolated-hex-without-digits", () => Member("string F = $\"\\xZ{1}\";"), "Partial; diagnostics: PARSE002 Warning @10+21 1:11 Invalid Unicode string escape.; class C { string F = $\"\\xZ{1}\"; }");
        yield return new("parse.string.interpolated-large-code-point", () => Member("string F = $\"\\U00110000{1}\";"), "Partial; diagnostics: PARSE002 Warning @10+28 1:11 Invalid Unicode code point.; class C { string F = $\"\\U00110000{1}\"; }");
        yield return new("parse.string.interpolated-bad-unicode", () => Member("string F = $\"\\uZZZZ{1}\";"), "Partial; diagnostics: PARSE002 Warning @10+24 1:11 Invalid Unicode string escape.; class C { string F = $\"\\uZZZZ{1}\"; }");
        yield return new("parse.string.interpolated-trailing-backslash", () => Member("string F = $\"{1:a\\\\}\";"), "Typed; diagnostics: ; class C { string F = $\"{1:a\\\\}\"; }");
        yield return new("parse.string.interpolated-verbatim", () => Member("string F = $@\"a\"\"b\\c{1}\";"), "Typed; diagnostics: ; class C { string F = $@\"a\"\"b\\c{1}\"; }");

        // Declarations.
        yield return new("parse.declaration.record-class-old", () => P("record class R;", CsLanguageVersion.CSharp9), "Partial; diagnostics: GEN073 Error @7+0 1:8 Explicit record class syntax requires C# 10.; record class R;");
        yield return new("parse.declaration.enum-missing-comma", () => P("enum E { A B }"), "Opaque; diagnostics: PARSE002 Warning @0+14 1:1 Enum may contain only enum members.; enum E { A B }");
        yield return new("parse.declaration.function-pointer-field", () => Member("unsafe delegate*<int> F; unsafe delegate* managed<int> G; unsafe delegate* unmanaged<int> H; unsafe delegate* unmanaged[Cdecl]<int> I;"), "Typed; diagnostics: ; class C { unsafe delegate*<int> F; unsafe delegate* managed<int> G; unsafe delegate* unmanaged<int> H; unsafe delegate* unmanaged[Cdecl]<int> I; }");
        yield return new("parse.declaration.destructor-without-body", () => Member("~C();"), "Partial; diagnostics: PARSE002 Warning @10+5 1:11 Destructor requires a body.; class C { ~C(); }");
        yield return new("parse.declaration.extern-destructor", () => Member("extern ~C();"), "Typed; diagnostics: ; class C { extern ~C(); }");
        yield return new("parse.declaration.constructor-without-body", () => Member("C();"), "Partial; diagnostics: PARSE002 Warning @10+4 1:11 Constructor body required.; class C { C(); }");
        yield return new("parse.declaration.extern-constructor", () => Member("extern C();"), "Typed; diagnostics: ; class C { extern C(); }");
        yield return new("parse.declaration.event-expression-body", () => Member("event System.Action E => null;"), "Partial; diagnostics: PARSE002 Warning @10+30 1:11 Expression-bodied event is unsupported.; class C { event System.Action E => null; }");
        yield return new("parse.declaration.unknown-accessor", () => Member("int P { foo; }"), "Partial; diagnostics: PARSE002 Warning @10+14 1:11 Unknown accessor.; class C { int P { foo; } }");
        yield return new("parse.declaration.event-accessors-initializer", () => Member("event System.Action E { add { } remove { } } = null;"), "Partial; diagnostics: PARSE002 Warning @10+44 1:11 Custom/abstract event cannot have an initializer. | PARSE002 Warning @55+7 1:56 Identifier expected.; class C { event System.Action E { add { } remove { } } = null; }");
        yield return new("parse.declaration.property-initializer", () => Member("int P { get; } = 1;"), "Typed; diagnostics: ; class C { int P { get; } = 1; }");
        yield return new("parse.declaration.explicit-conversion", () => P("interface I<T> where T : I<T> { static abstract explicit operator int(T t); }\nclass C : I<C> { static explicit I<C>.operator int(C c) => 0; }\n"), "Typed; diagnostics: ; interface I<T> where T : I<T> { static abstract explicit operator int(T t); }\nclass C : I<C> { static explicit I<C>.operator int(C c) => 0; }\n");
        yield return new("parse.declaration.explicit-conversion-name", () => P("class C : I<C> { static explicit I<C>.foo int(C c) => 0; }\n"), "Partial; diagnostics: PARSE002 Warning @17+39 1:18 Explicit interface operator expected.; class C : I<C> { static explicit I<C>.foo int(C c) => 0; }\n");
        yield return new("parse.declaration.unknown-operator", () => Member("public static C operator @(C c) => c;"), "Partial; diagnostics: PARSE002 Warning @10+37 1:11 Overloadable operator expected.; class C { public static C operator @(C c) => c; }");
        yield return new("parse.declaration.alias-interface-member", () => P("class C : global::I { void global::I.M() { } }"), "Typed; diagnostics: ; class C : global::I { void global::I.M() { } }");
        yield return new("parse.declaration.generic-interface-member", () => P("class C : N.I<int> { void N.I<int>.M() { } int N.I<int>.this[int i] => i; }"), "Typed; diagnostics: ; class C : N.I<int> { void N.I<int>.M() { } int N.I<int>.this[int i] => i; }");
        yield return new("parse.declaration.constraint-unknown-parameter", () => P("class C<T> where U : class { }"), "Partial; diagnostics: PARSE001 Error @0+30 1:1 Constraint has no matching type parameter.; class C<T> where U : class { }");
        yield return new("parse.declaration.this-modifiers", () => P("static class C { static void M(this ref int x) { } static void N(this in int y) { } static void O(this int z) { } }"), "Typed; diagnostics: ; static class C { static void M(this ref int x) { } static void N(this in int y) { } static void O(this int z) { } }");
        yield return new("parse.declaration.scoped", () => Member("void M(scoped ref int a, scoped Span<int> b) { scoped ref int c = ref a; scoped[] d = null; scoped? e = null; }"), "Partial; diagnostics: PARSE002 Warning @83+18 1:84 Expected ';'. | PARSE002 Warning @102+17 1:103 Expected ':'.; class C { void M(scoped ref int a, scoped Span<int> b) { scoped ref int c = ref a; scoped[] d = null; scoped? e = null; } }");
        yield return new("parse.declaration.attributes", () => P("[assembly: A, B]\n[module: C]\n[D]\nclass C { }\n"), "Typed; diagnostics: ; [assembly: A, B]\n[module: C]\n[D]\nclass C { }\n");
        yield return new("parse.declaration.attributes-at-end", () => P("class C { }\n[assembly: A, B]\n"), "Typed; diagnostics: ; class C { }\n[assembly: A, B]\n");
        yield return new("parse.declaration.attribute-argument-forms", () => P("[A(1, B = 2, c: 3)]\nclass C { }\n"), "Typed; diagnostics: ; [A(1, B = 2, c: 3)]\nclass C { }\n");
        yield return new("parse.declaration.union-empty", () => P("union U();"), "Partial; diagnostics: PARSE001 Error @0+10 1:1 Expected ';'.; union U();");
        yield return new("parse.declaration.union", () => P("union U(int, string);"), "Typed; diagnostics: ; union U(int, string);");
        yield return new("parse.declaration.partial-constructor", () => P("partial class C { partial C(); }"), "Typed; diagnostics: ; partial class C { partial C(); }");
        yield return new("parse.declaration.delegate-member", () => Member("delegate void D(int a);"), "Typed; diagnostics: ; class C { delegate void D(int a); }");
        yield return new("parse.declaration.qualified-base", () => P("class C : A.B<int>.D, global::E { }"), "Typed; diagnostics: ; class C : A.B<int>.D, global::E { }");

        // Statements and recovery.
        yield return new("parse.statement.top-level-using", () => P("using (var x = F()) { }\nusing var y = F();\n"), "Typed; diagnostics: ; using (var x = F()) { }\nusing var y = F();\n");
        yield return new("parse.statement.top-level-mixed", () => P("System.Console.WriteLine(1);\nclass C { }\nSystem.Console.WriteLine(2);\n"), "Partial; diagnostics: PARSE002 Warning @41+28 3:1 Top-level statements must precede type/namespace declarations.; System.Console.WriteLine(1);\nclass C { }\nSystem.Console.WriteLine(2);\n");
        yield return new("parse.statement.try-without-block", () => Body("try ;"), "Partial; diagnostics: PARSE002 Warning @21+5 1:22 try requires a block.; class C { void M() { try ; } }");
        yield return new("parse.statement.catch-without-block", () => Body("try { } catch ;"), "Partial; diagnostics: PARSE002 Warning @21+7 1:22 catch requires a block.; class C { void M() { try { } catch ; } }");
        yield return new("parse.statement.try-alone", () => Body("try { }"), "Partial; diagnostics: PARSE002 Warning @21+7 1:22 try requires catch or finally.; class C { void M() { try { } } }");
        yield return new("parse.statement.for-without-value", () => Body("for (int i; i < 1; i++) { }"), "Typed; diagnostics: ; class C { void M() { for (int i; i < 1; i++) { } } }");
        yield return new("parse.statement.for-declaration-missing", () => Body("for (int i = 0 j; ; ) { }"), "Partial; diagnostics: PARSE002 Warning @21+25 1:22 Expected ';'.; class C { void M() { for (int i = 0 j; ; ) { } } }");
        yield return new("parse.statement.for-expressions", () => BriefTree("for (i = 0, j = 1; i < j; i++) { }\nfor (var k = 0; k < 1; k++) { }\nfor (int m = 0, n; m < 1; m++) { }\n"), "Typed; ; items CsInstruction CsInstruction CsInstruction; round trip");
        yield return new("parse.statement.case-discard", () => Body("switch (a) { case _: break; }"), "Typed; diagnostics: ; class C { void M() { switch (a) { case _: break; } } }");
        yield return new("parse.statement.case-empty-section", () => Body("switch (a) { case 1: case 2: break; default: }"), "Partial; diagnostics: PARSE002 Warning @21+46 1:22 A switch section cannot fall through; use break, return, throw or goto.; class C { void M() { switch (a) { case 1: case 2: break; default: } } }");
        yield return new("parse.statement.invalid-target", () => Body("1 = 2;"), "Partial; diagnostics: PARSE002 Warning @21+6 1:22 An assignment requires a writable target.; class C { void M() { 1 = 2; } }");
        yield return new("parse.statement.compound-assignments", () => Body("a -= 1; a *= 2; a /= 3; a %= 4; a ^= 5; a <<= 1; a >>>= 1; a ??= b;"), "Typed; diagnostics: ; class C { void M() { a -= 1; a *= 2; a /= 3; a %= 4; a ^= 5; a <<= 1; a >>>= 1; a ??= b; } }");
        yield return new("parse.statement.invalid-increments", () => P("class C\n{\n    void M(object o, int x, int a, int b)\n    {\n        int before = 1;\n        ((int)x)++;\n        N()++;\n        (a + b)++;\n        ++N();\n        int after = 2;\n    }\n}\n"), "Partial; diagnostics: PARSE002 Warning @90+11 6:9 Increment and decrement require a writable target. | PARSE002 Warning @110+6 7:9 Increment and decrement require a writable target. | PARSE002 Warning @125+10 8:9 Increment and decrement require a writable target. | PARSE002 Warning @144+6 9:9 Increment and decrement require a writable target.; class C\n{\n    void M(object o, int x, int a, int b)\n    {\n        int before = 1;\n        ((int)x)++;\n        N()++;\n        (a + b)++;\n        ++N();\n        int after = 2;\n    }\n}\n");
        yield return new("parse.statement.validation-raw-member", () => P("class C { int P => N()++; int Q => 1; }"), "Partial; diagnostics: PARSE002 Warning @10+15 1:11 Increment and decrement require a writable target.; class C { int P => N()++; int Q => 1; }");
        yield return new("parse.statement.whole-source-opaque", () => P("#endregion\nclass C { }\n"), "Opaque; diagnostics: PARSE002 Warning @0+23 1:1 Endregion has no matching region.; #endregion\nclass C { }\n");
        yield return new("parse.statement.is-expression", () => P("class C\n{\n    void M(object o)\n    {\n        o is R { A: 1 } r with { };\n        F();\n    }\n}\n"), "Partial; diagnostics: PARSE002 Warning @45+27 5:9 An is-expression cannot be followed by an operator that binds tighter than 'is'.; class C\n{\n    void M(object o)\n    {\n        o is R { A: 1 } r with { };\n        F();\n    }\n}\n");
        yield return new("parse.statement.await-tree", () => BriefTree("var x = await F();\nawait G();\n"), "Typed; ; items CsSymbol CsSymbol; round trip");
        yield return new("parse.statement.tree-kinds", () => BriefTree("lock (o) { }\nchecked { }\nunsafe { }\nfixed (int* p = &x) { }\nyield return 1;\nyield break;\ngoto L;\nL: ;\ntry { } finally { }\nswitch (a) { default: break; }\n"), "Typed; ; items CsInstruction CsInstruction CsInstruction CsInstruction CsInstruction CsInstruction CsInstruction CsInstruction CsInstruction CsInstruction; round trip");
        yield return new("parse.statement.tree-break", () => BriefTree("break;"), "Opaque; GEN082 Break requires an enclosing loop or switch outside finally.; items CsRaw; round trip");

        // Expressions.
        yield return new("parse.expression.refs", () => Body("var r = __makeref(x); var t = __reftype(r); int v = __refvalue(r, int);"), "Typed; diagnostics: ; class C { void M() { var r = __makeref(x); var t = __reftype(r); int v = __refvalue(r, int); } }");
        yield return new("parse.expression.keyword-members", () => Body("var a = int.MaxValue; var b = nint.Zero; var c = nuint.Size; var d = string.Empty;"), "Typed; diagnostics: ; class C { void M() { var a = int.MaxValue; var b = nint.Zero; var c = nuint.Size; var d = string.Empty; } }");
        yield return new("parse.expression.keyword-without-member", () => Body("var a = int.;"), "Partial; diagnostics: PARSE002 Warning @21+13 1:22 Expected ';'.; class C { void M() { var a = int.; } }");
        yield return new("parse.expression.tuple-elements", () => Body("var t = (a * b, c); (int* p, int q) = (null, 1); var u = (from x in xs select x, 1);"), "Typed; diagnostics: ; class C { void M() { var t = (a * b, c); (int* p, int q) = (null, 1); var u = (from x in xs select x, 1); } }");
        yield return new("parse.expression.out-arguments", () => Body("F(out var x, out int y, out z, out w.v);"), "Typed; diagnostics: ; class C { void M() { F(out var x, out int y, out z, out w.v); } }");
        yield return new("parse.expression.ref-out-in", () => Body("ref int r = ref x; F(ref a, in b);"), "Typed; diagnostics: ; class C { void M() { ref int r = ref x; F(ref a, in b); } }");
        yield return new("parse.expression.casts", () => Body("var a = (int*)p; var b = (T*)p; var c = (int)-x; var d = (a)+b; var e = (int)+1;"), "Typed; diagnostics: ; class C { void M() { var a = (int*)p; var b = (T*)p; var c = (int)-x; var d = (a)+b; var e = (int)+1; } }");
        yield return new("parse.expression.new-without-arguments", () => Body("var x = new C;"), "Partial; diagnostics: PARSE002 Warning @21+14 1:22 Expected ';'.; class C { void M() { var x = new C; } }");
        yield return new("parse.expression.arrays", () => Body("var a = new int[,] { { 1 } }; var b = new int[1][,]; var c = new int[2][][]; var d = new[] { 1 }; var e = new[][] { };"), "Partial; diagnostics: PARSE002 Warning @119+20 1:120 Expected ';'.; class C { void M() { var a = new int[,] { { 1 } }; var b = new int[1][,]; var c = new int[2][][]; var d = new[] { 1 }; var e = new[][] { }; } }");
        yield return new("parse.expression.lambdas", () => Body("var a = ref int () => ref x; var b = ref readonly int () => ref x; var c = int () => 1; var d = (int, int) () => (1, 2); var e = (int x = 1) => x; var f = static async () => 1; var g = async static () => 1;"), "Typed; diagnostics: ; class C { void M() { var a = ref int () => ref x; var b = ref readonly int () => ref x; var c = int () => 1; var d = (int, int) () => (1, 2); var e = (int x = 1) => x; var f = static async () => 1; var g = async static () => 1; } }");
        yield return new("parse.expression.lambda-repeats", () => Body("var a = async async () => 1;"), "Partial; diagnostics: PARSE002 Warning @21+28 1:22 Expected ';'.; class C { void M() { var a = async async () => 1; } }");
        yield return new("parse.expression.lambda-static-repeat", () => Body("var a = static static () => 1;"), "Partial; diagnostics: PARSE002 Warning @21+30 1:22 Expected ';'.; class C { void M() { var a = static static () => 1; } }");
        yield return new("parse.expression.lambda-parameter-form", () => Body("var a = (int x y) => 1;"), "Partial; diagnostics: PARSE002 Warning @21+23 1:22 Expected ';'.; class C { void M() { var a = (int x y) => 1; } }");
        yield return new("parse.expression.anonymous-method-expression", () => Body("var a = delegate () => 1;"), "Partial; diagnostics: PARSE002 Warning @21+25 1:22 Expected ';'.; class C { void M() { var a = delegate () => 1; } }");
        yield return new("parse.expression.anonymous-method-without-block", () => Body("var a = delegate ();"), "Partial; diagnostics: PARSE002 Warning @21+20 1:22 Expected ';'.; class C { void M() { var a = delegate (); } }");
        yield return new("parse.expression.unclosed-parenthesis", () => P("class C { void M() { F((a, b"), "Partial; diagnostics: PARSE002 Warning @21+7 1:22 Expected ')'. | PARSE002 Warning @10+18 1:11 Expected '}'. | PARSE001 Error @0+28 1:1 Expected '}'.; class C { void M() { F((a, b");
        yield return new("parse.expression.queries", () => Body("var q = from x in xs join y in ys on x equals y into g select g; var r = from x in xs group x by x into g select g; var s = from x in xs where x;"), "Partial; diagnostics: PARSE002 Warning @137+29 1:138 Expected ';'.; class C { void M() { var q = from x in xs join y in ys on x equals y into g select g; var r = from x in xs group x by x into g select g; var s = from x in xs where x; } }");
        yield return new("parse.expression.query-tuple", () => Body("var q = (from x in xs select x, 2);"), "Typed; diagnostics: ; class C { void M() { var q = (from x in xs select x, 2); } }");
        yield return new("parse.expression.operand-heuristics", () => Body("var a = (A)b; var b = (A)(b); var c = (A)'c'; var d = (A)\"s\"; var e = (A)$\"s\"; var f = (A)@b; var g = (A)_b; var h = (A)1; var i = (A) as B; var j = (A) is B;"), "Typed; diagnostics: ; class C { void M() { var a = (A)b; var b = (A)(b); var c = (A)'c'; var d = (A)\"s\"; var e = (A)$\"s\"; var f = (A)@b; var g = (A)_b; var h = (A)1; var i = (A) as B; var j = (A) is B; } }");
        yield return new("parse.expression.adjacent-tokens", () => Body("var a = b >> 1; var c = d > > 1; var e = f >>> 1;"), "Partial; diagnostics: PARSE002 Warning @37+16 1:38 Expected ';'.; class C { void M() { var a = b >> 1; var c = d > > 1; var e = f >>> 1; } }");

        // Patterns.
        yield return new("parse.pattern.nullable-type", () => Body("var a = o is int?; var b = o is int? && c; var c = o is int? x;"), "Partial; diagnostics: PARSE002 Warning @64+20 1:65 Expected ';'.; class C { void M() { var a = o is int?; var b = o is int? && c; var c = o is int? x; } }");
        yield return new("parse.pattern.parenthesized-types", () => Body("var a = o is (int, string)[]; var b = o is (A<B>[]); var c = o is (int[][]); var d = o is (A<B<C>>[]); var e = o is ((int, string)); var f = o is (int.MaxValue); var g = o is (A >>> B); var h = o is ([])"), "Partial; diagnostics: PARSE002 Warning @207+17 1:208 Expected ';'.; class C { void M() { var a = o is (int, string)[]; var b = o is (A<B>[]); var c = o is (int[][]); var d = o is (A<B<C>>[]); var e = o is ((int, string)); var f = o is (int.MaxValue); var g = o is (A >>> B); var h = o is ([]) } }");
        yield return new("parse.pattern.property-without-name", () => Body("var a = o is { 1 };"), "Partial; diagnostics: PARSE002 Warning @21+19 1:22 Expected ';'.; class C { void M() { var a = o is { 1 }; } }");
        yield return new("parse.pattern.designations", () => Body("var a = o is int x when; var b = o switch { int y and > 1 => y, _ => 0 };"), "Partial; diagnostics: PARSE002 Warning @21+24 1:22 Expected ';'.; class C { void M() { var a = o is int x when; var b = o switch { int y and > 1 => y, _ => 0 }; } }");

        // Documentation comments.
        yield return new("parse.doc.block", () => P("/** <summary>a</summary> */\nclass C { }\n"), "Typed; diagnostics: ; /** <summary>a</summary> */\nclass C { }\n");
        yield return new("parse.doc.no-space", () => P("///<summary>a</summary>\nclass C { }\n"), "Typed; diagnostics: ; ///<summary>a</summary>\nclass C { }\n");
        yield return new("parse.doc.unclosed", () => P("/// <a>\nclass C { }\n"), "Typed; diagnostics: ; /// <a>\nclass C { }\n");
        yield return new("parse.doc.deep", () => Brief("/// " + Repeat("<a>", 140) + Repeat("</a>", 140) + "\nclass C { }\n"), "Typed; ; items type(); round trip");
        yield return new("parse.doc.cref-trailing", () => P("/// <see cref=\"A B\"/>\nclass C { }\n"), "Typed; diagnostics: ; /// <see cref=\"A B\"/>\nclass C { }\n");
        yield return new("parse.doc.cref-broken", () => P("/// <see cref=\"A(\"/>\nclass C { }\n"), "Typed; diagnostics: ; /// <see cref=\"A(\"/>\nclass C { }\n");
        yield return new("parse.doc.cref-operators", () => P("/// <see cref=\"operator +\"/> <see cref=\"operator @\"/> <see cref=\"this\"/> <see cref=\"this[int]\"/> <see cref=\"M(int[], int[,])\"/>\nclass C { }\n"), "Typed; diagnostics: ; /// <see cref=\"operator +\"/> <see cref=\"operator @\"/> <see cref=\"this\"/> <see cref=\"this[int]\"/> <see cref=\"M(int[], int[,])\"/>\nclass C { }\n");
        yield return new("parse.doc.cref-deep", () => Brief("/// <see cref=\"" + Repeat("G{", 200) + "int" + Repeat("}", 200) + "\"/>\nclass C { }\n"), "Typed; ; items type(); round trip");

        // Comments placed around and inside units.
        yield return new("parse.comment.positions", () => P("class C\n{\n    // lead\n    int F; // trail\n    /* inner */ int G /* after */;\n    void M(/* p */ int a /* q */) { /* body */ }\n}\n"), "Typed; diagnostics: ; class C\n{\n    // lead\n    int F; // trail\n    /* inner */ int G /* after */;\n    void M(/* p */ int a /* q */) { /* body */ }\n}\n");
        yield return new("parse.comment.blank-lines", () => P("class C\n{\n    int F;\r\n\r\n    // c\r\n\r\n    int G;\n}\n"), "Typed; diagnostics: ; class C\n{\n    int F;\r\n\r\n    // c\r\n\r\n    int G;\n}\n");
        yield return new("parse.comment.expression-braces", () => Body("var a = new[] { 1 }; var b = new C { }; var c = x switch { _ => 1 }; Run(() => { });"), "Typed; diagnostics: ; class C { void M() { var a = new[] { 1 }; var b = new C { }; var c = x switch { _ => 1 }; Run(() => { }); } }");

        // Nesting past the limit.
        yield return new("parse.depth.namespaces", () => Brief(Repeat("namespace N { ", 200) + Repeat("} ", 200)), "Partial; PARSE002 Nesting exceeds 128. @1764+1183; items namespace(1); round trip");
        yield return new("parse.depth.types", () => Brief(Repeat("class C { ", 200) + Repeat("} ", 200)), "Partial; PARSE002 Nesting exceeds 128. @1260+887; items type(CsTypeDeclaration); round trip");
        yield return new("parse.depth.parens", () => Brief("class C { int M(int a) => " + Repeat("(", 130) + "a" + Repeat(")", 130) + "; }"), "Partial; PARSE002 Nesting exceeds 128. @10+278; items type(CsRawMember); round trip");
        yield return new("parse.depth.blocks", () => Brief("class C { void M() { " + Repeat("{ ", 130) + Repeat("} ", 130) + "} }"), "Partial; PARSE002 Nesting exceeds 128. @143+275; items type(CsMethod); round trip");
        yield return new("parse.depth.statement", () => Brief("class C\n{\n    int M(int a)\n    {\n        int b = 1;\n        return " + Repeat("(", 130) + "a" + Repeat(")", 130) + ";\n    }\n    int N() => 2;\n}\n"), "Partial; PARSE002 Nesting exceeds 128. @60+269; items type(CsMethod CsMethod); round trip");
        yield return new("parse.depth.generics", () => Brief("class C { " + Repeat("G<", 130) + "int" + Repeat(">", 130) + " f; }"), "Partial; PARSE002 Nesting exceeds 128. @10+396; items type(CsRawMember); round trip");
        yield return new("parse.depth.generic-call", () => Brief("class C { int f; object M(C a) => a.F<" + Repeat("L<", 130) + "int" + Repeat(">", 130) + ">(1); void N() { } }"), "Partial; PARSE002 Nesting exceeds 128. @17+419; items type(CsField CsRawMember CsMethod); round trip");
        yield return new("parse.depth.generic-call-nullable", () => Brief("class C { int f; object M(C a) => a.F<" + Repeat("L<", 130) + "int?" + Repeat(">", 130) + ">(1).b; void N() { } }"), "Partial; PARSE002 Nesting exceeds 128. @17+422; items type(CsField CsRawMember CsMethod); round trip");
        yield return new("parse.depth.generic-call-block", () => Brief("class C { int f; object M(C a) { int b = 1; return a.F<" + Repeat("L<", 130) + "int" + Repeat(">", 130) + ">(1); } }"), "Partial; PARSE002 Nesting exceeds 128. @44+409; items type(CsField CsMethod); round trip");
        yield return new("parse.depth.generic-call-top-level", () => Brief("var x = 1;\nvar y = a.F<" + Repeat("L<", 130) + "int" + Repeat(">", 130) + ">(1);\n"), "Partial; PARSE002 Nesting exceeds 128. @11+410; items CsGlobalStatement CsRaw; round trip");
        yield return new("parse.depth.arrays", () => Brief("class C { int" + Repeat("[]", 130) + " f; }"), "Partial; PARSE002 Nesting exceeds 128. @10+266; items type(CsRawMember); round trip");
        yield return new("parse.depth.lambdas", () => Brief("class C { object M() => " + Repeat("() => ", 70) + "1; }"), "Partial; PARSE002 Nesting exceeds 128. @10+436; items type(CsRawMember); round trip");
        yield return new("parse.depth.lambda-arguments", () => Brief("class C { void M() { " + Repeat("F(() => { ", 40) + Repeat("}); ", 40) + "} }"), "Partial; PARSE002 Nesting exceeds 128. @321+139; items type(CsMethod); round trip");
        yield return new("parse.depth.switch-expressions", () => Brief("class C { int M(int a) => " + Repeat("a switch { _ => ", 70) + "1" + Repeat(" }", 70) + "; }"), "Partial; PARSE002 Nesting exceeds 128. @10+1278; items type(CsRawMember); round trip");
        yield return new("parse.depth.or-pattern", () => Brief("class C { bool M(int a) => a is " + Repeat("1", 130, " or ") + "; }"), "Partial; PARSE002 Nesting exceeds 128. @10+669; items type(CsRawMember); round trip");
        yield return new("parse.depth.ternary", () => Brief("class C { int M(bool b) => " + Repeat("b ? 1 : ", 130) + "0; }"), "Partial; PARSE002 Nesting exceeds 128. @10+1059; items type(CsRawMember); round trip");
        yield return new("parse.depth.initializers", () => Brief("class C { object M() => " + Repeat("new C { P = ", 50) + "1" + Repeat(" }", 50) + "; }"), "Partial; PARSE002 Nesting exceeds 128. @10+716; items type(CsRawMember); round trip");
        yield return new("parse.depth.embedded-directive", () => Brief("class C { int M(int a) => " + Repeat("(", 124) + "a\n#if X\n#endif\n" + Repeat(")", 124) + "; }"), "Opaque; PARSE002 Nesting exceeds 128. @0+292; items CsRaw; round trip");
        yield return new("parse.depth.tree", () => BriefTree("var x = " + Repeat("F(", 60) + "a" + Repeat(")", 60) + ";"), "Opaque; PARSE002 Nesting exceeds 128.; items CsRaw; round trip");
        yield return new("parse.depth.local-functions", () => Brief("class C { void M() { " + Repeat("void L() { ", 50) + Repeat("} ", 50) + "} }"), "Partial; PARSE002 Nesting exceeds 128. @472+116; items type(CsMethod); round trip");
        yield return new("parse.depth.if", () => Brief("class C { void M(bool b) { " + Repeat("if (b) ", 70) + "; } }"), "Partial; PARSE002 Nesting exceeds 128. @27+491; items type(CsMethod); round trip");
        yield return new("parse.depth.unary", () => Brief("class C { int M(int a) => " + Repeat("~", 130) + "a; }"), "Partial; PARSE002 Nesting exceeds 128. @10+148; items type(CsRawMember); round trip");

        // Explicit fragment parses.
        yield return new("parse.fragment.expression", () => Fragment(new CsRaw("a + b"), ParseKind.Expression), "Typed; diagnostics: ; CsExpression(CsExpression CsExpression)");
        yield return new("parse.fragment.expression-comment", () => Fragment(new CsRaw("a + b") { Comments = new[] { new CsComment("// c") } }, ParseKind.Expression), "Typed; diagnostics: ; CsExpression(CsComment CsExpression CsExpression)");
        yield return new("parse.fragment.member", () => Fragment(new CsRaw("int F;"), ParseKind.Member), "Typed; diagnostics: ; CsField(CsType)");
        yield return new("parse.fragment.member-in-struct", () => Fragment(new CsRawMember("int F;"), ParseKind.Member), "Typed; diagnostics: ; CsField(CsType)");
        yield return new("parse.fragment.member-struct-context", () => new CsRawMember("S() { }").Parse(new ParseContext(ParseKind.Member, ContainingType: "S", ContainingKind: TypeKind.Struct)), "Typed; diagnostics: ; CsConstructor(CsBody)");
        yield return new("parse.fragment.statements", () => Fragment(new CsBody { Raw = "F(); G();" }, ParseKind.Statements), "Typed; diagnostics: ; CsBody(CsStatement(CsExpression(CsExpression)) CsStatement(CsExpression(CsExpression)))");
        yield return new("parse.fragment.raw-statement", () => Fragment(new CsStatement(StatementKind.Raw, Text: "F();"), ParseKind.Statements), "Typed; diagnostics: ; CsBody(CsStatement(CsExpression(CsExpression)))");
        yield return new("parse.fragment.raw-expression", () => Fragment(new CsExpression(ExpressionKind.Raw, "a", Values<CsExpression>.Empty), ParseKind.Expression), "Typed; diagnostics: ; CsExpression");
        yield return new("parse.fragment.raw-type", () => Fragment(new CsType("int") { Form = TypeForm.Raw }, ParseKind.Expression), "Typed; diagnostics: ; CsExpression");
        yield return new("parse.fragment.document", () => Fragment(new CsRaw("class C { }") { Comments = new[] { new CsComment("// c") } }, ParseKind.Document), "Typed; diagnostics: ; // c\nclass C\n{\n}\n");
        yield return new("parse.fragment.document-errors", () => Fragment(new CsRaw("class C {"), ParseKind.Document), "Partial; diagnostics: PARSE001 Error @0+9 1:1 Expected '}'.; CsRaw");
        yield return new("parse.fragment.typed-node", () => Fragment(Cs.Id("a"), ParseKind.Expression), "throws InvalidOperationException: Explicit Parse requires a raw node and a matching context.");
        yield return new("parse.fragment.undefined-kind", () => Fragment(new CsRaw("a"), (ParseKind)99), "throws ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'context')");
        yield return new("parse.fragment.expression-error", () => Fragment(new CsRaw("a +"), ParseKind.Expression), "Opaque; diagnostics: PARSE001 Error @3+0 1:4 Expression expected.; CsRaw");
        yield return new("parse.fragment.trailing-tokens", () => Fragment(new CsRaw("a b"), ParseKind.Expression), "Opaque; diagnostics: PARSE001 Error @2+0 1:3 Unexpected trailing tokens.; CsRaw");
        yield return new("parse.fragment.expression-version", () => Fragment(new CsRaw("x is not null"), ParseKind.Expression, CsLanguageVersion.CSharp7_3), "Opaque; diagnostics: GEN073 Error @0+0 1:1 Type, relational and logical patterns requires C# 9.; CsRaw");
        yield return new("parse.fragment.member-version", () => Fragment(new CsRaw("public required int F { get; init; }"), ParseKind.Member, CsLanguageVersion.CSharp7_3), "Opaque; diagnostics: GEN073 Error @0+0 1:1 Required member requires C# 11. | GEN073 Error @0+0 1:1 Init accessor requires C# 9.; CsRaw");
        yield return new("parse.fragment.statements-version", () => Fragment(new CsRaw("var f = static x => x;"), ParseKind.Statements, CsLanguageVersion.CSharp7_3), "Opaque; diagnostics: GEN073 Error @0+0 1:1 Natural lambda type inference requires C# 10. | GEN073 Error @0+0 1:1 Static lambda requires C# 9.; CsRaw");
        yield return new("parse.fragment.member-record-class-old", () => Fragment(new CsRaw("record class R;"), ParseKind.Member, CsLanguageVersion.CSharp9), "Partial; diagnostics: GEN073 Error @7+0 1:8 Explicit record class syntax requires C# 10.; CsRaw");
        yield return new("parse.fragment.document-version", () => Fragment(new CsRaw("record R;"), ParseKind.Document, CsLanguageVersion.CSharp8), "Partial; diagnostics: GEN073 Error @0+0 1:1 Record class requires C# 9.; CsRaw");
        yield return new("parse.fragment.deep-expression", () => BriefFragment(Repeat("(", 200) + "a" + Repeat(")", 200), ParseKind.Expression), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-calls-statements", () => BriefFragment(Repeat("F(", 200) + "a" + Repeat(")", 200) + ";", ParseKind.Statements), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-calls-member", () => BriefFragment("object M() => " + Repeat("F(", 200) + "a" + Repeat(")", 200) + ";", ParseKind.Member), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-generic", () => BriefFragment("F<" + Repeat("G<", 200) + "int" + Repeat(">", 200) + ">()", ParseKind.Expression), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-generic-nullable", () => BriefFragment("F<" + Repeat("G<", 126) + "int?" + Repeat(">", 126) + ">()", ParseKind.Expression), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-generic-statement", () => BriefFragment("var x = F<" + Repeat("G<", 126) + "int" + Repeat(">", 126) + ">();", ParseKind.Statements), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-generic-member", () => BriefFragment("object M() => F<" + Repeat("G<", 126) + "int" + Repeat(">", 126) + ">();", ParseKind.Member), "Opaque; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.deep-generic-fits", () => BriefFragment("F<" + Repeat("G<", 124) + "int" + Repeat(">", 124) + ">()", ParseKind.Expression), "Typed; ; CsExpression");
        yield return new("parse.fragment.deep-document", () => BriefFragment(Repeat("namespace N { ", 200) + Repeat("} ", 200), ParseKind.Document), "Partial; PARSE002 Nesting exceeds 128.; CsRaw");
        yield return new("parse.fragment.tree-options", () => Cs.ParseTree("Run();", new ParseOptions { LanguageVersion = CsLanguageVersion.CSharp12 }), "Typed; diagnostics: ; Run();");
    }
}
