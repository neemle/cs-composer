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
        using var typeScope = validation.TypeScope(member);
        if (member is CsDisabledText) return;
        if (member is CsDirective directive) { validation.Directive(directive, path, depth); return; }
        if (member is not (CsRawMember or CsOperatorDeclaration or CsExtension)) validation.Identifier(member.Name, path + ".Name");
        if (member.ExplicitInterface != null)
        {
            validation.Type(member.ExplicitInterface, path + ".ExplicitInterface", depth + 1);
            if (member is not (CsMethod or CsProperty or CsEvent or CsOperatorDeclaration)) validation.Error("GEN065", path, "Explicit interface belongs to methods, properties, events or operators.");
        }
        validation.Attributes(member.Attributes, path + ".Attributes", depth + 1);
        if (!validation.Required(member.Modifiers, path + ".Modifiers")) return;
        if (member.Modifiers.Contains("file") && (parent != null || member.Modifiers.Any(m => m is "public" or "private" or "protected" or "internal"))) validation.Error("GEN034", path, "File-local type cannot be nested or have accessibility modifiers.");
        if (member is CsField f && f.Modifiers.Contains("fixed") && parent?.Kind is not (TypeKind.Struct or TypeKind.RecordStruct)) validation.Error("GEN092", path, "Fixed buffer requires a struct.");
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
            case CsDelegate d:
                validation.Type(d.ReturnType, path + ".ReturnType", depth + 1); validation.Parameters(d.Parameters, path + ".Parameters", depth + 1);
                validation.Generics(d.TypeParameters, path + ".TypeParameters", depth + 1, true); break;
            case CsDestructor d:
                if (parent?.Kind is not (TypeKind.Class or TypeKind.Record or TypeKind.RecordClass) || Neemle.CSharpComposer.Rendering.Names.Key(d.Name) != Neemle.CSharpComposer.Rendering.Names.Key(parent.Name)) validation.Error("GEN051", path, "Destructor requires its containing class name.");
                if (d.Body != null || !d.Modifiers.Contains("extern")) validation.Body(d.Body, path + ".Body", depth + 1); break;
            case CsOperatorDeclaration o: Operator(o, path, depth, interfaceParent); break;
            case CsExtension e:
                validation.Feature("Extension blocks", CsLanguageVersion.CSharp14, path);
                validation.Attributes(e.ReceiverAttributes, path + ".ReceiverAttributes", depth + 1);
                validation.Type(e.ReceiverType, path + ".ReceiverType", depth + 1);
                if (e.ReceiverName != null) validation.Identifier(e.ReceiverName, path + ".ReceiverName");
                if (e.ReceiverModifier != null && e.ReceiverModifier is not ("ref" or "in" or "ref readonly" or "scoped" or "scoped ref" or "scoped in" or "scoped ref readonly")) validation.Error("GEN016", path, "Invalid extension receiver modifier.");
                validation.Generics(e.TypeParameters, path + ".TypeParameters", depth + 1, false);
                validation.List(e.Members, path + ".Members", (m, child) =>
                {
                    if (m is CsProperty { IsIndexer: true }) { validation.Feature("Extension indexer", CsLanguageVersion.CSharp15, child); if (e.ReceiverName == null) validation.Error("GEN092", child, "Extension indexer requires a named receiver."); }
                    Validate(m, child, parent, depth + 1);
                }); break;
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

    void Operator(CsOperatorDeclaration o, string path, int depth, bool interfaceParent)
    {
        if (!validation.Required(o.Parameters, path + ".Parameters")) return;
        if (!Enum.IsDefined(o.Operator)) validation.Error("GEN092", path, "Unknown overloadable operator.");
        validation.Type(o.ReturnType, path + ".ReturnType", depth + 1); validation.Parameters(o.Parameters, path + ".Parameters", depth + 1);
        if (o.Body != null) validation.Body(o.Body, path + ".Body", depth + 1);
        else if (!o.Modifiers.Any(m => m is "abstract" or "extern")) validation.Required(null, path + ".Body");
        bool compound = o.Operator >= OverloadableOperator.AddAssign && o.Operator <= OverloadableOperator.UnsignedRightShiftAssign;
        bool conversion = o.Operator is OverloadableOperator.Implicit or OverloadableOperator.Explicit;
        bool instanceMutation = !o.Modifiers.Contains("static") && o.Operator is OverloadableOperator.Increment or OverloadableOperator.Decrement;
        if (compound || instanceMutation) validation.Feature("Instance mutation operators", CsLanguageVersion.CSharp14, path);
        if (!compound && !instanceMutation && !o.Modifiers.Contains("static")) validation.Error("GEN092", path, "This operator must be static.");
        if (!interfaceParent && o.ExplicitInterface == null && !o.Modifiers.Contains("public")) validation.Error("GEN092", path, "Operator must be public or explicitly implement an interface.");
        if (o.IsChecked && o.Operator is not (OverloadableOperator.Plus or OverloadableOperator.Minus or OverloadableOperator.Multiply or OverloadableOperator.Divide or OverloadableOperator.Increment or OverloadableOperator.Decrement or OverloadableOperator.Explicit or OverloadableOperator.AddAssign or OverloadableOperator.SubtractAssign or OverloadableOperator.MultiplyAssign or OverloadableOperator.DivideAssign)) validation.Error("GEN092", path, "This operator has no checked form.");
        bool unary = o.Operator is OverloadableOperator.Not or OverloadableOperator.Complement or OverloadableOperator.True or OverloadableOperator.False or OverloadableOperator.Increment or OverloadableOperator.Decrement;
        if (!compound && !instanceMutation && (unary && o.Parameters.Count != 1 || !unary && !conversion && o.Operator is not (OverloadableOperator.Plus or OverloadableOperator.Minus) && o.Parameters.Count != 2 || o.ReturnType?.Name == "void")) validation.Error("GEN092", path, "Operator has an invalid signature.");
        if (o.IsChecked) validation.Feature("Checked operators", CsLanguageVersion.CSharp11, path);
        if (compound && (o.Modifiers.Contains("static") || o.Parameters.Count != 1 || o.ReturnType?.Name != "void") || instanceMutation && (o.Parameters.Count != 0 || o.ReturnType?.Name != "void")) validation.Error("GEN092", path, "Instance mutation operator has an invalid signature.");
        if (!compound && !instanceMutation && (o.Parameters.Count < 1 || o.Parameters.Count > 2 || conversion && o.Parameters.Count != 1)) validation.Error("GEN092", path, "Operator has an invalid parameter count.");
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
        if (type.Kind == TypeKind.Union)
        {
            validation.Feature("Union declaration", CsLanguageVersion.CSharp15, path);
            if (type.UnionCases?.Count == 0) validation.Error("GEN092", path, "Union requires case types.");
        }
        else if (type.UnionCases?.Count > 0) validation.Error("GEN065", path, "Union cases belong to a union.");
        validation.List(type.UnionCases, path + ".UnionCases", (t, child) => validation.Type(t, child, depth + 1));
        if (type.Modifiers.Contains("closed"))
        {
            validation.Feature("Closed hierarchy", CsLanguageVersion.CSharp15, path);
            if (type.Kind is not (TypeKind.Class or TypeKind.Record or TypeKind.RecordClass) || type.Modifiers.Any(m => m is "abstract" or "sealed" or "static")) validation.Error("GEN034", path, "Invalid closed hierarchy modifiers.");
        }
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
        if ((type.HasPrimaryConstructor || type.PrimaryParameters?.Count > 0) && !record)
        {
            validation.Feature("Primary constructor", CsLanguageVersion.CSharp12, path);
            if (type.Kind is not (TypeKind.Class or TypeKind.Struct)) validation.Error("GEN042", path, "Only classes, structs and records have primary constructors.");
        }
        if (type.BaseArguments != null)
        {
            if (type.BaseTypes?.Count == 0 || !type.HasPrimaryConstructor && type.PrimaryParameters?.Count == 0) validation.Error("GEN042", path, "Base arguments require a primary constructor and base type.");
            validation.List(type.BaseArguments, path + ".BaseArguments", (argument, child) => validation.Expression(argument, child, 1, parent: ExpressionKind.Invocation));
        }
        if (type.Kind == TypeKind.Enum && type.TypeParameters?.Count > 0)
            validation.Error("GEN043", path, "Enums cannot be generic.");
    }

    void Property(CsProperty property, string path, bool interfaceParent, int depth)
    {
        var modifiers = property.Modifiers;
        if (property.IsIndexer)
        {
            if (property.Name != "this") validation.Error("GEN065", path, "Indexer name must be this.");
            if (property.Parameters?.Count == 0 || property.Initializer != null) validation.Error("GEN092", path, "Indexer requires parameters and cannot have an initializer.");
            validation.Parameters(property.Parameters, path + ".Parameters", depth + 1);
        }
        else if (property.Parameters?.Count > 0) validation.Error("GEN065", path, "Parameters belong to an indexer.");
        validation.Type(property.Type, path + ".Type", depth + 1);
        validation.Accessors(property.Accessors, path + ".Accessors", depth + 1, false, modifiers.Contains("abstract"));
        PropertyShape(property, path, depth);
        if (modifiers.Contains("abstract") && (property.Expression != null || property.Initializer != null))
            validation.Error("GEN046", path, "Abstract property cannot have implementation or initializer.");
        if (modifiers.Contains("required") && InvalidRequiredProperty(property))
            validation.Error("GEN047", path, "Required property must be a writable instance property.");
        if (interfaceParent && !modifiers.Contains("static") && property.Initializer != null)
            validation.Error("GEN058", path, "Interface instance property cannot have an initializer.");
        validation.VariableInitializer(property.Initializer, property.Type, path + ".Initializer", depth + 1);
    }

    void PropertyShape(CsProperty property, string path, int depth)
    {
        if (property.Expression != null)
        {
            validation.Expression(property.Expression, path + ".Expression", depth + 1, allowThrow: true);
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
        if (field.FixedSize != null || field.Modifiers.Contains("fixed"))
        {
            if (field.FixedSize == null || !field.Modifiers.Contains("fixed") || field.Initializer != null) validation.Error("GEN092", path, "Fixed buffer needs the fixed modifier, a size and no initializer.");
            if (field.FixedSize != null) validation.Constant(field.FixedSize, path + ".FixedSize", depth + 1);
        }
        if (field.Type?.Form == TypeForm.Ref) validation.Feature("Ref fields", CsLanguageVersion.CSharp11, path);
        validation.List(field.AdditionalVariables, path + ".AdditionalVariables", (v, child) => { validation.Identifier(v.Name, child + ".Name"); validation.VariableInitializer(v.Initializer, field.Type, child + ".Initializer", depth + 1); if (v.FixedSize != null) validation.Constant(v.FixedSize, child + ".FixedSize", depth + 1); if ((v.FixedSize != null) != field.Modifiers.Contains("fixed")) validation.Error("GEN092", child, "Fixed declarator needs a size."); });
        if (interfaceParent && !modifiers.Contains("static") && !modifiers.Contains("const"))
            validation.Error("GEN056", path, "Interface field must be static or const.");
        validation.Type(field.Type, path + ".Type", depth + 1);
        validation.VariableInitializer(field.Initializer, field.Type, path + ".Initializer", depth + 1);
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
        if (constructor.Modifiers.Contains("extern") && constructor.Body != null) validation.Error("GEN049", path, "Extern constructor cannot have a body.");
        if (constructor.Body != null || !constructor.Modifiers.Any(m => m is "partial" or "extern")) validation.Body(constructor.Body, path + ".Body", depth + 1);
        if (parent != null && Neemle.CSharpComposer.Rendering.Names.Key(constructor.Name) != Neemle.CSharpComposer.Rendering.Names.Key(parent.Name))
            validation.Error("GEN051", path, "Constructor name must match its declaring type.");
        if (constructor.InitializerKind != null &&
            constructor.InitializerKind != "base" && constructor.InitializerKind != "this")
            validation.Error("GEN052", path, "Constructor initializer must be base or this.");
        if (ModifierRules.InvalidStaticConstructor(constructor))
            validation.Error("GEN053", path,
                "Static constructor cannot have accessibility, parameters, or initializer.");
        validation.List(constructor.InitializerArguments, path + ".InitializerArguments",
            (argument, child) => validation.Expression(argument, child, depth + 1, parent: ExpressionKind.Invocation));
    }

    void Event(CsEvent @event, string path, bool interfaceParent, int depth)
    {
        var modifiers = @event.Modifiers;
        if (@event.Accessors?.Count > 0 && @event.AdditionalVariables?.Count > 0) validation.Error("GEN065", path, "Custom event cannot contain additional declarators.");
        validation.List(@event.AdditionalVariables, path + ".AdditionalVariables", (v, child) => { validation.Identifier(v.Name, child + ".Name"); Initializer(v.Initializer, child + ".Initializer", depth); });
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
