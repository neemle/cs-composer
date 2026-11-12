using System.Text;
using System.Xml;
using System.Xml.Linq;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Parsing;

internal static class DocumentationParser
{
    internal static CsComment Parse(CsComment comment, CsLanguageVersion version)
    {
        bool block = comment.Text.StartsWith("/**", StringComparison.Ordinal);
        string text = block ? comment.Text[3..^2] : string.Join("\n", comment.Text.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            string content = line.TrimStart()[3..]; return content.StartsWith(' ') ? content[1..] : content;
        }));
        try
        {
            using var reader = XmlReader.Create(new StringReader("<root>" + text + "</root>"), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
            var root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
            CsXmlNode Node(XNode node, int depth = 0)
            {
                if (depth > 128) throw new XmlException("Documentation nesting exceeds 128.");
                return node switch
                {
                    XElement element => new(element.IsEmpty ? XmlNodeKind.EmptyElement : XmlNodeKind.Element, element.GetPrefixOfNamespace(element.Name.Namespace) is { Length: > 0 } prefix ? prefix + ":" + element.Name.LocalName : element.Name.LocalName)
                    {
                        Attributes = new(element.Attributes().Select(a =>
                        {
                            string name = a.Name.NamespaceName == "http://www.w3.org/2000/xmlns/" ? "xmlns:" + a.Name.LocalName : element.GetPrefixOfNamespace(a.Name.Namespace) is { Length: > 0 } ap ? ap + ":" + a.Name.LocalName : a.Name.LocalName;
                            var reference = name == "cref" ? DocumentParser.DocumentationReference(a.Value, version) : null;
                            return new CsXmlAttribute(name, reference == null ? a.Value : null, reference);
                        })),
                        Content = new(element.Nodes().Select(n => Node(n, depth + 1)))
                    },
                    XCData data => new(XmlNodeKind.CData, Value: data.Value),
                    XText value => new(XmlNodeKind.Text, Value: value.Value),
                    XComment value => new(XmlNodeKind.Comment, Value: value.Value),
                    XProcessingInstruction instruction => new(XmlNodeKind.ProcessingInstruction, instruction.Target, instruction.Data),
                    _ => throw new XmlException("Unsupported XML documentation node.")
                };
            }
            return comment with { Documentation = new(XmlNodeKind.Fragment) { Content = new(root.Nodes().Select(n => Node(n))) }, BlockDocumentation = block };
        }
        catch (XmlException) { return comment; }
    }
}
