using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Neemle.CSharpComposer.Model;

namespace Neemle.CSharpComposer.Tests;

// GENE-29 AD 84939297 (v1 I4/F2, v2 D1/F): the corpus gate's own structural comparer (the §0.17 derivation bar). It never calls
// CsNode.Equals, the renderer or any guard code: it walks the public settable properties of both trees by reflection,
// compares leaves (strings, numbers, enums) by value and lists element by element.
// Two wrappers in the re-parsed tree are transparent and nothing else is:
// - an ExpressionKind.Parenthesized expression over an intended expression of any other kind;
// - an `else` written as a block holding exactly one statement over an intended brace-less `else` statement.
// A braced and a brace-less embedded body (`if (x) { y; }` and `if (x) y;`) are the same CsBody in the public model,
// so that half of the AD's single-statement brace rule needs no unwrapping here.
// Comment text is compared after the gate's dedent (CorpusComments.Normalize, a superset of AD v2 B4), and a comment's
// parsed Documentation is not compared at all: it is derived from that text.
// The walk uses an explicit stack, so a long expression chain costs no stack depth.
internal static class GuardEquivalence
{
    const int StepLimit = 50_000_000;

    static readonly ConcurrentDictionary<Type, Getter[]> Getters = new();

    internal sealed record Getter(string Name, Func<object, object?> Read);

    internal sealed record Divergence(string Path, string Pattern, string Intended, string Actual);

    sealed record Frame(object? Intended, object? Actual, Frame? Parent, object? Owner, string Label);

    // Null when `actual` (the re-parse) equals `intended` under guard equivalence, else the first divergence in
    // depth-first order.
    internal static Divergence? Compare(object? intended, object? actual)
    {
        var stack = new Stack<Frame>();
        stack.Push(new(intended, actual, null, null, "Root"));
        int steps = 0;
        while (stack.Count != 0)
        {
            if (++steps > StepLimit) throw new InvalidOperationException("Guard comparison exceeded " + StepLimit + " steps.");
            var frame = stack.Pop();
            object? left = frame.Intended, right = Transparent(frame.Intended, frame.Actual, frame.Label);
            if (ReferenceEquals(left, right)) continue;
            if (frame.Owner is CsComment && frame.Label == nameof(CsComment.Documentation)) continue;
            if (frame.Owner is CsComment && frame.Label == nameof(CsComment.Text) && left is string a && right is string b)
            {
                if (CorpusComments.Normalize(a) != CorpusComments.Normalize(b)) return Diverge(frame, "value", Show(a), Show(b));
                continue;
            }
            if (left is null || right is null) return Diverge(frame, "null", Show(left), Show(right));
            var type = left.GetType();
            if (type != right.GetType()) return Diverge(frame, "type", Kind(left), Kind(right));
            if (IsLeaf(type))
            {
                if (!left.Equals(right)) return Diverge(frame, "value", Show(left), Show(right));
                continue;
            }
            if (left is IEnumerable leftItems)
            {
                var l = leftItems.Cast<object?>().ToList();
                var r = ((IEnumerable)right).Cast<object?>().ToList();
                if (l.Count != r.Count) return Diverge(frame, "count", l.Count.ToString(), r.Count.ToString());
                for (int i = l.Count - 1; i >= 0; i--) stack.Push(new(l[i], r[i], frame, frame.Owner, "[" + i + "]"));
                continue;
            }
            var getters = For(type);
            for (int i = getters.Length - 1; i >= 0; i--)
                stack.Push(new(getters[i].Read(left), getters[i].Read(right), frame, left, getters[i].Name));
        }
        return null;
    }

    static object? Transparent(object? intended, object? actual, string label)
    {
        while (actual is CsExpression { Kind: ExpressionKind.Parenthesized, Arguments.Count: 1 } parenthesized &&
            intended is CsExpression { Kind: not ExpressionKind.Parenthesized })
            actual = parenthesized.Arguments[0];
        if (label == nameof(CsStatement.Else) && intended is CsStatement { Kind: not StatementKind.Block } &&
            actual is CsStatement { Kind: StatementKind.Block, Body: { Statements.Count: 1 } block })
            actual = block.Statements[0];
        return actual;
    }

    static bool IsLeaf(Type type) => type.IsValueType || type == typeof(string);

    internal static Getter[] For(Type type) => Getters.GetOrAdd(type, static type => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.CanRead && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.MetadataToken)
        .Select(p => new Getter(p.Name, Compile(type, p))).ToArray());

    static Func<object, object?> Compile(Type type, PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object));
        var read = Expression.Property(Expression.Convert(instance, type), property);
        return Expression.Lambda<Func<object, object?>>(Expression.Convert(read, typeof(object)), instance).Compile();
    }

    // A node's type plus its Kind, when it has one (CsExpression.Invocation, CsStatement.If, CsComment.Line).
    internal static string Kind(object node)
    {
        var type = node.GetType();
        var kind = For(type).FirstOrDefault(g => g.Name == "Kind");
        string name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;
        return kind?.Read(node) is Enum value ? name + "." + value : name;
    }

    static Divergence Diverge(Frame frame, string what, string intended, string actual)
    {
        var labels = new List<string>();
        for (var f = frame; f != null && labels.Count < 16; f = f.Parent) labels.Add(f.Label);
        labels.Reverse();
        string owner = frame.Owner == null ? "Root" : Kind(frame.Owner);
        string property = frame.Label.StartsWith('[') ? frame.Parent?.Label + "[]" : frame.Label;
        return new(string.Join(".", labels).Replace(".[", "["), owner + "." + property + " " + what, intended, actual);
    }

    static string Show(object? value) => value switch
    {
        null => "null",
        string text => Clip(text),
        _ when IsLeaf(value.GetType()) => value.ToString() ?? "",
        _ => Kind(value)
    };

    internal static string Clip(string text) => text.Length <= 80 ? text : text[..77] + "...";
}
