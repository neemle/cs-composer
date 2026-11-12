using Neemle.CSharpComposer.Model;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    CsTypeDeclaration TypeDeclaration(int start, Values<CsAttribute> attributes, Values<string> modifiers, int depth)
    {
        Guard(depth);
        string keyword = tokens[p++].Text;
        TypeKind kind = keyword switch
        {
            "class" => TypeKind.Class,
            "struct" => TypeKind.Struct,
            "interface" => TypeKind.Interface,
            "enum" => TypeKind.Enum,
            "union" => TypeKind.Union,
            _ => TypeKind.Record
        };
        if (keyword == "record" && Take("class"))
        {
            kind = TypeKind.RecordClass;
            if (version < CsLanguageVersion.CSharp10) Error("GEN073", "Explicit record class syntax requires C# 10.", Last.Start);
        }
        else if (keyword == "record" && Take("struct")) kind = TypeKind.RecordStruct;
        var name = Current;
        Identifier();
        var generics = Generics();
        var cases = new List<CsType>();
        if (kind == TypeKind.Union) { Expect("("); do { cases.Add(TypeName(depth + 1)); } while (Take(",")); Expect(")"); }
        bool hasPrimary = Current.Text == "(";
        var primary = hasPrimary ? Parameters() : Values<CsParameter>.Empty;
        Values<CsExpression>? baseArguments = null;
        var bases = new List<CsType>();
        if (Take(":")) do
            {
                bases.Add(TypeName());
                if (Current.Text == "(") { Expect("("); baseArguments = new(ArgumentList(depth + 1)); Expect(")"); }
            } while (Take(","));
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
        var type = new CsTypeDeclaration(kind, name.Text)
        {
            Attributes = attributes,
            Modifiers = modifiers,
            UnionCases = new(cases),
            HasPrimaryConstructor = hasPrimary,
            BaseArguments = baseArguments,
            TypeParameters = generics,
            PrimaryParameters = primary,
            BaseTypes = new(bases),
            Members = new(members)
        };
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
        bool previous = inAsync;
        try { return MemberCore(owner, kind, depth); }
        finally { inAsync = previous; }
    }

    CsMember MemberCore(string owner, TypeKind kind, int depth)
    {
        Guard(depth);
        int start = p;
        if (Current.IsDisabled) { string text = tokens[p++].Text; return Bind(new CsDisabledText(text), start); }
        if (Current.Text.StartsWith('#')) return Directive();
        var attributes = Attributes();
        var modifiers = Modifiers(owner);
        inAsync = modifiers.Contains("async");
        if (Current.Text == "delegate" && Peek(1) != "*") return DelegateDeclaration(start, attributes, modifiers, depth + 1);
        if (Current.Text == "extension") return ExtensionDeclaration(start, depth + 1) with { Attributes = attributes, Modifiers = modifiers };
        if (Take("~"))
        {
            string destructorName = Identifier(); Expect("("); Expect(")");
            var destructorBody = Body(depth + 1);
            if (destructorBody == null && !modifiers.Contains("extern")) throw new ParseFailure("Destructor requires a body.");
            return Bind(new CsDestructor(destructorName, destructorBody) { Attributes = attributes, Modifiers = modifiers }, start);
        }
        if (Current.Text is "implicit" or "explicit") return OperatorDeclaration(null, start, depth + 1) with { Attributes = attributes, Modifiers = modifiers };
        if (IsTypeKeyword(Current.Text)) return TypeDeclaration(start, attributes, modifiers, depth + 1);
        bool isEvent = Take("event");
        if (!isEvent && Neemle.CSharpComposer.Rendering.Names.Key(Current.Text) == Neemle.CSharpComposer.Rendering.Names.Key(owner) && Peek(1) == "(")
            return Constructor(start, attributes, modifiers, depth);
        var type = TypeName();
        if (Current.Text == "operator") return OperatorDeclaration(type, start, depth + 1) with { Attributes = attributes, Modifiers = modifiers };
        var (name, explicitInterface) = MemberName(depth + 1);
        CsMember member;
        if (name.Text == "operator")
        {
            p--; return OperatorDeclaration(type, start, depth + 1, explicitInterface) with { Attributes = attributes, Modifiers = modifiers };
        }
        if (name.Text == "this" && Current.Text == "[")
        {
            var parameters = Parameters("[", "]");
            member = ((CsProperty)Property(type, "this", false, depth)) with { IsIndexer = true, Parameters = parameters };
        }
        else if (Current.Text is "(" or "<") member = Method(type, name.Text, depth);
        else if (Current.Text is "{" or "=>") member = Property(type, name.Text, isEvent, depth);
        else
        {
            CsExpression? fixedSize = null;
            if (Take("[")) { fixedSize = Expression(); Expect("]"); }
            var initial = Take("=") ? Expression() : null;
            var additional = new List<CsVariableDeclarator>();
            while (Take(","))
            {
                int variableStart = p; var variableName = Current; Identifier();
                CsExpression? size = null; if (Take("[")) { size = Expression(); Expect("]"); }
                var value = Take("=") ? Expression() : null;
                additional.Add(Bind(new CsVariableDeclarator(variableName.Text, value) { FixedSize = size }, variableStart, "Name", variableName.Start, variableName.Text.Length));
            }
            Expect(";");
            member = isEvent ? new CsEvent(type, name.Text) { Initializer = initial, AdditionalVariables = new(additional) } :
                new CsField(type, name.Text) { FixedSize = fixedSize, Initializer = initial, AdditionalVariables = new(additional) };
        }
        member = member with { Attributes = attributes, Modifiers = modifiers, ExplicitInterface = explicitInterface };
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
            if (Current.Text != ")") do { arguments.Add(ArgumentExpression(depth + 1)); } while (Take(","));
            Expect(")");
        }
        var body = Body(depth);
        if (body == null && !modifiers.Any(m => m is "extern" or "partial")) throw new ParseFailure("Constructor body required.");
        return Bind(new CsConstructor(name.Text)
        {
            Attributes = attributes,
            Modifiers = modifiers,
            Parameters = parameters,
            InitializerKind = initializer,
            InitializerArguments = new(arguments),
            Body = body
        },
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
            bool previousAccessor = inAccessor; inAccessor = !isEvent;
            CsBody? accessorBody;
            try { accessorBody = Body(depth + 1); } finally { inAccessor = previousAccessor; }
            accessors.Add(Bind(new CsAccessor(accessor)
            {
                Accessibility = modifiers.All(m => m == "readonly") ? null :
                string.Join(" ", modifiers.Where(m => m != "readonly")),
                Modifiers = new(modifiers.Where(m => m == "readonly")),
                Attributes = attributes,
                Body = accessorBody
            }, start));
        }
        Expect("}");
        CsExpression? initializer = null;
        if (Take("=")) { initializer = Expression(); Expect(";"); }
        return isEvent ? new CsEvent(type, name) { Accessors = new(accessors), Initializer = initializer } :
            new CsProperty(type, name) { Accessors = new(accessors), Initializer = initializer };
    }
}
