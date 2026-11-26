using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
namespace Neemle.CSharpComposer.Parsing;

internal sealed partial class DocumentParser
{
    readonly SourceSnapshot source;

    readonly Guid syntaxIdentity = Guid.NewGuid();

    readonly List<Token> tokens;

    readonly List<Token> all;

    readonly List<ParseDiagnostic> diagnostics = new();

    readonly CsLanguageVersion version;

    readonly Queue<CsNode> pending = new();

    readonly List<CsNode> syntaxTrivia = new();

    readonly HashSet<(int, int)> triviaComments = new();

    int p;

    bool inAccessor;

    bool inAsync = true;

    readonly CancellationToken cancellation;

    // Start offsets of the units the recovery sites attempted, and the units an earlier attempt's
    // validation rejected, which this attempt keeps as raw source from the start (GENE-19).
    readonly HashSet<int> siteStarts = new();

    Dictionary<int, string> rejected = new();

    Token Current => tokens[Math.Min(p, tokens.Count - 1)];

    Token Last => tokens[Math.Max(0, p - 1)];

    DocumentParser(SourceSnapshot source, CsLanguageVersion version, IEnumerable<string>? symbols, CancellationToken cancellation)
    {
        this.source = source;
        this.version = version;
        this.cancellation = cancellation;
        all = Lexer.Scan(source.Text, evaluateDirectives: true, symbols, cancellation);
        tokens = all.Where(t => !t.IsComment).ToList();
        BuildIndex();
        ExtractSyntaxTrivia();
        foreach (var token in all.Where(t => t.IsMalformed))
            Error("PARSE001", "Unterminated literal or comment.", token.Start, token.End - token.Start);
    }

    // A typed result is never one that emit rejects (GENE-19 release invariant). When validation rejects
    // the parsed tree, the innermost statement, member or top-level item holding each error is parsed
    // again as raw source (PARSE002); an error no such unit holds, or one that survives that, and any
    // depth error, keep the whole source as one raw item (Opaque) instead.
    internal static ParseResult<CsDocument> Parse(SourceSnapshot source, ParseOptions options, CancellationToken cancellation)
    {
        LanguageFeatures.EnsureSupported(options.LanguageVersion);
        var rejected = new Dictionary<int, string>();
        for (int attempt = 0; ; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var parser = new DocumentParser(source, options.LanguageVersion, options.PreprocessorSymbols, cancellation) { rejected = rejected };
            var result = parser.Attempt(attempt == RecoveryAttempts);
            if (result != null) return result;
        }
    }

    const int RecoveryAttempts = 2;

    ParseResult<CsDocument>? Attempt(bool last)
    {
        var items = Items("", 1);
        var document = new CsDocument(version) { Items = new(items) };
        document = document with { Comments = ContainerComments(0, source.Text.Length, document.Children) };
        document = document with
        {
            Origin = new(source, document, 0, source.Text.Length)
            { CoreStart = 0, CoreEnd = source.Text.Length, ListStart = 0, ListEnd = source.Text.Length }
        };
        cancellation.ThrowIfCancellationRequested();
        document = (CsDocument)AttachSyntaxTrivia(document);
        cancellation.ThrowIfCancellationRequested();
        // Backstop: the recovery sites keep every parsed node within the limit, so a result that still
        // exceeds it, or that validation still rejects as too deep, is a parser defect. It is never
        // returned typed: the whole source is kept as one raw item.
        if (Height(document, Limits.MaxNestingDepth) > Limits.MaxNestingDepth) return OpaqueDocument(NestingMessage);
        var (errors, sites) = Validator.Sites(document);
        if (errors.Any(e => e.Code == "GEN001")) return OpaqueDocument(NestingMessage);
        bool retry = false;
        for (int i = 0; i < errors.Count; i++)
        {
            if (errors[i].Code is "GEN072" or "GEN073") continue;
            int site = sites[i].LastOrDefault(siteStarts.Contains, -1);
            if (last || site < 0 || rejected.ContainsKey(site)) return OpaqueDocument(errors[i].Message);
            rejected[site] = errors[i].Message; retry = true;
        }
        if (retry) return null;
        foreach (var error in errors.Where(e => e.Code is "GEN072" or "GEN073"))
            Error(error.Code, error.Message, 0, 0);
        return new(document, new(diagnostics), Coverage());
    }

    // Called by each recovery site before it parses a unit at the current token.
    void Site()
    {
        siteStarts.Add(Current.Start);
        if (rejected.TryGetValue(Current.Start, out var message)) throw new ParseFailure(message, kept: true);
    }

    ParseResult<CsDocument> OpaqueDocument(string message)
    {
        var raw = new CsRaw(source.Text);
        raw = raw with { Origin = new(source, raw, 0, source.Text.Length) { CoreStart = 0, CoreEnd = source.Text.Length } };
        var document = new CsDocument(version) { Items = new(new CsNode[] { raw }) };
        document = document with
        {
            Origin = new(source, document, 0, source.Text.Length)
            { CoreStart = 0, CoreEnd = source.Text.Length, ListStart = 0, ListEnd = source.Text.Length }
        };
        diagnostics.RemoveAll(d => d.Code == "PARSE002");
        Error("PARSE002", message, 0, source.Text.Length);
        return new(document, new(diagnostics), ParseCoverage.Opaque);
    }

    ParseCoverage Coverage() => diagnostics.Count == 0 ? ParseCoverage.Typed :
        diagnostics.All(d => d.Code == "PARSE002") ? ParseCoverage.Partial : ParseCoverage.Partial;

    // `depth` is the depth of the items themselves: 1 in a document, one more than the namespace inside one.
    List<CsNode> Items(string close, int depth)
    {
        var result = new List<CsNode>();
        while (Current.Text != close && Current.Text.Length != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (pending.Count != 0) { result.Add(pending.Dequeue()); continue; }
            int before = p;
            try { Site(); result.Add(Fit(Item(depth), depth)); }
            catch (ParseFailure failure) { p = before; result.Add(RecoverNode(failure.Message, !failure.Kept)); }
            catch (NestingFailure) when (depth < Limits.MaxNestingDepth) { p = before; result.Add(RecoverNode(NestingMessage, false)); }
            if (p <= before) throw new InvalidOperationException("Parser did not advance.");
        }
        while (pending.Count != 0) result.Add(pending.Dequeue());
        return result;
    }

    CsNode Item(int depth)
    {
        Guard(depth);
        if (Current.IsDisabled) { int disabledStart = p; string text = tokens[p++].Text; return Bind(new CsDisabledText(text), disabledStart); }
        if (Current.Text == "extern" && Peek(1) == "alias")
        { int aliasStart = p; p += 2; string alias = Identifier(); Expect(";"); return Bind(new CsExternAlias(alias), aliasStart); }
        if (Current.Text == "namespace") return Namespace(depth);
        if (Current.Text == "using" || Current.Text == "global" && Peek(1) == "using")
        {
            int beforeUsing = p;
            try { return UsingDirective(depth); }
            catch (ParseFailure) { p = beforeUsing; }
        }
        if (Current.Text.StartsWith('#')) return Directive();
        if (Current.Text == "[" && Peek(1) is "assembly" or "module" && Peek(2) == ":")
        {
            var fileAttributes = Attributes(depth, true);
            foreach (var attribute in fileAttributes.Skip(1)) pending.Enqueue(attribute);
            return fileAttributes[0];
        }
        int start = p;
        var attributes = Attributes(depth + 1);
        var modifiers = Modifiers();
        if (Current.Text == "delegate") return DelegateDeclaration(start, attributes, modifiers, depth);
        if (!IsTypeKeyword(Current.Text, depth))
        {
            // The statement model is never deeper than the symbol model it becomes; the item site measures the latter.
            p = start; var statement = Statement(depth + 1);
            return Bind(new CsGlobalStatement(SymbolParser.Statement(statement)), start);
        }
        return TypeDeclaration(start, attributes, modifiers, depth);
    }

    CsNode Namespace(int depth)
    {
        int start = p++;
        int nameStart = Current.Start;
        string name = QualifiedName();
        bool file = Take(";");
        int open = file ? Last.End : Expect("{").End;
        var items = Items(file ? "" : "}", depth + 1);
        int close = file ? source.Text.Length : Current.Start;
        if (!file) Expect("}");
        return Bind(new CsNamespace(name, new(items), file), start, "Name", nameStart, name.Length,
            open, close);
    }

    CsUsing UsingDirective(int depth)
    {
        int start = p;
        bool global = Take("global");
        Expect("using");
        bool statik = Take("static");
        bool unsafeContext = Take("unsafe");
        string? alias = null;
        if (Peek(1) == "=") { alias = Current.Text; p += 2; }
        int nameStart = Current.Start;
        var type = TypeName(depth + 1);
        Expect(";");
        return type.Form == TypeForm.Named ? Bind(new CsUsing(type.Name, alias, statik, global) { IsUnsafe = unsafeContext }, start, "Name", nameStart, type.Name.Length) :
            Bind(new CsUsing("", alias, statik, global) { Target = type, IsUnsafe = unsafeContext }, start);
    }

    string QualifiedName()
    {
        var name = new System.Text.StringBuilder(Identifier());
        if (Take("::")) name.Append("::").Append(Identifier());
        while (Take(".")) name.Append('.').Append(Identifier());
        return name.ToString();
    }

    string Identifier()
    {
        if (!Rendering.Names.IsIdentifier(Current.Text))
            throw new ParseFailure("Identifier expected.");
        return tokens[p++].Text;
    }

    Token Expect(string text) => Take(text) ? Last : throw new ParseFailure("Expected '" + text + "'.");

    bool Take(string text) { if (Current.Text != text) return false; p++; return true; }

    string Peek(int offset) => tokens[Math.Min(p + offset, tokens.Count - 1)].Text;

    bool IsTypeKeyword(string word, int depth) => word is "class" or "struct" or "interface" or "enum" or "record" || word == "union" && UnionDeclarationAhead(depth);

    bool UnionDeclarationAhead(int depth)
    {
        int before = p;
        try
        {
            p++; Identifier(); Generics(depth + 1); Expect("(");
            if (Current.Text == ")") return false;
            do { TypeName(depth + 1); } while (Take(","));
            Expect(")");
            return Current.Text is "where" or ";" or "{";
        }
        catch (ParseFailure) { return false; }
        finally { p = before; }
    }

    internal static readonly string NestingMessage = "Nesting exceeds " + Limits.MaxNestingDepth + ".";

    // `depth` is the depth, in Children edges from the root, of the node being built. Callers pass the
    // node's own depth to a parse that returns it and one more for each child edge, never resetting at a
    // statement, member, body or lambda, so every recursion cycle adds depth and stays bounded.
    static void Guard(int depth) { if (depth > Limits.MaxNestingDepth) throw new NestingFailure(); }

    // Types are also parsed speculatively (casts, tuples, lambda headers, generic arguments), where a
    // deeper reading of the same tokens must not end the parse: a too-deep type only fails that reading.
    static void TypeGuard(int depth) { if (depth > Limits.MaxNestingDepth) throw new ParseFailure(NestingMessage, kept: true); }

    // Exact check of a finished node placed at `depth`: its whole subtree, comments and trivia included.
    // Recovery sites call it on each unit; iterative chains call it on every new link of the spine.
    static T Fit<T>(T node, int depth) where T : CsNode
    {
        if (depth + Height(node, Limits.MaxNestingDepth - depth) > Limits.MaxNestingDepth) throw new NestingFailure();
        return node;
    }

    static T FitType<T>(T type, int depth) where T : CsNode
    {
        if (depth + Height(type, Limits.MaxNestingDepth - depth) > Limits.MaxNestingDepth) throw new ParseFailure(NestingMessage, kept: true);
        return type;
    }

    // Children-edge height of `node`, iteratively; stops as soon as it exceeds `limit`.
    internal static int Height(CsNode node, int limit)
    {
        int height = 0;
        var stack = new Stack<(CsNode Node, int Depth)>();
        stack.Push((node, 0));
        while (stack.Count != 0)
        {
            var (current, depth) = stack.Pop();
            if (depth > height && (height = depth) > limit) break;
            foreach (var child in current.Children) stack.Push((child, depth + 1));
        }
        return height;
    }

    void Error(string code, string message, int offset, int length = 0)
    {
        var (line, column) = Position(offset);
        diagnostics.Add(new(code, code == "PARSE002" ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            offset, length, line, column, message));
    }
}
