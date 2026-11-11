using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class TypeBuilder<TParent> : MemberBuilder<TypeBuilder<TParent>>, IVersionContext
{
    readonly TParent parent;

    readonly Action<CsTypeDeclaration> add;

    readonly TypeKind kind;

    readonly string name;

    CsLanguageVersion IVersionContext.TargetVersion => targetVersion;

    TypeKind? IVersionContext.OwningType => kind;

    internal override void CheckFeatures() { _ = Build(); }

    readonly List<CsMember> members = new List<CsMember>();

    readonly List<CsType> bases = new List<CsType>();

    readonly List<TypeParameter> generics = new List<TypeParameter>();

    readonly List<CsParameter> primary = new List<CsParameter>();

    internal TypeBuilder(TParent parent, Action<CsTypeDeclaration> add, TypeKind kind, string name)
    {
        this.parent = parent;
        this.add = add;
        this.kind = kind;
        this.name = name;
        Context(parent!);
        if (targetVersion is not (CsLanguageVersion.CSharp14 or CsLanguageVersion.CSharp15)) CheckFeatures();
    }

    public TypeBuilder<TParent> Add(CsMember member)
    {
        members.Add(LanguageFeatures.Checked(member, targetVersion, kind));
        return this;
    }

    public TypeBuilder<TParent> AddRange(IEnumerable<CsMember> values)
    {
        foreach (var member in values) Add(member);
        return this;
    }

    public TypeBuilder<TParent> Base(CsType type)
    {
        bases.Add(type);
        CheckKnownFeatures();
        return this;
    }

    public TypeBuilder<TParent> Generic(string name, params CsConstraint[] constraints)
    {
        generics.Add(new TypeParameter(name) { Constraints = constraints });
        CheckKnownFeatures();
        return this;
    }

    public TypeBuilder<TParent> Generic(TypeParameter parameter)
    {
        generics.Add(parameter);
        CheckKnownFeatures();
        return this;
    }

    public TypeBuilder<TParent> Parameter(CsType type, string name)
    {
        primary.Add(new CsParameter(type, name));
        return this;
    }

    public PropertyBuilder<TypeBuilder<TParent>> Property<T>(string name) => Property(Cs.Type<T>(), name);

    public PropertyBuilder<TypeBuilder<TParent>> Property(CsType type, string name) =>
        new PropertyBuilder<TypeBuilder<TParent>>(this, m => members.Add(m), type, name);

    public FieldBuilder<TypeBuilder<TParent>> Field<T>(string name) => Field(Cs.Type<T>(), name);

    public FieldBuilder<TypeBuilder<TParent>> Field(CsType type, string name) =>
        new FieldBuilder<TypeBuilder<TParent>>(this, m => members.Add(m), type, name);

    public MethodBuilder<TypeBuilder<TParent>> Method(CsType returnType, string name) =>
        new MethodBuilder<TypeBuilder<TParent>>(this, m => members.Add(m), returnType, name);

    public MethodBuilder<TypeBuilder<TParent>> Method<T>(string name) => Method(Cs.Type<T>(), name);

    public ConstructorBuilder<TypeBuilder<TParent>> Constructor() =>
        new ConstructorBuilder<TypeBuilder<TParent>>(this, m => members.Add(m), name);

    public EventBuilder<TypeBuilder<TParent>> Event(CsType type, string name) =>
        new EventBuilder<TypeBuilder<TParent>>(this, m => members.Add(m), type, name);

    public TypeBuilder<TParent> EnumMember(string name, CsExpression? value = null) =>
        Add(new CsEnumMember(name, value));

    public TypeBuilder<TypeBuilder<TParent>> Type(TypeKind kind, string name) =>
        new TypeBuilder<TypeBuilder<TParent>>(this, m => members.Add(m), kind, name);

    public TypeBuilder<TypeBuilder<TParent>> Class(string name) => Type(TypeKind.Class, name);

    public TypeBuilder<TypeBuilder<TParent>> Struct(string name) => Type(TypeKind.Struct, name);

    public TypeBuilder<TypeBuilder<TParent>> Record(string name) => Type(TypeKind.Record, name);

    public TypeBuilder<TypeBuilder<TParent>> RecordClass(string name) => Type(TypeKind.RecordClass, name);

    public TypeBuilder<TypeBuilder<TParent>> RecordStruct(string name) => Type(TypeKind.RecordStruct, name);

    public TypeBuilder<TypeBuilder<TParent>> Interface(string name) => Type(TypeKind.Interface, name);

    public TypeBuilder<TypeBuilder<TParent>> Enum(string name) => Type(TypeKind.Enum, name);

    public CsTypeDeclaration Build() => Versioned(new CsTypeDeclaration(kind, name)
    {
        Modifiers = FrozenModifiers,
        Attributes = FrozenAttributes,
        Members = new Values<CsMember>(members),
        BaseTypes = new Values<CsType>(bases),
        TypeParameters = new Values<TypeParameter>(generics),
        PrimaryParameters = new Values<CsParameter>(primary)
    });

    public TParent End()
    {
        add(Build());
        return parent;
    }
}
