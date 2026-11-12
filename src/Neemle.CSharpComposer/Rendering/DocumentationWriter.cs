using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal static class DocumentationWriter
{
    internal static string Comment(CsComment comment)
    {
        if (comment.Documentation == null) return comment.Text;
        string text = Xml(comment.Documentation);
        return comment.BlockDocumentation ? "/**" + text + "*/" : string.Join("\n", text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u0085', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n').Split('\n').Select(line => "/// " + line));
    }

    static string Escape(string text, bool attribute = false)
    {
        string escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", attribute ? "&quot;" : "\"")
            .Replace("\r", "&#xD;").Replace("\u0085", "&#x85;").Replace("\u2028", "&#x2028;").Replace("\u2029", "&#x2029;");
        return attribute ? escaped.Replace("\n", "&#xA;").Replace("\t", "&#x9;") : escaped;
    }
    internal static string Xml(CsXmlNode node)
    {
        string content = string.Concat(node.Content.Select(Xml));
        string attributes = string.Concat(node.Attributes.Select(a => " " + a.Name + "=\"" + Escape(a.Reference == null ? a.Value! : Cref(a.Reference), attribute: true) + "\""));
        return node.Kind switch
        {
            XmlNodeKind.Fragment => content,
            XmlNodeKind.Element => "<" + node.Name + attributes + ">" + content + "</" + node.Name + ">",
            XmlNodeKind.EmptyElement => "<" + node.Name + attributes + " />",
            XmlNodeKind.Text => Escape(node.Value!),
            XmlNodeKind.CData => "<![CDATA[" + node.Value + "]]>",
            XmlNodeKind.Comment => "<!--" + node.Value + "-->",
            XmlNodeKind.ProcessingInstruction => "<?" + node.Name + (string.IsNullOrEmpty(node.Value) ? "" : " " + node.Value) + "?>",
            _ => throw new ArgumentOutOfRangeException(nameof(node))
        };
    }

    internal static string Cref(CsCref reference)
    {
        string Type(CsType t) => Names.Type(t).Replace('<', '{').Replace('>', '}');
        string qualifier = reference.Qualifier == null ? "" : Type(reference.Qualifier) + ".";
        string head = reference.Kind switch
        {
            CrefKind.Name => Type(reference.Type!),
            CrefKind.Indexer => qualifier + "this",
            CrefKind.Operator => qualifier + "operator " + (reference.IsChecked ? "checked " : "") + OperatorWriter.Token(reference.Operator),
            CrefKind.Conversion => qualifier + OperatorWriter.Token(reference.Operator) + " operator " + (reference.IsChecked ? "checked " : "") + Type(reference.Type!),
            CrefKind.Extension => qualifier + "extension" + (reference.TypeArguments.Count == 0 ? "" : "{" + string.Join(", ", reference.TypeArguments.Select(Type)) + "}"),
            _ => throw new ArgumentOutOfRangeException(nameof(reference))
        };
        string parameters = reference.Parameters == null ? "" : (reference.Kind == CrefKind.Indexer ? "[" : "(") +
            string.Join(", ", reference.Parameters.Select(p => (p.Modifier == null ? "" : p.Modifier + " ") + Type(p.Type))) + (reference.Kind == CrefKind.Indexer ? "]" : ")");
        return head + parameters + (reference.Member == null ? "" : "." + Cref(reference.Member));
    }
}
