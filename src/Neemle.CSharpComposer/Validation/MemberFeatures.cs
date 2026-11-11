using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

internal static class MemberFeatures
{
    internal static void Validate(Validator validation, CsMember member, string path, CsTypeDeclaration? parent)
    {
        bool structParent = parent?.Kind is TypeKind.Struct or TypeKind.RecordStruct;
        if (member.Modifiers.Contains("required"))
            validation.Feature("Required member", CsLanguageVersion.CSharp11, path + ".Modifiers");
        if (parent?.Kind == TypeKind.Interface && member is not CsTypeDeclaration)
            Interface(validation, member, path);
        switch (member)
        {
            case CsTypeDeclaration type: Type(validation, type, path); break;
            case CsMethod method: Method(validation, method, path); break;
            case CsConstructor constructor: Constructor(validation, constructor, structParent, path); break;
            case CsField field when field.Initializer != null:
                Initializer(validation, member, structParent, path); break;
            case CsProperty property when property.Initializer != null:
                Initializer(validation, member, structParent, path); break;
        }
    }

    static void Type(Validator validation, CsTypeDeclaration type, string path)
    {
        if (type.Kind == TypeKind.Record || type.Kind == TypeKind.RecordClass)
            validation.Feature("Record class", CsLanguageVersion.CSharp9, path + ".Kind");
        if (type.Kind == TypeKind.RecordStruct)
            validation.Feature("Record struct", CsLanguageVersion.CSharp10, path + ".Kind");
        if (type.Modifiers.Contains("ref") && type.BaseTypes?.Count > 0)
            validation.Feature("Ref struct interfaces", CsLanguageVersion.CSharp13, path + ".BaseTypes");
    }

    static void Method(Validator validation, CsMethod method, string path)
    {
        if (method.Modifiers.Contains("readonly"))
            validation.Feature("Readonly member", CsLanguageVersion.CSharp8, path + ".Modifiers");
        if (method.Modifiers.Contains("partial") && EnhancedPartial(method))
            validation.Feature("Enhanced partial method", CsLanguageVersion.CSharp9, path);
    }

    static bool EnhancedPartial(CsMethod method) => method.Modifiers.Any(modifier =>
        new[] { "public", "private", "internal", "protected" }.Contains(modifier)) ||
        method.ReturnType is { Form: not TypeForm.Raw, Name: not "void" } ||
        method.Parameters?.Any(parameter => parameter?.Modifier == "out") == true;

    static void Interface(Validator validation, CsMember member, string path)
    {
        bool implemented = member is CsMethod method && method.Body != null ||
            member is CsProperty property && (property.Expression != null ||
                property.Accessors?.Any(accessor => accessor?.Body != null) == true) ||
            member is CsEvent @event && @event.Accessors?.Count > 0;
        if (implemented || member.Modifiers.Contains("private") || member.Modifiers.Contains("static"))
            validation.Feature("Implemented/private/static interface member", CsLanguageVersion.CSharp8, path);
        if (member.Modifiers.Contains("static") &&
            (member.Modifiers.Contains("abstract") || member.Modifiers.Contains("virtual")))
            validation.Feature("Static abstract/virtual interface member", CsLanguageVersion.CSharp11, path);
    }

    static void Constructor(Validator validation, CsConstructor constructor, bool structParent, string path)
    {
        if (structParent && !constructor.Modifiers.Contains("static") && constructor.Parameters?.Count == 0)
            validation.Feature("Parameterless struct constructor", CsLanguageVersion.CSharp10, path);
    }

    static void Initializer(Validator validation, CsMember member, bool structParent, string path)
    {
        if (structParent && !member.Modifiers.Contains("static"))
            validation.Feature("Struct instance initializer", CsLanguageVersion.CSharp10, path + ".Initializer");
    }

}
