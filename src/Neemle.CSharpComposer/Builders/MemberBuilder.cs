using Neemle.CSharpComposer.Rendering;
using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public abstract class MemberBuilder<TSelf>
    where TSelf : MemberBuilder<TSelf>
{
    protected readonly List<string> modifiers = new List<string>();
    protected readonly List<CsAttribute> attributes = new List<CsAttribute>();
    protected TSelf Self => this is TSelf self ? self : throw new InvalidOperationException(
        "Builder '" + GetType().Name + "' must use its own type as TSelf; '" + typeof(TSelf).Name + "' was supplied.");

    public TSelf Modifier(string modifier)
    {
        modifiers.Add(modifier);
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
