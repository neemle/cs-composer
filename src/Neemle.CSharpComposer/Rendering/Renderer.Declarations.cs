using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Rendering;

internal sealed partial class Renderer
{
    static string InterfacePrefix(CsMember member) => member.ExplicitInterface == null ? "" : Names.Type(member.ExplicitInterface) + ".";

    void Delegate(CsDelegate d)
    {
        string signature = Prefix(d) + "delegate " + Names.Type(d.ReturnType) + " " + Names.Id(d.Name) + Names.Generics(d.TypeParameters) +
            "(" + string.Join(", ", d.Parameters.Select(Names.Parameter)) + ")";
        if (d.TypeParameters.Any(p => p.Constraints.Count > 0))
        {
            Line(signature); depth++; Constraints(d.TypeParameters); depth--; Line(";");
        }
        else Line(signature + ";");
    }

    void Operator(CsOperatorDeclaration o)
    {
        bool conversion = o.Operator is OverloadableOperator.Implicit or OverloadableOperator.Explicit;
        string signature = Prefix(o) + (conversion ? OperatorWriter.Token(o.Operator) + " " + InterfacePrefix(o) + "operator " + (o.IsChecked ? "checked " : "") + Names.Type(o.ReturnType)
            : Names.Type(o.ReturnType) + " " + InterfacePrefix(o) + "operator " + (o.IsChecked ? "checked " : "") + OperatorWriter.Token(o.Operator));
        Body(o.Body, signature + "(" + string.Join(", ", o.Parameters.Select(Names.Parameter)) + ")");
    }

    void Extension(CsExtension e)
    {
        Line("extension" + Names.Generics(e.TypeParameters) + "(" + string.Concat(e.ReceiverAttributes.Select(a => Names.Attribute(a) + " ")) + (e.ReceiverModifier == null ? "" : e.ReceiverModifier + " ") +
            Names.Type(e.ReceiverType) + (e.ReceiverName == null ? "" : " " + Names.Id(e.ReceiverName)) + ")");
        depth++; Constraints(e.TypeParameters); depth--;
        Open(); Content(e, e.Members, n => Member((CsMember)n)); Close();
    }
}
