using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class FileBuilder : IVersionContext
{
    readonly CsLanguageVersion version;

    CsLanguageVersion IVersionContext.TargetVersion => version;

    TypeKind? IVersionContext.OwningType => null;

    string? ns;

    string? header;

    NullableMode nullable;

    readonly List<CsUsing> usings = new List<CsUsing>();

    readonly List<CsAttribute> attributes = new List<CsAttribute>();

    readonly List<CsTypeDeclaration> types = new List<CsTypeDeclaration>();

    public FileBuilder(CsLanguageVersion version = CsLanguageVersion.CSharp15)
    {
        LanguageFeatures.EnsureSupported(version);
        this.version = version;
    }

    public FileBuilder Namespace(string value)
    {
        ns = value;
        return this;
    }

    public FileBuilder Header(string value)
    {
        header = value;
        return this;
    }

    public FileBuilder Nullable(NullableMode mode)
    {
        if (mode != NullableMode.Omit)
            LanguageFeatures.Require(version, CsLanguageVersion.CSharp8, "Nullable context", "NullableContext");
        nullable = mode;
        return this;
    }

    public FileBuilder Using(string name, string? alias = null, bool isStatic = false, bool isGlobal = false)
    {
        if (isGlobal) LanguageFeatures.Require(version, CsLanguageVersion.CSharp10, "Global using", "Usings");
        usings.Add(new CsUsing(name, alias, isStatic, isGlobal));
        return this;
    }

    public FileBuilder Attribute(CsAttribute attribute)
    {
        attributes.Add(attribute);
        return this;
    }

    public FileBuilder Add(CsTypeDeclaration type)
    {
        types.Add(LanguageFeatures.Checked(type, version));
        return this;
    }

    public FileBuilder AddRange(IEnumerable<CsTypeDeclaration> declarations)
    {
        foreach (var type in declarations) Add(type);
        return this;
    }

    public FileBuilder When(bool condition, Action<FileBuilder> configure)
    {
        if (condition)
            configure(this);
        return this;
    }

    public TypeBuilder<FileBuilder> Type(TypeKind kind, string name) =>
        new TypeBuilder<FileBuilder>(this, t => Add(t), kind, name);

    public TypeBuilder<FileBuilder> Class(string name) => Type(TypeKind.Class, name);

    public TypeBuilder<FileBuilder> Struct(string name) => Type(TypeKind.Struct, name);

    public TypeBuilder<FileBuilder> Record(string name) => Type(TypeKind.Record, name);

    public TypeBuilder<FileBuilder> RecordClass(string name) => Type(TypeKind.RecordClass, name);

    public TypeBuilder<FileBuilder> RecordStruct(string name) => Type(TypeKind.RecordStruct, name);

    public TypeBuilder<FileBuilder> Interface(string name) => Type(TypeKind.Interface, name);

    public TypeBuilder<FileBuilder> Enum(string name) => Type(TypeKind.Enum, name);

    public CsDocument Build() => LanguageFeatures.Checked(new CsDocument()
    {
        LanguageVersion = version,
        Namespace = ns,
        Header = header,
        NullableContext = nullable,
        Usings = new Values<CsUsing>(usings),
        Attributes = new Values<CsAttribute>(attributes),
        Types = new Values<CsTypeDeclaration>(types)
    });

    public string Render(RenderOptions? options = null) => Build().Render(options);

    public static implicit operator CsDocument(FileBuilder builder) => builder.Build();
}
