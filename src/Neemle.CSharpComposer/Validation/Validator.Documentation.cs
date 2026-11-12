using System.Xml;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    void Documentation(CsNode root, int depth = 0)
    {
        if (depth > 128) return;
        string path = "Documentation";
        void Name(string? name) { try { XmlConvert.VerifyName(name!); } catch (Exception e) when (e is XmlException or ArgumentException) { Error("GEN092", path, "Invalid XML name."); } }
        if (root is CsComment c && (c.Documentation != null || c.BlockDocumentation) && c.Kind != CommentKind.Documentation) Error("GEN065", path, "XML payload belongs to documentation comments.");
        if (root is CsXmlNode x)
        {
            if (!Enum.IsDefined(x.Kind)) Error("GEN092", path, "Unknown XML node kind.");
            bool named = x.Kind is XmlNodeKind.Element or XmlNodeKind.EmptyElement or XmlNodeKind.ProcessingInstruction;
            bool value = x.Kind is XmlNodeKind.Text or XmlNodeKind.CData or XmlNodeKind.Comment or XmlNodeKind.ProcessingInstruction;
            bool container = x.Kind is XmlNodeKind.Element or XmlNodeKind.Fragment;
            if (named) Name(x.Name);
            if (x.Kind == XmlNodeKind.ProcessingInstruction && string.Equals(x.Name, "xml", StringComparison.OrdinalIgnoreCase)) Error("GEN092", path, "Processing instruction target cannot be xml.");
            if (value) Required(x.Value, path + ".Value");
            if (!named && x.Name != null || !value && x.Value != null || !container && x.Content.Count > 0 || x.Kind is not (XmlNodeKind.Element or XmlNodeKind.EmptyElement) && x.Attributes.Count > 0) Error("GEN065", path, "Payload belongs to another XML kind.");
            if (x.Kind == XmlNodeKind.CData && x.Value?.Contains("]]>", StringComparison.Ordinal) == true || x.Kind == XmlNodeKind.Comment && (x.Value?.Contains("--", StringComparison.Ordinal) == true || x.Value?.EndsWith('-') == true) || x.Kind == XmlNodeKind.ProcessingInstruction && x.Value?.Contains("?>", StringComparison.Ordinal) == true) Error("GEN092", path, "XML value contains its closing delimiter.");
            if (x.Attributes.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != x.Attributes.Count) Error("GEN092", path, "Duplicate XML attributes.");
        }
        if (root is CsXmlAttribute a)
        {
            Name(a.Name);
            if ((a.Value == null) == (a.Reference == null) || a.Reference != null && a.Name != "cref") Error("GEN065", path, "Attribute needs text or a cref reference.");
        }
        if (root is CsCref r)
        {
            if (r.Kind == CrefKind.Name && r.Qualifier != null || r.Kind is not (CrefKind.Operator or CrefKind.Conversion) && (r.IsChecked || r.Operator != OverloadableOperator.Plus)) Error("GEN065", path, "Cref payload belongs to another reference kind.");
            if (r.Kind is CrefKind.Operator or CrefKind.Conversion && !Enum.IsDefined(r.Operator)) Error("GEN092", path, "Unknown cref operator.");
            if (!Enum.IsDefined(r.Kind)) Error("GEN092", path, "Unknown cref kind.");
            bool typed = r.Kind is CrefKind.Name or CrefKind.Conversion;
            if (typed) Type(r.Type, path + ".Type", depth + 1); else if (r.Type != null) Error("GEN065", path, "Type belongs to a named or conversion cref.");
            if (r.Qualifier != null) Type(r.Qualifier, path + ".Qualifier", depth + 1);
            List(r.TypeArguments, path + ".TypeArguments", (type, child) => Type(type, child, depth + 1));
            if (r.Parameters != null) List(r.Parameters, path + ".Parameters", (parameter, child) => { Type(parameter.Type, child + ".Type", depth + 1); if (parameter.Modifier != null && parameter.Modifier is not ("ref" or "out" or "in" or "ref readonly")) Error("GEN016", child, "Invalid cref parameter modifier."); });
            if (r.Kind == CrefKind.Extension)
            {
                Feature("Extension member cref", CsLanguageVersion.CSharp14, path);
                Required(r.Member, path + ".Member"); Required(r.Parameters, path + ".Parameters");
            }
            else if (r.Member != null || r.TypeArguments.Count > 0) Error("GEN065", path, "Nested cref belongs to an extension reference.");
            if (r.Kind == CrefKind.Conversion && r.Operator is not (OverloadableOperator.Implicit or OverloadableOperator.Explicit)) Error("GEN092", path, "Conversion cref requires implicit or explicit.");
            if (r.IsChecked) Feature("Checked operator cref", CsLanguageVersion.CSharp11, path);
        }
        foreach (var child in root.Children) Documentation(child, depth + 1);
    }
}
