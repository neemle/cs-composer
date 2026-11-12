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

    Token Current => tokens[Math.Min(p, tokens.Count - 1)];

    Token Last => tokens[Math.Max(0, p - 1)];

    DocumentParser(SourceSnapshot source, CsLanguageVersion version, IEnumerable<string>? symbols = null)
    {
        this.source = source;
        this.version = version;
        all = Lexer.Scan(source.Text, evaluateDirectives: true, symbols);
        tokens = all.Where(t => !t.IsComment).ToList();
        ExtractSyntaxTrivia();
        foreach (var token in all.Where(t => t.IsMalformed))
            Error("PARSE001", "Unterminated literal or comment.", token.Start, token.End - token.Start);
    }

    internal static ParseResult<CsDocument> Parse(SourceSnapshot source, ParseOptions options)
    {
        LanguageFeatures.EnsureSupported(options.LanguageVersion);
        var parser = new DocumentParser(source, options.LanguageVersion, options.PreprocessorSymbols);
        var items = parser.Items("", 0);
        var document = new CsDocument(options.LanguageVersion) { Items = new(items) };
        document = document with { Comments = parser.ContainerComments(0, source.Text.Length, document.Children) };
        document = document with
        {
            Origin = new(source, document, 0, source.Text.Length)
            { CoreStart = 0, CoreEnd = source.Text.Length, ListStart = 0, ListEnd = source.Text.Length }
        };
        document = (CsDocument)parser.AttachSyntaxTrivia(document);
        foreach (var error in document.Validate().Where(e => e.Code is "GEN072" or "GEN073"))
            parser.Error(error.Code, error.Message, 0, 0);
        return new(document, new(parser.diagnostics), parser.Coverage());
    }

    ParseCoverage Coverage() => diagnostics.Count == 0 ? ParseCoverage.Typed :
        diagnostics.All(d => d.Code == "PARSE002") ? ParseCoverage.Partial : ParseCoverage.Partial;

    List<CsNode> Items(string close, int depth)
    {
        Guard(depth);
        var result = new List<CsNode>();
        while (Current.Text != close && Current.Text.Length != 0)
        {
            if (pending.Count != 0) { result.Add(pending.Dequeue()); continue; }
            int before = p;
            try { result.Add(Item(depth)); }
            catch (ParseFailure failure) { p = before; result.Add(RecoverNode(failure.Message)); }
            if (p <= before) throw new InvalidOperationException("Parser did not advance.");
        }
        while (pending.Count != 0) result.Add(pending.Dequeue());
        return result;
    }

    CsNode Item(int depth)
    {
        if (Current.IsDisabled) { int disabledStart = p; string text = tokens[p++].Text; return Bind(new CsDisabledText(text), disabledStart); }
        if (Current.Text == "extern" && Peek(1) == "alias")
        { int aliasStart = p; p += 2; string alias = Identifier(); Expect(";"); return Bind(new CsExternAlias(alias), aliasStart); }
        if (Current.Text == "namespace") return Namespace(depth + 1);
        if (Current.Text == "using" || Current.Text == "global" && Peek(1) == "using")
        {
            int beforeUsing = p;
            try { return UsingDirective(); }
            catch (ParseFailure) { p = beforeUsing; }
        }
        if (Current.Text.StartsWith('#')) return Directive();
        if (Current.Text == "[" && Peek(1) is "assembly" or "module" && Peek(2) == ":")
        {
            var fileAttributes = Attributes(true);
            foreach (var attribute in fileAttributes.Skip(1)) pending.Enqueue(attribute);
            return fileAttributes[0];
        }
        int start = p;
        var attributes = Attributes();
        var modifiers = Modifiers();
        if (Current.Text == "delegate") return DelegateDeclaration(start, attributes, modifiers, depth + 1);
        if (!IsTypeKeyword(Current.Text))
        {
            p = start; var statement = Statement(depth + 1);
            return Bind(new CsGlobalStatement(SymbolParser.Statement(statement)), start);
        }
        return TypeDeclaration(start, attributes, modifiers, depth + 1);
    }

    CsNode Namespace(int depth)
    {
        int start = p++;
        int nameStart = Current.Start;
        string name = QualifiedName();
        bool file = Take(";");
        int open = file ? Last.End : Expect("{").End;
        var items = Items(file ? "" : "}", depth);
        int close = file ? source.Text.Length : Current.Start;
        if (!file) Expect("}");
        return Bind(new CsNamespace(name, new(items), file), start, "Name", nameStart, name.Length,
            open, close);
    }

    CsUsing UsingDirective()
    {
        int start = p;
        bool global = Take("global");
        Expect("using");
        bool statik = Take("static");
        bool unsafeContext = Take("unsafe");
        string? alias = null;
        if (Peek(1) == "=") { alias = Current.Text; p += 2; }
        int nameStart = Current.Start;
        var type = TypeName();
        Expect(";");
        return type.Form == TypeForm.Named ? Bind(new CsUsing(type.Name, alias, statik, global) { IsUnsafe = unsafeContext }, start, "Name", nameStart, type.Name.Length) :
            Bind(new CsUsing("", alias, statik, global) { Target = type, IsUnsafe = unsafeContext }, start);
    }

    string QualifiedName()
    {
        string name = Identifier();
        if (Take("::")) name += "::" + Identifier();
        while (Take(".")) name += "." + Identifier();
        return name;
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

    bool IsTypeKeyword(string word) => word is "class" or "struct" or "interface" or "enum" or "record" || word == "union" && UnionDeclarationAhead();

    bool UnionDeclarationAhead()
    {
        int before = p;
        try
        {
            p++; Identifier(); Generics(); Expect("(");
            if (Current.Text == ")") return false;
            do { TypeName(); } while (Take(","));
            Expect(")");
            return Current.Text is "where" or ";" or "{";
        }
        catch (ParseFailure) { return false; }
        finally { p = before; }
    }

    static void Guard(int depth) { if (depth > 128) throw new ParseFailure("Nesting exceeds128."); }

    void Error(string code, string message, int offset, int length = 0)
    {
        int line = 1, column = 1;
        for (int i = 0; i < offset; i++)
        {
            if (source.Text[i] == '\r') { line++; column = 1; if (i + 1 < offset && source.Text[i + 1] == '\n') i++; }
            else if (source.Text[i] == '\n') { line++; column = 1; }
            else column++;
        }
        diagnostics.Add(new(code, code == "PARSE002" ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            offset, length, line, column, message));
    }
}
