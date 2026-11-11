using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Neemle.CSharpComposer.Validation;

internal static class LanguageFeatures
{
    internal static readonly string UnsupportedMessage = "Unsupported C# language version. Supported: " +
        string.Join(", ", Enum.GetValues(typeof(CsLanguageVersion)).Cast<CsLanguageVersion>().Select(Display)) + ".";
    internal static string Display(CsLanguageVersion version) => version == CsLanguageVersion.CSharp7_3
        ? "7.3" : ((int)version / 100).ToString(CultureInfo.InvariantCulture);

    internal static void EnsureSupported(CsLanguageVersion version)
    {
        if (!Enum.IsDefined(typeof(CsLanguageVersion), version))
            throw new GenValidationException(new[] { new GenDiagnostic("GEN072", "LanguageVersion",
                UnsupportedMessage) });
    }

    internal static void Require(CsLanguageVersion version, CsLanguageVersion minimum, string name, string path)
    {
        if (version < minimum) throw new GenValidationException(new[]
        {
            new GenDiagnostic("GEN073", path, name + " requires C# " + Display(minimum) + ".")
        });
    }

    internal static T Checked<T>(T member, CsLanguageVersion version, TypeKind? parent = null) where T : CsMember
    {
        if (version == CsLanguageVersion.CSharp14) return member;
        var type = member as CsTypeDeclaration ?? new CsTypeDeclaration(parent ?? TypeKind.Class, "Context")
        {
            Members = new CsMember[] { member }
        };
        Checked(new CsDocument(version) { Types = new[] { type } });
        return member;
    }

    internal static CsDocument Checked(CsDocument document)
    {
        if (document.LanguageVersion == CsLanguageVersion.CSharp14) return document;
        var errors = document.Validate().Where(IsFeatureError).ToArray();
        if (errors.Length != 0) throw new GenValidationException(errors);
        return document;
    }

    static bool IsFeatureError(GenDiagnostic error) => error.Code == "GEN072" || error.Code == "GEN073";

    internal static IReadOnlyList<GenDiagnostic> RenderErrors(CsDocument document, RenderOptions options)
    {
        var errors = document.Validate().Where(error => options.Validate || IsFeatureError(error) ||
            error.Code == "GEN001" || error.Code == "GEN002" || error.Code == "GEN074" || error.Code == "GEN075").ToList();
        if (options.NamespaceStyle == NamespaceStyle.FileScoped && document.Namespace != null)
            Option(document, CsLanguageVersion.CSharp10,
                "File-scoped namespace", "RenderOptions.NamespaceStyle", errors);
        if (options.Nullable is { } nullable && nullable != NullableMode.Omit)
            Option(document, CsLanguageVersion.CSharp8, "Nullable context", "RenderOptions.Nullable", errors);
        return errors;
    }

    static void Option(CsDocument document, CsLanguageVersion minimum, string name, string path,
        List<GenDiagnostic> errors)
    {
        if (document.LanguageVersion < minimum)
            errors.Add(new GenDiagnostic("GEN073", path, name + " requires C# " + Display(minimum) + "."));
    }

    internal static bool IsAnnotation(CsType? element) => element != null &&
        element.Form != TypeForm.Raw && element.Form != TypeForm.Tuple &&
        !(element.Form == TypeForm.Named && Cs.PrimitiveNames.Values.Contains(element.Name) &&
            element.Name != "string" && element.Name != "object");

    internal static void Parameter(Validator validation, CsParameter parameter, string path)
    {
        if (parameter.Modifier == "ref readonly")
            validation.Feature("Ref readonly parameter", CsLanguageVersion.CSharp12, path + ".Modifier");
        if (parameter.Modifier == "scoped ref" || parameter.Modifier == "scoped in")
            validation.Feature("Scoped parameter", CsLanguageVersion.CSharp11, path + ".Modifier");
        if (parameter.Modifier == "params" && parameter.Type != null &&
            parameter.Type.Form != TypeForm.Array && parameter.Type.Form != TypeForm.Raw)
            validation.Feature("Params collection", CsLanguageVersion.CSharp13, path + ".Modifier");
    }
}
