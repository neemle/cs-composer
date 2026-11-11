using Neemle.CSharpComposer.Model;
using System.Text;
namespace Neemle.CSharpComposer.Parsing;
internal static class FragmentParser
{
    internal static ParseResult<CsNode> Parse(CsRaw raw, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = SourceSnapshot.Decode(new UTF8Encoding(false, true).GetBytes(raw.Text), null);
        if (context.Kind == ParseKind.Document)
        {
            var result = DocumentParser.Parse(snapshot, new() { LanguageVersion = context.LanguageVersion });
            return new(result.Root, result.Diagnostics, result.Coverage);
        }
        return DocumentParser.Fragment(snapshot, raw, context);
    }
}
