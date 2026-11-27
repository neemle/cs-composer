using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Parsing;

namespace Neemle.CSharpComposer.Tests.Scenarios;

// Small public-API builders the scenario tables share.
public static class Build
{
    public static CsDocument InClass(params CsMember[] members) => InType(TypeKind.Class, members);

    public static CsDocument InType(TypeKind kind, params CsMember[] members) =>
        new() { Types = new[] { new CsTypeDeclaration(kind, "C") { Members = members } } };

    public static CsDocument Field(CsType type) => InClass(new CsField(type, "F"));

    public static CsDocument Init(CsExpression value) => InClass(new CsField(Cs.Type("object"), "F") { Initializer = value });

    public static CsDocument Method(CsMethod method) => InClass(method);

    public static CsDocument InMethod(params CsStatement[] statements) => InClass(new CsMethod(Cs.Void, "M") { Body = Cs.Body(statements) });

    public static CsDocument Value(CsExpression value) => InMethod(Cs.Local(Cs.Type("object"), "x", value));

    public static CsDocument Src(string source, CsLanguageVersion version = CsLanguageVersion.CSharp15)
    {
        var parsed = Cs.Parse(source, new ParseOptions { LanguageVersion = version });
        return parsed.Diagnostics.Count == 0 ? parsed.Root : throw new InvalidOperationException("Scenario source does not parse: " + parsed.Diagnostics[0].Message);
    }

    public static CsDocument At(this CsDocument document, CsLanguageVersion version) => document with { LanguageVersion = version };

    public static CsExpression E(ExpressionKind kind, params CsExpression[] arguments) => new(kind, "", arguments);

    public static CsDocument Typed(CsType type) => InClass(new CsField(type, "F"));

    // A member kind the library does not define; the public model can still carry one.
    public sealed record ForeignMember() : CsMember("Foreign");
}
