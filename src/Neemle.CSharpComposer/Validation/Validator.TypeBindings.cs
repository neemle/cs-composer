using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Rendering;

namespace Neemle.CSharpComposer.Validation;

internal sealed partial class Validator
{
    enum TypeBinding { Unknown, Value, Reference, Unconstrained }
    readonly Dictionary<string, TypeBinding> declaredTypes = new(StringComparer.Ordinal);
    readonly Stack<Dictionary<string, TypeBinding>> typeScopes = new();
    string currentNamespace = "";

    internal IDisposable TypeScope(CsMember member)
    {
        var parameters = member switch { CsTypeDeclaration t => t.TypeParameters, CsMethod m => m.TypeParameters, CsDelegate d => d.TypeParameters, CsExtension e => e.TypeParameters, _ => null };
        var scope = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
        if (member is CsTypeDeclaration owner)
        {
            scope[Names.Key(owner.Name)] = Kind(owner);
            if (owner.Members != null) foreach (var nested in owner.Members.OfType<CsTypeDeclaration>()) scope[Names.Key(nested.Name)] = Kind(nested);
        }
        if (parameters != null) foreach (var parameter in parameters)
            {
                if (parameter?.Name == null || parameter.Constraints == null) continue;
                var constraints = parameter.Constraints.Where(c => c != null).ToArray();
                scope[Names.Key(parameter.Name)] = constraints.Any(c => c.Kind is "struct" or "unmanaged") ? TypeBinding.Value :
                    constraints.Any(c => c.Kind is "class" or "class?") ? TypeBinding.Reference :
                    constraints.Any(c => c.Kind == "type") ? TypeBinding.Unknown : TypeBinding.Unconstrained;
            }
        typeScopes.Push(scope); return new ScopeEnd(() => typeScopes.Pop());
    }

    sealed class ScopeEnd(Action end) : IDisposable { public void Dispose() => end(); }

    static TypeBinding Kind(CsTypeDeclaration type) => type.Kind is TypeKind.Struct or TypeKind.RecordStruct or TypeKind.Enum or TypeKind.Union ? TypeBinding.Value : TypeBinding.Reference;

    IDisposable DeclarationScope(IEnumerable<CsNode> items)
    {
        var scope = new Dictionary<string, TypeBinding>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is CsTypeDeclaration t && t.Name != null) scope[Names.Key(t.Name)] = Kind(t);
            if (item is CsDelegate d && d.Name != null) scope[Names.Key(d.Name)] = TypeBinding.Reference;
        }
        typeScopes.Push(scope); return new ScopeEnd(() => typeScopes.Pop());
    }

    void CollectTypes(CsNode node, string prefix = "")
    {
        if (node is CsDocument { Namespace: not null } document) prefix = document.Namespace;
        if (node is CsNamespace space) prefix = prefix == "" ? space.Name : prefix + "." + space.Name;
        if (node is CsTypeDeclaration type)
        {
            prefix = prefix == "" ? Names.Key(type.Name) : prefix + "." + Names.Key(type.Name);
            declaredTypes[prefix] = Kind(type);
        }
        if (node is CsDelegate d) declaredTypes[(prefix == "" ? "" : prefix + ".") + Names.Key(d.Name)] = TypeBinding.Reference;
        foreach (var child in node.Children) CollectTypes(child, prefix);
    }

    IDisposable NamespaceScope(string name)
    {
        string before = currentNamespace;
        currentNamespace = before == "" ? name : before + "." + name;
        return new ScopeEnd(() => currentNamespace = before);
    }

    static string TypeName(CsType? type, int depth = 0) => type == null || depth > 128 ? "" : type.Form == TypeForm.Generic ? TypeName(type.Element, depth + 1) :
        type.Form == TypeForm.Nested ? TypeName(type.Element, depth + 1) + "." + Names.Key(type.Name) : Names.Key(type.Name);

    TypeBinding Binding(CsType? type, int depth = 0)
    {
        if (type == null || depth > 128) return TypeBinding.Unknown;
        if (type.Form == TypeForm.Array) return TypeBinding.Reference;
        if (type.Form is TypeForm.Tuple or TypeForm.Pointer or TypeForm.FunctionPointer or TypeForm.Nullable) return TypeBinding.Value;
        if (type.Form == TypeForm.Generic)
        {
            if (type.Element == null || type.Arguments == null) return TypeBinding.Unknown;
            string genericBase = TypeName(type.Element).Replace("global::", "");
            string genericName = genericBase + "`" + type.Arguments.Count;
            var generic = genericBase.StartsWith("System.", StringComparison.Ordinal) && Names.IsQualified(genericBase) ? System.Type.GetType(genericName, throwOnError: false) : null;
            if (generic != null) return generic.IsValueType ? TypeBinding.Value : TypeBinding.Reference;
            return Binding(type.Element, depth + 1);
        }
        string name = TypeName(type);
        foreach (var scope in typeScopes) if (scope.TryGetValue(name, out var parameter)) return parameter;
        if (name is "string" or "object" or "dynamic") return TypeBinding.Reference;
        if (Cs.PrimitiveNames.Values.Contains(name) || name is "nint" or "nuint") return TypeBinding.Value;
        string qualified = name.Replace("global::", "");
        for (string prefix = currentNamespace; ;)
        {
            if (declaredTypes.TryGetValue(prefix == "" ? qualified : prefix + "." + qualified, out var declared)) return declared;
            if (prefix == "") break;
            int dot = prefix.LastIndexOf('.'); prefix = dot < 0 ? "" : prefix[..dot];
        }
        string runtimeName = name.StartsWith("global::", StringComparison.Ordinal) ? name[8..] : name;
        var runtime = runtimeName.StartsWith("System.", StringComparison.Ordinal) && Names.IsQualified(runtimeName) ? System.Type.GetType(runtimeName, throwOnError: false) : null;
        return runtime == null ? TypeBinding.Unknown : runtime.IsValueType ? TypeBinding.Value : TypeBinding.Reference;
    }

    void NullableFeature(CsType type, string path)
    {
        var binding = Binding(type.Element);
        if (binding == TypeBinding.Reference) Feature("Nullable annotation", CsLanguageVersion.CSharp8, path);
        if (binding == TypeBinding.Unconstrained) Feature("Unconstrained nullable type parameter", CsLanguageVersion.CSharp9, path);
        // An unresolved name may denote a value type. The compiler binds names;
        // a syntax DOM must not reject a legal T? by guessing it is a reference.
    }
}
