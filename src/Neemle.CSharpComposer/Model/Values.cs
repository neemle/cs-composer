using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed class Values<T> : IReadOnlyList<T>, IEquatable<Values<T>>
{
    readonly T[] items;
    public static Values<T> Empty { get; } = new Values<T>();

    public Values(params T[] items)
    {
        this.items = (T[])(items ?? throw new ArgumentNullException(nameof(items))).Clone();
    }

    public Values(IEnumerable<T> items)
    {
        this.items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
    }

    public int Count => items.Length;

    public T this[int index] => items[index];
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(Values<T>? other) => other != null && items.SequenceEqual(other.items);
    public override bool Equals(object? obj) => obj is Values<T> other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            foreach (var item in items)
                h = h * 31 + (item == null ? 0 : item.GetHashCode());
            return h;
        }
    }

    public static implicit operator Values<T>(T[] items) => new Values<T>(items);
}
