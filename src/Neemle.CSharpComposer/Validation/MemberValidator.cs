using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

internal sealed class MemberValidator
{
    readonly Validator validation;

    internal MemberValidator(Validator validation) { this.validation = validation; }

    internal void Validate(CsMember member, string path, CsTypeDeclaration? parent, int depth)
    {
        if (!validation.Depth(depth, path)) return;
        if (member is not CsRawMember) validation.Identifier(member.Name, path + ".Name");
        validation.Attributes(member.Attributes, path + ".Attributes", depth + 1);
        if (!validation.Required(member.Modifiers, path + ".Modifiers")) return;
        MemberFeatures.Validate(validation, member, path, parent);
        bool interfaceParent = parent?.Kind == TypeKind.Interface;
        CommonModifiers(member, path, interfaceParent);
        ParentKind(member, path, parent);
        Declaration(member, path, parent, depth, interfaceParent);
    }

    void Declaration(CsMember member, string path, CsTypeDeclaration? parent, int depth, bool interfaceParent)
    {
        switch (member)
        {
            case CsTypeDeclaration type: Type(type, path, parent, depth); break;
            case CsProperty property: Property(property, path, interfaceParent, depth); break;
            case CsField field: Field(field, path, interfaceParent, depth); break;
            case CsMethod method: Method(method, path, interfaceParent, depth); break;
            case CsConstructor constructor: Constructor(constructor, path, parent, depth, interfaceParent); break;
            case CsEvent @event: Event(@event, path, interfaceParent, depth); break;
            case CsEnumMember value: Initializer(value.Value, path + ".Value", depth); break;
            case CsRawMember: break;
            default: validation.Error("GEN055", path, "Unsupported member node."); break;
        }
    }

    void CommonModifiers(CsMember member, string path, bool interfaceParent)
    {
        var modifiers = member.Modifiers;
        string allowed = ModifierRules.Allowed(member);
        foreach (string modifier in modifiers)
            if (!allowed.Split(' ').Contains(modifier))
                validation.Error("GEN031", path + ".Modifiers",
                    "Modifier '" + modifier + "' is not valid for this construct.");
        if (modifiers.Count != modifiers.Distinct(StringComparer.Ordinal).Count())
            validation.Error("GEN032", path + ".Modifiers", "Duplicate modifier.");
        if (ModifierRules.ConflictingAccess(modifiers))
            validation.Error("GEN033", path, "Conflicting accessibility modifiers.");
        bool staticInterfaceMember = interfaceParent && member is not CsTypeDeclaration;
        if (ModifierRules.Conflicting(modifiers, staticInterfaceMember))
            validation.Error("GEN034", path, "Conflicting modifiers.");
        if (modifiers.Contains("sealed") && member is not CsTypeDeclaration && !modifiers.Contains("override"))
            validation.Error("GEN035", path, "Sealed member must override.");
    }

    void ParentKind(CsMember member, string path, CsTypeDeclaration? parent)
    {
        if (parent?.Kind == TypeKind.Enum && member is not CsEnumMember)
            validation.Error("GEN036", path, "Enum may contain only enum members.");
        if (parent != null && parent.Kind != TypeKind.Enum && member is CsEnumMember)
            validation.Error("GEN037", path, "Enum member requires an enum parent.");
    }

    void Type(CsTypeDeclaration type, string path, CsTypeDeclaration? parent, int depth)
    {
        if (!Enum.IsDefined(typeof(TypeKind), type.Kind))
            validation.Error("GEN038", path, "Unknown declaration kind.");
        TypeModifiers(type, path, parent);
        TypeParameters(type, path);
        validation.Parameters(type.PrimaryParameters, path + ".PrimaryParameters", depth + 1);
        validation.Generics(type.TypeParameters, path + ".TypeParameters", depth + 1,
            type.Kind == TypeKind.Interface);
        validation.List(type.BaseTypes, path + ".BaseTypes",
            (baseType, child) => validation.Type(baseType, child, depth + 1));
        validation.List(type.Members, path + ".Members", (member, child) => Validate(member, child, type, depth + 1));
    }

    void TypeModifiers(CsTypeDeclaration type, string path, CsTypeDeclaration? parent)
    {
        var modifiers = type.Modifiers;
        bool structure = type.Kind == TypeKind.Struct || type.Kind == TypeKind.RecordStruct;
        if (parent == null && ModifierRules.HasRestrictedTopLevelAccess(modifiers))
            validation.Error("GEN062", path, "Top-level type cannot be private, protected, or new.");
        if (structure && (modifiers.Contains("abstract") || modifiers.Contains("sealed")))
            validation.Error("GEN063", path, "Struct declaration cannot be abstract or sealed.");
        if ((modifiers.Contains("ref") || modifiers.Contains("readonly")) && !structure)
            validation.Error("GEN039", path, "Ref/readonly declaration must be a struct.");
        if (modifiers.Contains("static") && type.Kind != TypeKind.Class)
            validation.Error("GEN040", path, "Only class declarations can be static.");
        if (modifiers.Contains("ref") && type.Kind == TypeKind.RecordStruct)
            validation.Error("GEN041", path, "Record struct cannot be ref.");
    }

    void TypeParameters(CsTypeDeclaration type, string path)
    {
        bool record = type.Kind == TypeKind.Record || type.Kind == TypeKind.RecordClass ||
            type.Kind == TypeKind.RecordStruct;
        if (type.PrimaryParameters != null && type.PrimaryParameters.Count > 0 && !record)
            validation.Error("GEN042", path, "C# 11 primary parameters require a record.");
        if (type.Kind == TypeKind.Enum && type.TypeParameters?.Count > 0)
            validation.Error("GEN043", path, "Enums cannot be generic.");
    }

    void Property(CsProperty property, string path, bool interfaceParent, int depth)
    {
        var modifiers = property.Modifiers;
        validation.Type(property.Type, path + ".Type", depth + 1);
        validation.Accessors(property.Accessors, path + ".Accessors", depth + 1, false, modifiers.Contains("abstract"));
        PropertyShape(property, path, depth);
        if (modifiers.Contains("abstract") && (property.Expression != null || property.Initializer != null))
            validation.Error("GEN046", path, "Abstract property cannot have implementation or initializer.");
        if (modifiers.Contains("required") && InvalidRequiredProperty(property))
            validation.Error("GEN047", path, "Required property must be a writable instance property.");
        if (interfaceParent && !modifiers.Contains("static") && property.Initializer != null)
            validation.Error("GEN058", path, "Interface instance property cannot have an initializer.");
        Initializer(property.Initializer, path + ".Initializer", depth);
    }

    void PropertyShape(CsProperty property, string path, int depth)
    {
        if (property.Expression != null)
        {
            validation.Expression(property.Expression, path + ".Expression", depth + 1);
            if (property.Accessors?.Count > 0 || property.Initializer != null)
                validation.Error("GEN044", path, "Expression property cannot have accessors or initializer.");
        }
        else if (property.Accessors?.Count == 0)
            validation.Error("GEN045", path, "Property needs an expression or accessors.");
    }

    static bool InvalidRequiredProperty(CsProperty property) =>
        property.Modifiers.Contains("static") || property.Expression != null ||
        property.Accessors != null && !property.Accessors.Any(IsWritable);

    static bool IsWritable(CsAccessor accessor) =>
        accessor != null && (accessor.Kind == "set" || accessor.Kind == "init");

    void Field(CsField field, string path, bool interfaceParent, int depth)
    {
        var modifiers = field.Modifiers;
        if (interfaceParent && !modifiers.Contains("static") && !modifiers.Contains("const"))
            validation.Error("GEN056", path, "Interface field must be static or const.");
        validation.Type(field.Type, path + ".Type", depth + 1);
        Initializer(field.Initializer, path + ".Initializer", depth);
        if (ModifierRules.InvalidField(field))
            validation.Error("GEN048", path, "Invalid constant/readonly/volatile field combination.");
    }

    void Method(CsMethod method, string path, bool interfaceParent, int depth)
    {
        var modifiers = method.Modifiers;
        validation.Type(method.ReturnType, path + ".ReturnType", depth + 1);
        validation.Parameters(method.Parameters, path + ".Parameters", depth + 1);
        validation.Generics(method.TypeParameters, path + ".TypeParameters", depth + 1, false);
        if ((modifiers.Contains("abstract") || modifiers.Contains("extern")) && method.Body != null)
            validation.Error("GEN049", path, "Abstract/extern method cannot have a body.");
        InterfaceMethod(method, path, interfaceParent);
        if (method.Body != null) validation.Body(method.Body, path + ".Body", depth + 1);
        else if (!interfaceParent && ModifierRules.NeedsMethodBody(modifiers))
            validation.Error("GEN050", path, "Concrete method needs a body.");
    }

    void InterfaceMethod(CsMethod method, string path, bool interfaceParent)
    {
        var modifiers = method.Modifiers;
        if (!interfaceParent || method.Body != null || modifiers.Contains("extern")) return;
        if (modifiers.Contains("private"))
            validation.Error("GEN060", path, "Interface private method must have a body.");
        if (modifiers.Contains("static") && !modifiers.Contains("abstract"))
            validation.Error("GEN061", path, "Interface concrete static method must have a body.");
    }

    void Constructor(CsConstructor constructor, string path, CsTypeDeclaration? parent, int depth, bool interfaceParent)
    {
        if (interfaceParent && !constructor.Modifiers.Contains("static"))
            validation.Error("GEN057", path, "Interface constructor must be static.");
        validation.Parameters(constructor.Parameters, path + ".Parameters", depth + 1);
        validation.Body(constructor.Body, path + ".Body", depth + 1);
        if (parent != null && constructor.Name != parent.Name)
            validation.Error("GEN051", path, "Constructor name must match its declaring type.");
        if (constructor.InitializerKind != null &&
            constructor.InitializerKind != "base" && constructor.InitializerKind != "this")
            validation.Error("GEN052", path, "Constructor initializer must be base or this.");
        if (ModifierRules.InvalidStaticConstructor(constructor))
            validation.Error("GEN053", path,
                "Static constructor cannot have accessibility, parameters, or initializer.");
        validation.List(constructor.InitializerArguments, path + ".InitializerArguments",
            (argument, child) => validation.Expression(argument, child, depth + 1));
    }

    void Event(CsEvent @event, string path, bool interfaceParent, int depth)
    {
        var modifiers = @event.Modifiers;
        if (interfaceParent && !modifiers.Contains("static") && @event.Initializer != null)
            validation.Error("GEN059", path, "Interface instance event cannot have an initializer.");
        validation.Type(@event.Type, path + ".Type", depth + 1);
        validation.Accessors(@event.Accessors, path + ".Accessors", depth + 1, true, modifiers.Contains("abstract"));
        if (@event.Initializer == null) return;
        validation.Expression(@event.Initializer, path + ".Initializer", depth + 1);
        if (@event.Accessors?.Count > 0 || modifiers.Contains("abstract"))
            validation.Error("GEN054", path, "Custom/abstract event cannot have an initializer.");
    }

    void Initializer(CsExpression? expression, string path, int depth)
    {
        if (expression != null) validation.Expression(expression, path, depth + 1);
    }
}
