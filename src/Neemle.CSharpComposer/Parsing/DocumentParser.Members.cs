using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;
internal sealed partial class DocumentParser
{
    CsTypeDeclaration TypeDeclaration(int start, Values<CsAttribute> attributes, Values<string> modifiers, int depth)
    {
        Guard(depth);
        string keyword = tokens[p++].Text;
        TypeKind kind = keyword switch { "class" => TypeKind.Class, "struct" => TypeKind.Struct,
            "interface" => TypeKind.Interface, "enum" => TypeKind.Enum, _ => TypeKind.Record };
        if (keyword == "record" && Take("class")) kind = TypeKind.RecordClass;
        else if (keyword == "record" && Take("struct")) kind = TypeKind.RecordStruct;
        var name = Current;
        Identifier();
        var generics = Generics();
        var primary = Current.Text == "(" ? Parameters() : Values<CsParameter>.Empty;
        var bases = new List<CsType>();
        if (Take(":")) do { bases.Add(TypeName()); } while (Take(","));
        generics = Constraints(generics);
        int open = -1, close = -1;
        var members = new List<CsMember>();
        if (!Take(";"))
        {
            open = Expect("{").End;
            while (Current.Text is not ("}" or ""))
            {
                int before = p;
                try { members.Add(kind == TypeKind.Enum ? EnumMember() : Member(name.Text, kind, depth + 1)); }
                catch (ParseFailure failure)
                {
                    p = before;
                    var raw = RecoverNode(failure.Message, false);
                    members.Add(new CsRawMember(raw.Text) { Origin = raw.Origin, Comments = raw.Comments });
                }
                if (p <= before) throw new InvalidOperationException("Member parser did not advance.");
            }
            close = Expect("}").Start;
            Take(";");
        }
        var type = new CsTypeDeclaration(kind, name.Text) { Attributes = attributes, Modifiers = modifiers,
            TypeParameters = generics, PrimaryParameters = primary, BaseTypes = new(bases), Members = new(members) };
        type = Bind(type, start, "Name", name.Start, name.Text.Length, open, close);
        return type with { Comments = new(type.Comments.Concat(ContainerComments(open, close, type.Children))) };
    }
    CsEnumMember EnumMember()
    {
        int start = p;
        var attributes = Attributes();
        var name = Current;
        Identifier();
        var value = Take("=") ? Expression() : null;
        if (!Take(",") && Current.Text != "}") throw new ParseFailure("Enum comma expected.");
        return Bind(new CsEnumMember(name.Text, value) { Attributes = attributes }, start, "Name", name.Start, name.Text.Length);
    }
    CsMember Member(string owner, TypeKind kind, int depth)
    {
        Guard(depth);
        int start = p;
        var attributes = Attributes();
        var modifiers = Modifiers();
        if (IsTypeKeyword(Current.Text)) return TypeDeclaration(start, attributes, modifiers, depth + 1);
        bool isEvent = Take("event");
        if (!isEvent && Current.Text.TrimStart('@') == owner.TrimStart('@') && Peek(1) == "(")
            return Constructor(start, attributes, modifiers, depth);
        var type = TypeName();
        var name = Current;
        Identifier();
        CsMember member;
        if (Current.Text is "(" or "<") member = Method(type, name.Text, depth);
        else if (Current.Text is "{" or "=>") member = Property(type, name.Text, isEvent, depth);
        else
        {
            var initial = Take("=") ? Expression() : null;
            Expect(";");
            member = isEvent ? new CsEvent(type, name.Text) { Initializer = initial } :
                new CsField(type, name.Text) { Initializer = initial };
        }
        member = member with { Attributes = attributes, Modifiers = modifiers };
        return Bind(member, start, "Name", name.Start, name.Text.Length);
    }
    CsMethod Method(CsType type, string name, int depth)
    {
        var generic = Generics();
        var parameters = Parameters();
        generic = Constraints(generic);
        return new(type, name) { Parameters = parameters, TypeParameters = generic, Body = Body(depth) };
    }
    CsConstructor Constructor(int start, Values<CsAttribute> attributes, Values<string> modifiers, int depth)
    {
        var name = Current; p++;
        var parameters = Parameters();
        string? initializer = null;
        var arguments = new List<CsExpression>();
        if (Take(":"))
        {
            initializer = Identifier();
            Expect("(");
            if (Current.Text != ")") do { arguments.Add(Expression()); } while (Take(","));
            Expect(")");
        }
        var body = Body(depth) ?? throw new ParseFailure("Constructor body required.");
        return Bind(new CsConstructor(name.Text) { Attributes = attributes, Modifiers = modifiers,
            Parameters = parameters, InitializerKind = initializer, InitializerArguments = new(arguments), Body = body },
            start, "Name", name.Start, name.Text.Length);
    }
    CsMember Property(CsType type, string name, bool isEvent, int depth)
    {
        if (Take("=>"))
        {
            var expression = Expression(); Expect(";");
            if (isEvent) throw new ParseFailure("Expression-bodied event is unsupported.");
            return new CsProperty(type, name) { Expression = expression };
        }
        Expect("{");
        var accessors = new List<CsAccessor>();
        while (Current.Text is not ("}" or ""))
        {
            int start = p;
            var attributes = Attributes();
            var modifiers = Modifiers();
            string accessor = Identifier();
            if (accessor is not ("get" or "set" or "init" or "add" or "remove"))
                throw new ParseFailure("Unknown accessor.");
            accessors.Add(Bind(new CsAccessor(accessor) { Accessibility = modifiers.Count == 0 ? null :
                string.Join(" ", modifiers), Attributes = attributes, Body = Body(depth + 1) }, start));
        }
        Expect("}");
        CsExpression? initializer = null;
        if (Take("=")) { initializer = Expression(); Expect(";"); }
        return isEvent ? new CsEvent(type, name) { Accessors = new(accessors), Initializer = initializer } :
            new CsProperty(type, name) { Accessors = new(accessors), Initializer = initializer };
    }
}
