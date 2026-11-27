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

    // Start offsets of the units that parsed only after a too-deep type reading of their tokens was abandoned.
    // When validation rejects such a unit, it is valid source that is too deep (see tooDeepTypes).
    readonly HashSet<int> deepSites = new();

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
        // exceeds it (syntax trivia attached past the last site can), or that validation still rejects as too
        // deep, is never returned typed: the whole source is kept as one raw item.
        bool deep = Height(document, Limits.MaxNestingDepth) > Limits.MaxNestingDepth;
        var (errors, sites) = deep ? (Array.Empty<GenDiagnostic>(), Array.Empty<int[]>()) : Validator.Sites(document);
        if (TooDeep(deep, errors)) return OpaqueDocument(NestingMessage);
        bool retry = false;
        // Only a unit an earlier attempt rejected survives its retry; several errors in one unit reject it once.
        var earlier = new HashSet<int>(rejected.Keys);
        for (int i = 0; i < errors.Count; i++)
        {
            if (errors[i].Code is "GEN072" or "GEN073") continue;
            int site = sites[i].LastOrDefault(siteStarts.Contains, -1);
            if (last || site < 0 || earlier.Contains(site)) return OpaqueDocument(errors[i].Message);
            rejected.TryAdd(site, deepSites.Contains(site) ? NestingMessage : errors[i].Message); retry = true;
        }
        if (retry) return null;
        // Every error left here is a language-version limit (GEN072/GEN073): any other one returned above.
        foreach (var error in errors) Error(error.Code, error.Message, 0, 0);
        return new(document, new(diagnostics), Coverage());
    }

    // Called by each recovery site before it parses a unit at the current token.
    void Site()
    {
        siteStarts.Add(Current.Start);
        if (rejected.TryGetValue(Current.Start, out var message)) throw new ParseFailure(message, kept: true);
    }

    // Called by each recovery site after the unit that starts at token `start` parsed; `deepTypes` is the count
    // of too-deep type readings before it.
    void Parsed(int start, int deepTypes)
    {
        if (tooDeepTypes != deepTypes) deepSites.Add(tokens[start].Start);
    }

    // The message a recovery site keeps a failed unit with: a unit whose too-deep type reading was abandoned
    // and whose other reading then failed is too deep, not malformed.
    string FailureMessage(ParseFailure failure, int deepTypes) =>
        !failure.Kept && tooDeepTypes != deepTypes ? NestingMessage : failure.Message;

    // Parser-defect backstops, excluded from coverage (GENE-25): no parse reaches them unless a recovery site is
    // broken. A validation depth error on a parse that fits the limit would mean a recovery site let a too-deep
    // unit through; a loop that does not advance would mean a recovery site consumed no token.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Parser-defect backstop: validation reports no depth error for a parse within the limit.")]
    static bool TooDeep(bool height, IReadOnlyList<GenDiagnostic> errors) => height || errors.Any(e => e.Code == "GEN001");

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Parser-defect backstop: every recovery site consumes at least one token.")]
    static void Advanced(int position, int before, string message)
    {
        if (position <= before) throw new InvalidOperationException(message);
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

    ParseCoverage Coverage() => diagnostics.Count == 0 ? ParseCoverage.Typed : ParseCoverage.Partial;

    // `depth` is the depth of the items themselves: 1 in a document, one more than the namespace inside one.
    List<CsNode> Items(string close, int depth)
    {
        var result = new List<CsNode>();
        while (Current.Text != close && Current.Text.Length != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (pending.Count != 0) { result.Add(pending.Dequeue()); continue; }
            int before = p, deepTypes = tooDeepTypes;
            try { Site(); result.Add(Fit(Item(depth), depth)); Parsed(before, deepTypes); }
            catch (ParseFailure failure) { p = before; result.Add(RecoverNode(FailureMessage(failure, deepTypes), !failure.Kept && tooDeepTypes == deepTypes)); }
            catch (NestingFailure) when (depth < Limits.MaxNestingDepth) { p = before; result.Add(RecoverNode(NestingMessage, false)); }
            Advanced(p, before, "Parser did not advance.");
        }
        while (pending.Count != 0) result.Add(pending.Dequeue());
        return result;
    }

    CsNode Item(int depth)
    {
        Guard(depth);
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
    void TypeGuard(int depth) { if (depth > Limits.MaxNestingDepth) throw TooDeepType(); }

    // Too-deep type readings seen so far. When a speculative type reading fails only because it is too deep and
    // the other reading of the same tokens then fails as well (for example `F<G<...>>()` read as comparisons,
    // which no longer stop at the limit since chains count as one level), or parses into a unit validation
    // rejects, the unit is valid source that is too deep, not malformed source (GENE-19 D10).
    int tooDeepTypes;

    ParseFailure TooDeepType() { tooDeepTypes++; return new ParseFailure(NestingMessage, kept: true); }

    // Exact check of a finished node placed at `depth`: its whole subtree, comments and trivia included.
    // Recovery sites call it on each unit; iterative chains call it on every new link of the spine, which
    // costs only the nodes not measured before, so a chain is never rescanned per link (GENE-19 D10).
    T Fit<T>(T node, int depth) where T : CsNode
    {
        if (depth + Measure(node) > Limits.MaxNestingDepth) throw new NestingFailure();
        return node;
    }

    T FitType<T>(T type, int depth) where T : CsNode
    {
        if (depth + Measure(type) > Limits.MaxNestingDepth) throw TooDeepType();
        return type;
    }

    // Heights by the D10 metric of every node measured in this parse, capped one past the limit. Nodes are
    // immutable, so a height never changes once known.
    readonly Dictionary<CsNode, int> heights = new(ReferenceEqualityComparer.Instance);

    int Measure(CsNode node)
    {
        var stack = new Stack<(CsNode Node, IReadOnlyList<CsNode>? Children)>();
        stack.Push((node, null));
        while (stack.Count != 0)
        {
            var (current, children) = stack.Pop();
            if (children == null)
            {
                if (heights.ContainsKey(current)) continue;
                children = current.Children;
                stack.Push((current, children));
                foreach (var child in children) if (!heights.ContainsKey(child)) stack.Push((child, null));
                continue;
            }
            int spine = Spine.Index(current), height = 0;
            for (int i = 0; i < children.Count; i++) height = Math.Max(height, heights[children[i]] + (i == spine ? 0 : 1));
            heights[current] = Math.Min(height, Limits.MaxNestingDepth + 1);
        }
        return heights[node];
    }

    // Children-edge height of `node` by the D10 metric, iteratively; stops as soon as it exceeds `limit`.
    internal static int Height(CsNode node, int limit) => Spine.Height(node, limit);

    void Error(string code, string message, int offset, int length = 0)
    {
        var (line, column) = Position(offset);
        diagnostics.Add(new(code, code == "PARSE002" ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            offset, length, line, column, message));
    }
}
