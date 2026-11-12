using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsDelegate DelegateDeclaration(int start, Values<CsAttribute> attributes, Values<string> modifiers, int depth)
    {
        Expect("delegate"); var type = TypeName(depth + 1); string name = Identifier();
        var generics = Generics(); var parameters = Parameters(); generics = Constraints(generics); Expect(";");
        return Bind(new CsDelegate(type, name) { Attributes = attributes, Modifiers = modifiers, TypeParameters = generics, Parameters = parameters }, start);
    }

    CsExtension ExtensionDeclaration(int start, int depth)
    {
        Expect("extension"); var generics = Generics(); Expect("("); var attributes = Attributes(); string? modifier = ParameterModifier();
        var type = TypeName(depth + 1); string? name = Current.Text == ")" ? null : Identifier(); Expect(")");
        generics = Constraints(generics); int open = Expect("{").End; var members = new List<CsMember>();
        while (Current.Text is not ("}" or "")) members.Add(Member("", TypeKind.Class, depth + 1));
        int close = Expect("}").Start;
        var extension = new CsExtension(type, name) { ReceiverModifier = modifier, ReceiverAttributes = attributes, TypeParameters = generics, Members = new(members) };
        return Bind(extension with { Comments = ContainerComments(open, close, extension.Members) }, start, listStart: open, listEnd: close);
    }

    CsOperatorDeclaration OperatorDeclaration(CsType? type, int start, int depth, CsType? explicitInterface = null)
    {
        OverloadableOperator op;
        bool conversion = type == null;
        if (conversion) { op = Take("implicit") ? OverloadableOperator.Implicit : OverloadableOperator.Explicit; if (op == OverloadableOperator.Explicit) Expect("explicit"); }
        else op = OverloadableOperator.Plus;
        if (conversion && Current.Text != "operator")
        {
            var member = MemberName(depth + 1);
            if (member.Name.Text != "operator") throw new ParseFailure("Explicit interface operator expected.");
            explicitInterface = member.Interface; p--;
        }
        Expect("operator"); bool check = Take("checked");
        if (conversion) type = TypeName(depth + 1);
        else
        {
            string token = OperatorToken(out int width);
            var found = Enum.GetValues<OverloadableOperator>().Where(o => OperatorWriter.Token(o) == token).ToArray();
            if (found.Length == 0) throw new ParseFailure("Overloadable operator expected.");
            op = found[0]; p += width;
        }
        var parameters = Parameters(); var body = Body(depth + 1);
        return Bind(new CsOperatorDeclaration(op, type!) { IsChecked = check, Parameters = parameters, Body = body, ExplicitInterface = explicitInterface }, start);
    }

    (Token Name, CsType? Interface) MemberName(int depth)
    {
        Token name = Current; Identifier();
        CsType? initial = null;
        if (Take("::")) { string alias = name.Text; name = Current; Identifier(); initial = new(alias + "::" + name.Text); }
        CsType? owner = null;
        while (true)
        {
            int afterName = p;
            CsType part = initial ?? new(name.Text); initial = null;
            try { part = TypeArguments(part, depth + 1); }
            catch (ParseFailure) { p = afterName; }
            if (!Take(".")) { p = afterName; return (name, owner); }
            owner = owner == null ? part : part.Form is TypeForm.Generic or TypeForm.UnboundGeneric
                ? part with { Element = new CsType(name.Text) { Form = TypeForm.Nested, Element = owner } }
                : new CsType(name.Text) { Form = TypeForm.Nested, Element = owner };
            name = Current; Identifier();
        }
    }
}
