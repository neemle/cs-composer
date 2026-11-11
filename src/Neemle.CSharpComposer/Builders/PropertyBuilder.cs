using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class PropertyBuilder<TParent> : MemberBuilder<PropertyBuilder<TParent>>
{
    readonly TParent parent;
    readonly Action<CsProperty> add;
    readonly CsType type;
    readonly string name;
    readonly List<CsAccessor> accessors = new List<CsAccessor>();
    CsExpression? expression, initializer;
    internal PropertyBuilder(TParent parent, Action<CsProperty> add, CsType type, string name)
    {
        this.parent = parent;
        this.add = add;
        this.type = type;
        this.name = name;
    }

    public PropertyBuilder<TParent> Accessor(CsAccessor accessor)
    {
        accessors.Add(accessor);
        return this;
    }

    public PropertyBuilder<TParent> Get(string? accessibility = null, CsBody? body = null) =>
        Accessor(new CsAccessor("get") { Accessibility = accessibility, Body = body });
    public PropertyBuilder<TParent> Set(string? accessibility = null, CsBody? body = null) =>
        Accessor(new CsAccessor("set") { Accessibility = accessibility, Body = body });
    public PropertyBuilder<TParent> Init(string? accessibility = null, CsBody? body = null) =>
        Accessor(new CsAccessor("init") { Accessibility = accessibility, Body = body });
    public PropertyBuilder<TParent> Expression(CsExpression value)
    {
        expression = value;
        return this;
    }

    public PropertyBuilder<TParent> Initialize(CsExpression value)
    {
        initializer = value;
        return this;
    }

    public CsProperty Build() => new CsProperty(type, name)
    {
        Modifiers = FrozenModifiers,
        Attributes = FrozenAttributes,
        Accessors = new Values<CsAccessor>(accessors),
        Expression = expression,
        Initializer = initializer
    };
    public TParent End()
    {
        add(Build());
        return parent;
    }
}
