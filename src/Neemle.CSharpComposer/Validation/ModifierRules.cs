using Neemle.CSharpComposer.Model;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

internal static class ModifierRules
{
    internal static string Allowed(CsMember member) => member switch
    {
        CsDelegate => "new public protected internal private unsafe file",
        CsDestructor => "extern unsafe",
        CsOperatorDeclaration => "public static unsafe safe extern abstract virtual sealed",
        CsExtension => "",
        CsTypeDeclaration => "new public protected internal private static abstract sealed readonly ref unsafe partial file closed",
        CsProperty => "new public protected internal private static abstract virtual sealed override required unsafe readonly partial",
        CsMethod => "new public protected internal private static abstract virtual sealed override unsafe " +
            "extern async partial readonly safe",
        CsField => "new public protected internal private static readonly const volatile unsafe fixed safe",
        CsConstructor => "public protected internal private static unsafe safe extern partial",
        CsEvent => "new public protected internal private static abstract virtual sealed override unsafe extern partial",
        _ => ""
    };

    static string[] Access(Values<string> modifiers) =>
        modifiers.Where(value => new[] { "public", "protected", "internal", "private" }.Contains(value)).ToArray();

    internal static bool ConflictingAccess(Values<string> modifiers)
    {
        var access = Access(modifiers);
        return access.Length > 1 && !(access.Length == 2 && access.Contains("protected") &&
            (access.Contains("internal") || access.Contains("private")));
    }

    internal static bool Conflicting(Values<string> modifiers, bool staticInterfaceMember)
    {
        return modifiers.Contains("abstract") &&
            (modifiers.Contains("sealed") || !staticInterfaceMember && modifiers.Contains("static") ||
                modifiers.Contains("virtual")) ||
            modifiers.Contains("static") &&
            (!staticInterfaceMember && modifiers.Contains("virtual") || modifiers.Contains("override")) ||
            modifiers.Contains("virtual") && modifiers.Contains("override");
    }

    internal static bool HasRestrictedTopLevelAccess(Values<string> modifiers) =>
        modifiers.Contains("private") || modifiers.Contains("protected") || modifiers.Contains("new");

    internal static bool InvalidField(CsField field)
    {
        var modifiers = field.Modifiers;
        return modifiers.Contains("const") && (field.Initializer == null || modifiers.Contains("static") ||
            modifiers.Contains("readonly") || modifiers.Contains("volatile")) ||
            modifiers.Contains("readonly") && modifiers.Contains("volatile");
    }

    internal static bool NeedsMethodBody(Values<string> modifiers) =>
        !modifiers.Contains("abstract") && !modifiers.Contains("extern") && !modifiers.Contains("partial");

    internal static bool InvalidStaticConstructor(CsConstructor constructor) =>
        constructor.Modifiers.Contains("static") && (Access(constructor.Modifiers).Length != 0 ||
            constructor.Parameters?.Count > 0 || constructor.InitializerKind != null);
}
