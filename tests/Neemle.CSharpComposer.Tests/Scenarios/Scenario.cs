using System.Text;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// GENE-25: one row of public-API behaviour with its exact expected outcome. The same rows run twice:
// ScenarioUnitTests checks the outcome in memory, and ScenarioIntegrationTests runs the row end to end, writing every
// document it produces to a stream and parsing the written bytes back, so each category reaches the code on its own.
public sealed record Scenario(string Id, Func<object?> Act, string Expected);

public static class Outcome
{
    // In memory: a document or tree renders (or lists its diagnostics); any other value prints; a throw names its type.
    public static string Of(Func<object?> act)
    {
        try { return Describe(act(), stream: false); }
        catch (Exception error) when (error is not Xunit.Sdk.XunitException) { return Thrown(error); }
    }

    // End to end: a document is written to a stream and the written bytes are parsed back and must round-trip; a tree
    // is rendered to UTF-8 bytes and parsed back as statements. Invalid models must refuse the write and write nothing.
    public static string ThroughStream(Func<object?> act)
    {
        try { return Describe(act(), stream: true); }
        catch (Exception error) when (error is not Xunit.Sdk.XunitException) { return Thrown(error); }
    }

    static string Thrown(Exception error) => "throws " + error.GetType().Name + ": " + error.Message;

    static string Invalid(IEnumerable<GenDiagnostic> errors) =>
        "invalid: " + string.Join(" | ", errors.Select(e => e.Code + " " + e.Message).Distinct());

    static string Describe(object? value, bool stream) => value switch
    {
        null => "null",
        CsDocument document => stream ? Written(document) : document.Validate() is { Count: > 0 } errors ? Invalid(errors) : Rendered(document.Render),
        CsTree tree => stream ? Written(tree) : tree.Validate() is { Count: > 0 } errors ? Invalid(errors) : Rendered(tree.Render),
        IEnumerable<GenDiagnostic> errors => Invalid(errors),
        string text => text,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.GetType().Name,
    };

    // Render validates again, with the render options' own feature checks; a refusal there is an invalid outcome too.
    static string Rendered(Func<Rendering.RenderOptions?, string> render)
    {
        try { return render(null); }
        catch (GenValidationException error) { return Invalid(error.Diagnostics); }
    }

    static string Written(CsDocument document)
    {
        using var output = new MemoryStream();
        try { document.WriteTo(output); }
        catch (GenValidationException error)
        {
            Assert.Equal(0, output.Length);
            return Invalid(error.Diagnostics);
        }
        byte[] bytes = output.ToArray();
        output.Position = 0;
        var reparsed = Cs.Parse(output, new() { LanguageVersion = document.LanguageVersion });
        Assert.Equal(bytes, reparsed.Root.ToBytes());
        return new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEnd();
    }

    static string Written(CsTree tree)
    {
        string text;
        try { text = tree.Render(); }
        catch (GenValidationException error) { return Invalid(error.Diagnostics); }
        using var output = new MemoryStream(Encoding.UTF8.GetBytes(text));
        string read = new StreamReader(output, Encoding.UTF8).ReadToEnd();
        Assert.Equal(read, Cs.ParseTree(read, tree.LanguageVersion).Root.Render());
        return read;
    }
}
