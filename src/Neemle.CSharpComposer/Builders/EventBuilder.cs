using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class EventBuilder<TParent> : MemberBuilder<EventBuilder<TParent>>
{
    readonly TParent parent;
    readonly Action<CsEvent> add;
    readonly CsType type;
    readonly string name;
    readonly List<CsAccessor> accessors = new List<CsAccessor>();
    CsExpression? initializer;
    internal override void CheckFeatures() { _ = Build(); }
    internal EventBuilder(TParent parent, Action<CsEvent> add, CsType type, string name)
    {
        this.parent = parent;
        this.add = add;
        this.type = type;
        this.name = name;
        Context(parent!);
    }

    public EventBuilder<TParent> Add(CsBody body)
    {
        accessors.Add(new CsAccessor("add") { Body = body });
        CheckKnownFeatures();
        return this;
    }

    public EventBuilder<TParent> Remove(CsBody body)
    {
        accessors.Add(new CsAccessor("remove") { Body = body });
        CheckKnownFeatures();
        return this;
    }

    public EventBuilder<TParent> Initialize(CsExpression value)
    {
        initializer = value;
        return this;
    }

    public CsEvent Build() => Versioned(new CsEvent(type, name)
    {
        Modifiers = FrozenModifiers,
        Attributes = FrozenAttributes,
        Accessors = new Values<CsAccessor>(accessors),
        Initializer = initializer
    });
    public TParent End()
    {
        add(Build());
        return parent;
    }
}
