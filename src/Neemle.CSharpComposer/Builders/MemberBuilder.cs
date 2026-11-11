using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public abstract class MemberBuilder<TSelf>
    where TSelf : MemberBuilder<TSelf>
{
    protected readonly List<string> modifiers = new List<string>();
    protected readonly List<CsAttribute> attributes = new List<CsAttribute>();
    internal CsLanguageVersion targetVersion = CsLanguageVersion.CSharp14;
    internal TypeKind? parentKind;
    internal void Context(object parent)
    {
        if (parent is not IVersionContext context)
            throw new InvalidOperationException("A library builder requires its owning document/type version context.");
        targetVersion = context.TargetVersion;
        parentKind = context.OwningType;
    }

    internal virtual void CheckFeatures() { }
    internal void CheckKnownFeatures()
    {
        if (targetVersion != CsLanguageVersion.CSharp14) CheckFeatures();
    }
    internal T Versioned<T>(T member) where T : CsMember =>
        LanguageFeatures.Checked(member, targetVersion, parentKind);
    protected TSelf Self => this is TSelf self ? self : throw new InvalidOperationException(
        "Builder '" + GetType().Name + "' must use its own type as TSelf; '" + typeof(TSelf).Name + "' was supplied.");

    public TSelf Modifier(string modifier)
    {
        modifiers.Add(modifier);
        try { CheckKnownFeatures(); }
        catch { modifiers.RemoveAt(modifiers.Count - 1); throw; }
        return Self;
    }

    public TSelf Public() => Modifier("public");
    public TSelf Private() => Modifier("private");
    public TSelf Protected() => Modifier("protected");
    public TSelf Internal() => Modifier("internal");
    public TSelf Static() => Modifier("static");
    public TSelf Partial() => Modifier("partial");
    public TSelf Abstract() => Modifier("abstract");
    public TSelf Sealed() => Modifier("sealed");
    public TSelf ReadOnly() => Modifier("readonly");
    public TSelf Ref() => Modifier("ref");
    public TSelf Unsafe() => Modifier("unsafe");
    public TSelf Virtual() => Modifier("virtual");
    public TSelf Override() => Modifier("override");
    public TSelf Async() => Modifier("async");
    public TSelf Required() => Modifier("required");
    public TSelf Const() => Modifier("const");
    public TSelf New() => Modifier("new");
    public TSelf Volatile() => Modifier("volatile");
    public TSelf Extern() => Modifier("extern");
    public TSelf Attribute(CsAttribute attribute)
    {
        attributes.Add(attribute);
        return Self;
    }

    public TSelf When(bool condition, Action<TSelf> configure)
    {
        if (condition)
            configure(Self);
        return Self;
    }

    protected Values<string> FrozenModifiers =>
        new Values<string>(Names.OrderModifiers(modifiers).Distinct(StringComparer.Ordinal));
    protected Values<CsAttribute> FrozenAttributes => new Values<CsAttribute>(attributes);
}
