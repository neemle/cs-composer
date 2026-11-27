using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// Every payload and shape rule of builder-made preprocessor directives, one rule per row.
[Trait("Category", "Unit")]
public sealed class DirectiveValidationContractTests
{
    const string Payload = "Payload does not belong to this directive kind.";
    static readonly string Md5 = "406ea660-64cf-4c82-b6f0-42d48172a799";

    static IReadOnlyList<GenDiagnostic> Validate(CsDirective directive, CsLanguageVersion version = CsLanguageVersion.CSharp15)
    {
        var items = directive.Kind is DirectiveKind.If ? new CsNode[] { directive, new CsDirective(DirectiveKind.EndIf) }
            : directive.Kind is DirectiveKind.Region ? new CsNode[] { directive, new CsDirective(DirectiveKind.EndRegion) }
            : new CsNode[] { directive };
        return new CsDocument(version) { Items = items }.Validate();
    }

    static CsDirective Region => new(DirectiveKind.Region);

    public static IEnumerable<object[]> MisplacedPayloads()
    {
        yield return new object[] { Region with { Name = "x" } };
        yield return new object[] { Region with { Modifiers = new[] { "public" } } };
        yield return new object[] { Region with { Attributes = new[] { Cs.Attribute(Cs.Type("A")) } } };
        yield return new object[] { Region with { ExplicitInterface = Cs.Type("I") } };
        yield return new object[] { Region with { Condition = Cs.Id("A") } };
        yield return new object[] { Region with { Symbol = "A" } };
        yield return new object[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Id("A"), Text = "x" } };
        yield return new object[] { Region with { File = "a.cs" } };
        yield return new object[] { Region with { Line = 1 } };
        yield return new object[] { Region with { Start = new CsLinePosition(1, 1) } };
        yield return new object[] { Region with { End = new CsLinePosition(1, 1) } };
        yield return new object[] { Region with { CharacterOffset = 1 } };
        yield return new object[] { Region with { Checksum = "00" } };
        yield return new object[] { Region with { ChecksumAlgorithm = Md5 } };
        yield return new object[] { Region with { Warnings = new[] { "CS0168" } } };
        yield return new object[] { Region with { Action = DirectiveAction.Enable } };
    }

    [Theory]
    [MemberData(nameof(MisplacedPayloads))]
    public void Payload_of_another_directive_kind_is_rejected(CsDirective directive)
    {
        Assert.Contains(Validate(directive), d => d.Code == "GEN065" && d.Message == Payload);
    }

    public static IEnumerable<object?[]> Shapes()
    {
        // directive, version, expected message fragment (null: valid)
        yield return new object?[] { Region, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective((DirectiveKind)99), CsLanguageVersion.CSharp15, "Unknown directive or action." };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = (DirectiveAction)99, Warnings = new[] { "CS1" } }, CsLanguageVersion.CSharp15, "Unknown directive or action." };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Literal(1) }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Literal(true) }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.If), CsLanguageVersion.CSharp15, "Required node or collection is null." };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = new CsExpression(ExpressionKind.Parenthesized, "", new[] { Cs.Id("A") }) }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = new CsExpression(ExpressionKind.Parenthesized, "", new[] { Cs.Id("A"), Cs.Id("B") }) }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Unary(CsOperator.Not, Cs.Id("A")) }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Unary(CsOperator.Negate, Cs.Id("A")) }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Unary(CsOperator.Not, Cs.Literal(1)) }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Binary(CsOperator.AndAlso, Cs.Id("A"), Cs.Binary(CsOperator.NotEqual, Cs.Id("B"), Cs.Literal(false))) }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Binary(CsOperator.Add, Cs.Id("A"), Cs.Id("B")) }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Binary(CsOperator.OrElse, Cs.Id("A"), Cs.Literal(2)) }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.If) { Condition = Cs.Id("A").Member("B") }, CsLanguageVersion.CSharp15, "Preprocessor condition permits" };
        yield return new object?[] { new CsDirective(DirectiveKind.Define) { Symbol = "@A" }, CsLanguageVersion.CSharp15, "Invalid preprocessor symbol." };
        yield return new object?[] { new CsDirective(DirectiveKind.Undef) { Symbol = "true" }, CsLanguageVersion.CSharp15, "Invalid preprocessor symbol." };
        yield return new object?[] { new CsDirective(DirectiveKind.Define) { Symbol = "DEBUG" }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Define), CsLanguageVersion.CSharp15, "Invalid identifier." };
        yield return new object?[] { new CsDirective(DirectiveKind.Define) { Symbol = "A\rB" }, CsLanguageVersion.CSharp15, "Directive text must occupy one line." };
        yield return new object?[] { new CsDirective(DirectiveKind.Warning) { Text = "a\nb" }, CsLanguageVersion.CSharp15, "Directive text must occupy one line." };
        yield return new object?[] { new CsDirective(DirectiveKind.Warning) { Text = "careful" }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Nullable) { Action = DirectiveAction.Enable }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Nullable) { Action = DirectiveAction.Restore, Text = "warnings" }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Nullable) { Action = DirectiveAction.Disable, Text = "annotations" }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Nullable) { Action = DirectiveAction.Enable, Text = "all" }, CsLanguageVersion.CSharp15, "Invalid nullable directive action or target." };
        yield return new object?[] { new CsDirective(DirectiveKind.Nullable) { Action = DirectiveAction.Hidden }, CsLanguageVersion.CSharp15, "Invalid nullable directive action or target." };
        yield return new object?[] { new CsDirective(DirectiveKind.Nullable) { Action = DirectiveAction.Enable }, CsLanguageVersion.CSharp7_3, "Nullable directive requires C# 8." };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Disable, Warnings = new[] { "CS0168", "IDE1" } }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Restore }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Enable }, CsLanguageVersion.CSharp15, "Warning pragma requires disable or restore." };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Disable, Warnings = null! }, CsLanguageVersion.CSharp15, "Required node or collection is null." };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Disable, Warnings = new[] { "" } }, CsLanguageVersion.CSharp15, "Invalid warning identifier." };
        yield return new object?[] { new CsDirective(DirectiveKind.PragmaWarning) { Action = DirectiveAction.Disable, Warnings = new[] { "CS-1" } }, CsLanguageVersion.CSharp15, "Invalid warning identifier." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Line = 10 }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Line = 10, File = "a.cs" }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Action = DirectiveAction.Default }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Action = DirectiveAction.Hidden }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Action = DirectiveAction.Enable }, CsLanguageVersion.CSharp15, "Invalid line directive action." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line), CsLanguageVersion.CSharp15, "Line directive needs one mapping or default/hidden." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Line = 1, Start = new CsLinePosition(1, 1), End = new CsLinePosition(1, 2), File = "a.cs" }, CsLanguageVersion.CSharp15, "Line directive needs one mapping or default/hidden." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Action = DirectiveAction.Default, Line = 3 }, CsLanguageVersion.CSharp15, "Line directive needs one mapping or default/hidden." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Action = DirectiveAction.Hidden, Start = new CsLinePosition(1, 1), End = new CsLinePosition(1, 2), File = "a.cs" }, CsLanguageVersion.CSharp15, "Line directive needs one mapping or default/hidden." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Action = DirectiveAction.Default, File = "a.cs" }, CsLanguageVersion.CSharp15, "Line directive needs one mapping or default/hidden." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Line = 0 }, CsLanguageVersion.CSharp15, "Mapped line must be positive." };
        yield return new object?[] { Span(new(1, 1), new(2, 1)), CsLanguageVersion.CSharp15, null };
        yield return new object?[] { Span(new(1, 1), new(2, 1)) with { CharacterOffset = 4 }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { Span(new(1, 1), new(2, 1)), CsLanguageVersion.CSharp9, "Enhanced line mapping requires C# 10." };
        yield return new object?[] { Span(new(1, 1), null), CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(0, 1), new(2, 1)), CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(1, 0), new(2, 1)), CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(3, 1), new(2, 1)), CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(1, 1), new(2, 0)), CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(1, 5), new(1, 4)), CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(1, 5), new(1, 5)), CsLanguageVersion.CSharp15, null };
        yield return new object?[] { Span(new(1, 1), new(2, 1)) with { CharacterOffset = -1 }, CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { Span(new(1, 1), new(2, 1)) with { File = null }, CsLanguageVersion.CSharp15, "Invalid line span mapping." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Line = 2, End = new CsLinePosition(2, 1) }, CsLanguageVersion.CSharp15, "Line endpoint requires a start position." };
        yield return new object?[] { new CsDirective(DirectiveKind.Line) { Line = 2, CharacterOffset = 1 }, CsLanguageVersion.CSharp15, "Line endpoint requires a start position." };
        yield return new object?[] { Checksum(), CsLanguageVersion.CSharp15, null };
        yield return new object?[] { Checksum() with { File = null }, CsLanguageVersion.CSharp15, "Invalid checksum directive." };
        yield return new object?[] { Checksum() with { ChecksumAlgorithm = "md5" }, CsLanguageVersion.CSharp15, "Invalid checksum directive." };
        yield return new object?[] { Checksum() with { Checksum = null }, CsLanguageVersion.CSharp15, "Invalid checksum directive." };
        yield return new object?[] { Checksum() with { Checksum = "ABC" }, CsLanguageVersion.CSharp15, "Invalid checksum directive." };
        yield return new object?[] { Checksum() with { Checksum = "ZZ" }, CsLanguageVersion.CSharp15, "Invalid checksum directive." };
        yield return new object?[] { new CsDirective(DirectiveKind.Reference), CsLanguageVersion.CSharp15, "Required node or collection is null." };
        yield return new object?[] { new CsDirective(DirectiveKind.Load) { File = "a.csx" }, CsLanguageVersion.CSharp15, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Sdk) { Text = "Microsoft.NET.Sdk" }, CsLanguageVersion.CSharp14, null };
        yield return new object?[] { new CsDirective(DirectiveKind.Package) { Text = "X@1.0.0" }, CsLanguageVersion.CSharp13, "File-based application directive requires C# 14." };
    }

    static CsDirective Span(CsLinePosition start, CsLinePosition? end) =>
        new(DirectiveKind.Line) { Start = start, End = end, File = "a.cs" };

    static CsDirective Checksum() =>
        new(DirectiveKind.PragmaChecksum) { File = "a.cs", ChecksumAlgorithm = Md5, Checksum = "0A1b" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Directive_shapes_follow_the_documented_rules(CsDirective directive, CsLanguageVersion version, string? message)
    {
        var errors = Validate(directive, version);
        if (message == null) Assert.Empty(errors);
        else Assert.Contains(errors, d => d.Message.Contains(message, StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> Sequences()
    {
        CsDirective If = new(DirectiveKind.If) { Condition = Cs.Id("A") };
        CsDirective Elif = new(DirectiveKind.Elif) { Condition = Cs.Id("B") };
        CsDirective Else = new(DirectiveKind.Else), EndIf = new(DirectiveKind.EndIf), EndRegion = new(DirectiveKind.EndRegion);
        yield return new object[] { new CsNode[] { If, Elif, Else, EndIf, Region, EndRegion }, "" };
        yield return new object[] { new CsNode[] { Elif }, "Elif requires a preceding if and cannot follow else." };
        yield return new object[] { new CsNode[] { If, Else, Elif, EndIf }, "Elif requires a preceding if and cannot follow else." };
        yield return new object[] { new CsNode[] { Else }, "Else requires a preceding if and cannot repeat." };
        yield return new object[] { new CsNode[] { If, Else, Else, EndIf }, "Else requires a preceding if and cannot repeat." };
        yield return new object[] { new CsNode[] { EndIf }, "Endif has no matching if." };
        yield return new object[] { new CsNode[] { EndRegion }, "Endregion has no matching region." };
        yield return new object[] { new CsNode[] { If }, "Conditional or region directive is not closed." };
        yield return new object[] { new CsNode[] { Region }, "Conditional or region directive is not closed." };
    }

    [Theory]
    [MemberData(nameof(Sequences))]
    public void Directive_sequences_must_nest(CsNode[] items, string message)
    {
        var errors = new CsDocument { Items = items }.Validate();
        if (message == "") Assert.Empty(errors);
        else Assert.Contains(errors, d => d.Code == "GEN092" && d.Message == message);
    }

    [Fact]
    public void Parsed_directives_are_ordered_by_source_offset_and_builder_ones_by_document_order()
    {
        var parsed = Cs.Parse("#region A\nclass C { }\n#endregion\n").Root;
        Assert.Empty(parsed.Validate());
        // An added builder directive has no source span, so list order decides; an orphan endregion first is reported.
        var mixed = parsed with { Items = new Values<CsNode>(new CsNode[] { new CsDirective(DirectiveKind.EndRegion) }.Concat(parsed.Items)) };
        Assert.Contains(mixed.Validate(), d => d.Message == "Endregion has no matching region.");
    }
}
