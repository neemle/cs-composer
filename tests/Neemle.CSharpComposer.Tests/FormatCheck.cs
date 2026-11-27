using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 F4 (AD 84939297 v2 E): every render formats the whole document, so untouched parsed output is no longer the
// source. Where a test pinned untouched output to its source byte for byte, it now pins that the output is a fixed point:
// rendering its re-parse gives the same text (the product's meaning guard has already checked that it reads back as the
// model). An edit is compared with the render of the edited source.
internal static class FormatCheck
{
    internal static string FixedPoint(CsDocument document, RenderOptions? options = null)
    {
        string text = document.Render(options);
        Assert.Equal(text, Cs.Parse(text, new() { LanguageVersion = document.LanguageVersion }).Root.Render(options));
        return text;
    }

    internal static string FixedPoint(CsTree tree)
    {
        string text = tree.Render();
        Assert.Equal(text, Cs.ParseTree(text, tree.LanguageVersion).Root.Render());
        return text;
    }

    internal static string Of(string source) => Cs.Parse(source).Root.Render();

    internal static string OfTree(string source) => Cs.ParseTree(source).Root.Render();
}
