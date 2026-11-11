using Neemle.CSharpComposer.Model;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Builders;
public sealed class FieldBuilder<TParent> : MemberBuilder<FieldBuilder<TParent>>
{
    readonly TParent parent;
    readonly Action<CsField> add;
    readonly CsType type;
    readonly string name;
    CsExpression? initializer;
    internal FieldBuilder(TParent parent, Action<CsField> add, CsType type, string name)
    {
        this.parent = parent;
        this.add = add;
        this.type = type;
        this.name = name;
    }

    public FieldBuilder<TParent> Initialize(CsExpression value)
    {
        initializer = value;
        return this;
    }

    public CsField Build() => new CsField(type, name)
    {
        Modifiers = FrozenModifiers,
        Attributes = FrozenAttributes,
        Initializer = initializer
    };
    public TParent End()
    {
        add(Build());
        return parent;
    }
}
