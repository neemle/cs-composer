# Neemle.CSharpComposer

A .NET 10 library for composing, parsing and editing C# source through an immutable DOM. It has no Roslyn or
Generator dependency, performs no path-based file IO and never executes supplied source.

**Unreleased (0.7.0, GENE-29):** every render formats the whole document. Parsed source is no longer written back byte
for byte: whitespace, indentation and blank lines come from the formatter, comments belong to nodes and move with
them, and every checked render reads its own output back to prove it means the model. See
[Comments and formatting](#comments-and-formatting).

**0.6.1:** source-editing fixes: edits keep the surrounding layout (directives, chains, lambdas, interpolated
strings, type headers, brace-less statements) and never change meaning. See [Known limitations](#known-limitations).

**0.6.0:** parse-time nesting limit with raw recovery, iterative chains of any length, cancellable parsing,
parser and edit-layout fixes, and 100% unit and integration coverage gates. See [Known limitations](#known-limitations).

**0.5.0:** expanded typed syntax support, semantic directives and source-editing fixes.
See the [compiler-backed follow-up audit and regression scope](docs/syntax-gaps.md).

## Symbols and instructions in one tree

The common tree holds semantic nodes directly: a symbol has a name and immutable options; its options may
contain nested symbols. A compound symbol owns an ordered collection of other symbols/expressions. Instructions
share the same `CsNode` tree. No token concatenation or manual expression/statement casts are required.

```csharp
var ms = Cs.Symbol("ms", new()
{
    AsVar = true,
    Using = true,
    Value = Cs.Symbol(new()
    {
        Type = Cs.Type<System.IO.MemoryStream>(),
        AsNew = true,
        Arguments = Cs.Compound()
    })
});

var tree = Cs.Tree(ms,
    Cs.Symbol("WriteByte", new()
    {
        Target = Cs.Symbol("ms"),
        AsCall = true,
        Arguments = Cs.Compound(Cs.Value(42))
    }),
    Cs.Instruction(InstructionKind.Return, new()
    {
        Value = Cs.Symbol("Length", new() { Target = Cs.Symbol("ms") })
    }));

string bodySource = tree.Render();
// using var ms = new global::System.IO.MemoryStream();
// ms.WriteByte(42);
// return ms.Length;
```

`Using` and `AsVar` belong to the declared symbol, and `AsNew` belongs to its initializer. `Arguments` is a
compound symbol, even when empty; `Symbol` supplies an empty compound automatically for calls/constructions.
Compound parts are copied into immutable snapshots. `Value`, `Target` and `Parts` accept semantic nodes;
`Type` is a structured `CsType`. `Cs.Value(data)` encodes a literal as a symbol. Calls, construction, member
access, unary/binary operators and assignments are symbol options. For example, `Cs.Symbol("i", new()
{ AsAssignment = true, Operator = CsOperator.Add, Value = Cs.Value(2) })` represents `i += 2` in any valid context.

`Instruction` takes a closed `InstructionKind` and typed `CsInstructionOptions`. Block, if, return/throw,
break/continue, for, foreach, while and do/while use the same model. Bodies are `CsTree`; operands, bindings and
conditions are nested nodes. Foreach uses `Symbol` for its binding, `Value` for the collection and `Await` as a
modifier. Await disposal is `Using = true, Await = true` on the declaration symbol. For initializers accept a
declaration symbol or compound expression list; iterators are compound symbols. Existing typed statements and
expressions remain interoperable, including switch/try/lambda nodes. `MethodBuilder.Body(tree)` keeps the common
tree in the document DOM rather than requiring callers to convert nodes individually.

`Cs.ParseTree(source, version)` projects statement sequences into symbols and instructions, including
lambda parameters and query clauses as nested symbols. `SymbolOperation` identifies additional semantic
operations; `ParameterSymbols` and query `Parts` are compounds, so callers do not assemble lexical tokens.
`Cs.Parse(source, options)` covers compilation units, declarations, directives and structured XML documentation.
The checked-in [syntax matrix](docs/syntax-coverage.json) maps 250 reference grammar forms to their model and
exercised corpus fixture; three entries describe malformed-input recovery rather than valid language constructs.

The tree supports `Children`, `Descendants`, `At`, `Replace`, `Insert`, `Remove`, `Move`, `Detach`, `Reformat`,
`Validate` and `Render` (`Reformat` is obsolete and returns its input). Paths belong to immutable snapshots. Options
and compound symbols participate in traversal, structural equality and rewriting. Every render formats the whole
document and honors `RenderOptions.Indent`. Validation rejects contradictory or unused options, invalid identifiers, instruction
slots, malformed arguments, invalid transfer contexts, null/deep/duplicated-origin graphs and version violations.
Rendering uses the existing typed backend; type resolution, conversions and overload selection remain compiler
responsibilities. Compilation/execution tests cover every stable target from C# 7.3 through 15 and explicitly opted-in preview syntax.

## Create a document

Documents have no filename. Use fluent builders or construct the same model records directly:

```csharp
using Neemle.CSharpComposer;
using Neemle.CSharpComposer.Model;

CsDocument document = Cs.Document().Namespace("Example")
    .Class("User").Public().Partial()
    .Property<int>("Id").Public().Get().Init().End()
    .Property<string>("Name").Public().Required().Get().Init().End()
    .End();

string source = document.Render();
byte[] bytes = document.ToBytes();
```

`CsNode.Children` exposes ordered children from the typed properties. Collections copy their inputs; builders
produce immutable snapshots. Structural equality compares model content, excluding source-origin and formatting
metadata. Declarations include classes, structs, records, interfaces, enums, fields, properties, methods,
constructors, events and nested types, with attributes, generics and constraints.

The namespaces follow the folders: `Model`, `Builders`, `Parsing`, `Rendering`, `Validation` and `IO`.
`Cs` remains the root factory in `Neemle.CSharpComposer`.

## Parse, inspect and edit

`Cs.Parse` accepts a string, byte array or readable stream and returns a root, diagnostics and typed/partial/opaque
coverage. Parsed nodes use the same DOM as newly constructed nodes:

```csharp
using System.Text;
using Neemle.CSharpComposer;
using Neemle.CSharpComposer.Model;

var parsed = Cs.Parse(Encoding.UTF8.GetBytes("class C { int Compute() { return 1; } }"));
var location = parsed.Root.Descendants().Single(x => x.Node is CsMethod { Name: "Compute" });
var method = (CsMethod)location.Node;
var edited = parsed.Root.Replace(location.Path, method with { Name = "Calculate" });
byte[] output = edited.ToBytes();
```

Inspect diagnostics before relying on typed coverage. Malformed regions retain source in explicit recovery nodes
with positioned diagnostics. Inactive conditional source is `CsDisabledText`; preprocessor symbols are supplied
through `ParseOptions.PreprocessorSymbols`. Composer validates syntax structure and feature versions; the C#
compiler remains responsible for type binding, overload selection, conversions and definite assignment.
Rendering a parsed document, edited or not, formats all of it (see [Comments and formatting](#comments-and-formatting)):
its encoding and BOM are kept and its comments, literals and inactive text survive, but its whitespace, indentation and
blank lines are not read back from the source.

`Replace`, `Remove`, `Insert` and `Move` return new document snapshots. Traversal paths belong to their snapshot;
`Detach()` makes an explicit reusable copy. The original document remains unchanged.

## Comments and formatting

Line, block and XML documentation comments belong to nodes, including documents, types, methods and statements:

```csharp
var documented = method with
{
    Comments = new[] { new CsComment("/// Computes the value.", CommentKind.Documentation) }
};
```

A parse gives every comment to exactly one node, by one rule that looks at the tokens around the comment and the
innermost node around it:

1. A comment that starts on the line of the token before it is a trailing comment of the outermost node that ends at
   that token (a `,` stands for the list item before it). A comment that starts on the line where such a comment ends
   joins it.
2. Otherwise it is a leading comment of the outermost node that starts at the next token (a directive or inactive text
   counts as one), whatever blank lines lie between: a comment between two members belongs to the member after it.
3. Otherwise, before a closing `}`, `)`, `]` or the end of the file, it is a container comment of the node that closes
   there (at the end of a body, a type, an initializer or the document).
4. Otherwise it is a trailing comment of the outermost node that ends at the token before it, else a leading comment of
   the first part of its construct after it, else its construct's own container comment (`return /* c */;`).

The comments that open a file and are followed by a blank line (a license header, `// <auto-generated/>`) are the
document's own leading comments. Comments inside raw or recovered source, inactive `#if` text and directive lines are
part of that text. A documentation comment is a leading comment. A comment over several lines is stored without the
indentation its later lines share with the comment's start, and the formatter writes those lines at the indentation
the comment is written at.

A comment moves, survives and is removed with its owner; a container comment stays with its container. The formatter
writes a node's leading comments before it (on lines of their own before a member, statement or list item that starts
a line, inline before anything else, where a line comment ends the line), its trailing comments after it (after its
list separator) and its container comments at the end of its list or body, or before its closing `;`. A comment built
on a node that is not the owner the parser would choose (a leading comment on the receiver of a call chain, say) is
written at that node and reads back on the parser's owner.

Every render (`Render`, `ToBytes`, `WriteTo`, `CsTree.Render`) formats the whole document; its output depends only on
the model, the `RenderOptions` and the document's newline. There is no separate formatter and nothing to opt into:
`Reformat` is `[Obsolete]`, returns its input unchanged and will be removed in 0.8.0. The formatter:

- indents with `RenderOptions.Indent` (four spaces by default) and puts braces on their own lines. Embedded statements
  are always braced, auto-properties stay on one line, and an `else if` stays a chain;
- separates type members and top-level items by one blank line (none between consecutive fields, enum members, usings
  or extern aliases), puts one blank line after the usings, the file preamble and a file-scoped namespace line, and
  never writes two blank lines in a row;
- breaks argument, parameter and type-argument lists, initializers, collection expressions, switch-expression arms,
  call chains of two or more calls and `&&`/`||`/`??` chains that do not fit in `RenderOptions.MaxLineLength` (120 by
  default; 0 or less turns width breaking off), or that hold a line comment;
- writes `#region` and `#endregion` at the code's indentation and every other directive at column 0;
- sorts each run of consecutive document-level usings when `RenderOptions.SortUsings` is true (the default), parsed
  and built documents alike, unless a using of the run holds a leading comment or the document holds container
  comments (a sort would carry those comments away from the place they describe);
- writes string, character and raw literals, the text parts of interpolated strings, comment text, inactive `#if`
  text and opaque units (raw nodes and recovered source) as they are, except that an opaque unit's first line starts
  at the formatter's indentation (or, when it starts with a directive that did not start its line in the source,
  continues the line before it) and a comment's later lines start at the comment's indentation. `EmissionDiagnostics`
  reports each opaque node as `FORMAT001`.

`RenderOptions.Newline` chooses `"\n"` or `"\r\n"`. Left null, a parsed document or tree renders with its dominant
newline (CRLF when its layout holds more CRLF than LF line breaks, else LF) and a built one with LF. Line breaks inside
literals and inactive text are never converted; line breaks inside comments are written with the output's newline.
Output ends with exactly one line break.

With `RenderOptions.Validate` (the default), every render parses its own output once and compares it with the model.
Where an expression or pattern reads back differently, the render adds the parentheses it needs and tries again; output
that still does not read back as the model, or a comment that would read back on another owner, is refused with
`GenValidationException` and `EMIT001` instead of being returned. Rendering the re-parse of a render gives the same
text again.

## Structured logic and Raw

Expressions cover identifiers, literals, member access, calls, await, assignment, arithmetic, comparisons,
Boolean operators, increment/decrement, switch expressions and lambdas. Statements include locals/var, return,
blocks, if/else, for/foreach/while/do-while, switch/case/default, break/continue, goto case/default,
try/catch/finally, throw/rethrow and using:

```csharp
var body = Cs.Body(
    Cs.Var("result", Cs.Binary(CsOperator.Multiply, Cs.Id("input"), Cs.Literal(2))),
    Cs.If(Cs.Binary(CsOperator.GreaterThan, Cs.Id("result"), Cs.Literal(0)),
        Cs.Body(Cs.Return(Cs.Id("result"))), Cs.Block(Cs.Return(Cs.Literal(0)))));

CsDocument logic = Cs.Document().Class("Logic").Public()
    .Method<int>("Compute").Public().Parameter<int>("input").Body(body).End().End();
```

Raw type/expression/statement/body/member escapes preserve caller syntax without inferring validity or availability.
Explicit `raw.Parse(new ParseContext(ParseKind.Expression))` attempts typed conversion; failed conversion preserves
the original Raw node. Ordinary rendering never executes Raw or implicitly converts it into a structured tree.

### Switch statements and expressions

Switches have typed, immutable sections, labels, patterns and arms. Multiple labels share one statement list;
the renderer does not add block scopes or implicit breaks. Add `Cs.Block(...)` explicitly when a section needs
its own local-variable scope:

```csharp
var statement = Cs.Switch(Cs.Id("input"),
    Cs.Section(Cs.Body(Cs.Return(Cs.Literal(10))), Cs.Case(Cs.Literal(1)), Cs.Case(Cs.Literal(2))),
    Cs.Section(Cs.Body(Cs.Break()), Cs.Default()));

var expression = Cs.SwitchExpression(Cs.Id("input"),
    Cs.Arm(Cs.AndPattern(
        Cs.RelationalPattern(CsOperator.GreaterThanOrEqual, Cs.Literal(0)),
        Cs.RelationalPattern(CsOperator.LessThan, Cs.Literal(10))), Cs.Literal(1)),
    Cs.Arm(Cs.DiscardPattern(), Cs.Literal(0)));
```

`Cs.Case(pattern, when: guard)` and `Cs.Arm(pattern, value, when: guard)` model guards. Factories cover
constant, declaration (`int n`), var, discard, type, relational, not/and/or and parenthesized patterns.
Use `Cs.GotoCase(value)` / `Cs.GotoDefault()` for explicit transfers and `Cs.ThrowExpression(error)` for an
arm that throws. Switch expressions require C# 8; type-only, relational, logical and parenthesized patterns
require C# 9. Constant/declaration/var case labels work on the minimum target, C# 7.3.

These nodes participate in traversal, equality, detachment, comments, edits and formatting.
Validation rejects malformed payloads, repeated default labels, syntactically identical subsumed patterns,
obvious nonconstant case values, non-Boolean literal guards, known section fallthrough, and switch jumps
outside their enclosing switch (including jumps out of finally). A default label can appear before cases.
Raw or symbol-dependent control flow is left to the compiler. No exhaustive matching, constant folding,
symbol binding, type compatibility, definite assignment, full pattern subsumption, or goto-case target binding
is performed. Ambiguous bare pattern names retain constant-name syntax; their type/constant meaning is resolved
by the compiler. Compiler execution tests cover generated switches under C# 7.3, 8 and 9.

### Loops

`Cs.For`, `Cs.ForEach`, `Cs.While` and `Cs.DoWhile` model ordinary synchronous iteration on every supported
language target. Loop bodies use `CsBody`; parsed single-statement and empty bodies retain their original source,
while canonical formatting uses braces. `Cs.Empty()` represents an explicit empty statement.

```csharp
var loop = Cs.For(
    Cs.ForVar("i", Cs.Literal(0)),
    Cs.Binary(CsOperator.LessThan, Cs.Id("i"), Cs.Id("limit")),
    new[] { Cs.PostIncrement(Cs.Id("i")) },
    Cs.Body(Cs.If(Cs.Id("skip"), Cs.Body(Cs.Continue())),
        Cs.Statement(Cs.Id("Process").Call(Cs.Id("i")))));

var each = Cs.ForEach("item", Cs.Id("items"), // var item
    Cs.Body(Cs.Statement(Cs.Id("Process").Call(Cs.Id("item")))));
var typedEach = Cs.ForEach(Cs.Int, "item", Cs.Id("items"), Cs.Body(Cs.Break()));
var repeat = Cs.DoWhile(Cs.Body(Cs.Statement(Cs.Id("Tick").Call())), Cs.Id("ready"));
```

Use `Cs.ForDeclaration(type, Cs.Variable(...), ...)` for multiple variables sharing an explicit type,
`Cs.ForExpressions(...)` for a statement-expression initializer list, or `null` for no initializer. Conditions
can be omitted only on `for`; iterators are an immutable list and may be empty. `Cs.For(body)` models `for (;;)`. A
`var` initializer declares exactly one initialized variable. Mutation factories are `PreIncrement`,
`PostIncrement`, `PreDecrement` and `PostDecrement`; plain assignments and calls also work in headers.

Validation checks header/body shapes, identifiers, duplicate declaration names, obvious non-Boolean literal
conditions, writable mutation target shapes, and statement-expression forms in for initializers/iterators.
`break` requires an enclosing loop or switch; `continue` requires a loop, including through a nested switch.
Both are forbidden when they would leave a finally block, but loops nested inside that finally can use them.
Switch fallthrough analysis distinguishes loop exits, continue paths, and constant-true/false conditions;
Raw and symbol-dependent control flow remains conservative. No loop lowering or implicit control transfers occur.
A comment in a loop header belongs to the header part next to it. Compiler execution tests cover initialization/iterator order, zero/one/many iterations,
nested switch transfers, finally execution and foreach enumerator disposal under C# 7.3 and 14.

### Lambdas

Lambdas are general expression nodes: use them as arguments, initializers, assignments and return values.
Both expression and block bodies are typed. They can capture their surrounding scope; the C# compiler handles
capture analysis and delegate/expression-tree conversions.

```csharp
var add = Cs.Lambda(
    Cs.Binary(CsOperator.Add, Cs.Id("x"), Cs.Id("offset")), Cs.LambdaParameter("x"));

var callback = Cs.Lambda(Cs.Body(Cs.Statement(Cs.Id("Run").Call())));
var asyncCallback = Cs.AsyncLambda(Cs.Await(Cs.Id("Fetch").Call()));

var query = Cs.Id("items").Member("Where").Call(
    Cs.Lambda(Cs.Binary(CsOperator.GreaterThan, Cs.Id("x"), Cs.Literal(0)), Cs.LambdaParameter("x")));
```

`Cs.LambdaParameter(name, type: ..., modifier: ...)` supports inferred parameters or explicit types with
`ref`/`out`/`in`. Parameter types must be all inferred or all explicit. `IsAsync` and `IsStatic` are immutable
expression properties; `Cs.AsyncLambda` is a convenience factory. Static lambdas and repeated `_` discard
parameters require C# 9; direct `var` initialization with a lambda requires C# 10. Earlier targets need an
explicit delegate or expression-tree target type. C# 14 also permits modifiers on inferred parameters. Async lambdas cannot have ref/out/in parameters.

Lambda bodies establish a new function boundary: outer loops, switches and catches do not authorize a break,
continue, goto-case/default or rethrow inside them. Loops and catches within the lambda establish their own
contexts. `Cs.ThrowExpression` also works as a lambda expression body. Parameters and block bodies participate
in traversal, detachment and edits. Execution tests cover closures, callbacks, nested lambdas,
async/ref functions and LINQ method calls under C# 7.3, 9 and 14. These nodes do not perform full signature,
overload, capture or return-type analysis; the compiler remains authoritative for those checks.

### Syntax coverage

The semantic primitives cover indexing, casts and type operations; every operator family; conditional access,
coalescing and assignments; array/object/collection creation and initializers; tuples and deconstruction; ranges;
queries; structured interpolation; anonymous functions; recursive/list patterns; executable instructions;
member/type declarations; compilation items; directives and XML documentation with structured crefs.

C# 15 includes unions, closed hierarchies, extension indexers, collection constructor arguments and labeled
break/continue. See the [implementation plan and acceptance checks](docs/syntax-support.md) and the
[executable grammar corpus](tests/Neemle.CSharpComposer.Tests/SyntaxCorpus.json). Existing `Raw` escapes remain
explicit opt-ins and malformed-input recovery. The [follow-up audit](docs/syntax-gaps.md)
covers the corrected parser/formatter counterexamples and combination tests.
`CsNode.SyntaxTrivia` attaches typed directives and inactive text directly to semantic
nodes, including placements inside expressions, parameter lists and attributes.

## Language targets and validation

Supported output targets are **C# 7.3–15; default 15**. Select one with `Cs.Document(CsLanguageVersion.CSharp7_3)`
or `new CsDocument(version)`. The library targets `net10.0` and its implementation builds with C# 14; output language
selection is independent. Structured features are checked against their minimum version. Preview memory safety
requires the explicit `CsLanguageVersion.Preview` target; it is not silently enabled by selecting stable 15.

Rendering validates by default and throws `GenValidationException` with stable diagnostics. `RenderOptions.Validate=false`
does not bypass version, null/depth or ownership safeguards. A checked render also parses its own output once and compares
it with the model (the meaning guard): where an operand reads back regrouped, the render adds parentheses around it and
tries again; anything else that reads back differently, including a comment that would read back on another node, fails
with `EMIT001` ("Rendered output does not read back as the model") instead of returning text that means something else.
`Validate=false` skips this check too. Older targets can use block-scoped namespaces through
`RenderOptions.NamespaceStyle`. New documents default to four-space indentation, LF/final newline and UTF8 without BOM.
`RenderOptions.Newline` (`"\n"` or `"\r\n"`) sets the line break of the output; left null, a built document uses LF and a
parsed one keeps its own. The formatter writes Allman braces, braces every embedded statement (an `else if` chain
stays a chain), separates members and top-level items by one blank line (none between consecutive fields, enum members,
usings or extern aliases, none inside statement lists) and writes `#region` lines at the code's indentation and every
other directive at column 0. A line longer than `RenderOptions.MaxLineLength` (default 120, 0 or less turns width
breaking off) breaks its argument, parameter and type-argument lists, initializers, collection and switch expressions
one item per line, a chain of two or more calls before each `.` after the first call, and `&&`, `||` and `??` chains
before each operator; a line comment inside a line always ends it. Block lambdas keep their braces on lines of their
own, and literals, comment text and opaque text are written as they are.

## Limits and untrusted input

Composer has one nesting limit, **128**. A node's depth is the number of `Children` edges from the root of the
result (the document, or the node a fragment parse returns), and every node, comments and trivia included, must be
at most 128 deep. Nothing resets the count at a statement, member or lambda. Constructed documents deeper than the
limit are rejected with `GEN001` when rendered.

Chains are the one exception: a chain spine counts as **one level however long it is**. A spine is a run of
left-deep links, each a binary operator, member access (`.`, `?.`, `->`, `::`), invocation or element access whose
first operand (`Arguments[0]`) is again such a link; in the symbol model (`CsTree`, top-level statements) the same link
is `CsSymbol` → `Options` → receiver. So `a + b + ... + z`, `a.b.c...` and fluent builders of any length stay typed,
whether parsed or constructed. The chain's head and every other operand or argument sit one level below their link,
and other postfix forms (`!`, `++`/`--`, `as`, `is`, `with`, `switch`, ranges), parentheses, lambdas, blocks and
right-nested operators (`?:`, assignment, `??`) still count one level each. Every pass (parsing, validation,
rendering, reformatting, edits, `Descendants`, `Detach`, equality and hashing) walks a spine in a loop, so a chain of
N links costs time and memory in proportion to N and no stack per link. Directives (`#if`, `#region`) inside a long
chain are an exception: each one anchors to the tokens of its owning link, so their cost grows with chain length.

Paths keep their formats with two compressions for long chains:

- In a validation diagnostic (`GenDiagnostic.Path`), a run of more than 128 consecutive spine steps is written once
  as `.Arguments[0]×k` (multiplication sign, U+00D7), for example `...Expression.Arguments[0]×100000.Text`. Runs of
  up to 128 steps are written step by step as before.
- `NodePath.Segments` always lists every child index. Its 128-segment cap counts a run of consecutive spine steps as
  one segment, so nodes deep inside a long chain stay addressable with `At`, `Replace`, `Remove`, `Insert` and `Move`.

`Cs.Parse` enforces the same limit while parsing and never returns a typed tree that rendering would reject:

- A construct that is too deep keeps its nearest recovery unit as raw source: the statement in a statement list,
  otherwise the member (for expression bodies, initializers, parameter defaults and attribute arguments), otherwise
  the namespace or top-level item. Siblings stay typed and editable, and the raw unit is written verbatim.
- Each such unit reports `PARSE002` (Warning, `Nesting exceeds 128.`) positioned on the raw unit, and coverage is
  `Partial`. `PARSE001` (Error) is reserved for malformed input.
- A parsed statement, member or top-level item that validation rejects (for example `N()++`, `((int)x) = 1` or a
  `file` member nested in a type, `GEN034`) is also kept as raw source with `PARSE002` and the validation message.
- `CsNode.Parse(ParseContext)` and `Cs.ParseTree` have no smaller unit: a fragment that is too deep returns its
  source as raw with `PARSE002` and `Opaque` coverage. If a parsed document ever still failed these checks, the
  whole source would be returned as one raw item with `Opaque` coverage rather than as a typed tree.
- A `Cs.ParseTree` input that parses but that validation rejects is returned the same way, as one raw item with
  `Opaque` coverage. Its diagnostics are the validation errors (`GEN` codes, or `SYM001`), each with severity
  `Error`.
- Rendering an `Opaque` tree (`CsTree.Render`) returns its source verbatim. Render options are still checked, so an
  invalid `Indent` is rejected exactly as for a typed tree, with the `ArgumentException` that `CsDocument.Render`
  throws for it.

Parsing has no size or time limit of its own, and its cost grows with input size. Before parsing untrusted input,
bound its size, and pass a `CancellationToken` (for example from `CancellationTokenSource.CancelAfter`) to bound
time. Every parse entry point has an overload that takes one: `Cs.Parse(byte[] | string | Stream, ParseOptions?,
CancellationToken)`, `Cs.ParseTree(string, ParseOptions, CancellationToken)` and
`CsNode.Parse(ParseContext, CancellationToken)`. Cancellation is checked between items, members and statements,
along chains and token scans, and before each post-parse pass; it throws `OperationCanceledException` and returns
no partial result. A cancelled stream parse leaves the caller's stream open.

## Streams and encoding

Parsing reads a stream from its current position to EOF; writing uses `document.WriteTo(stream)` at the destination's
current position. Nonseekable streams are supported. Composer never seeks, truncates, flushes or disposes caller
streams, including on failure. IO errors propagate and partial writes are not transactional.

A parsed document keeps no reference to the supplied byte array or stream: it holds the decoded text and a copy of the
BOM, so changing the caller's buffer after `Cs.Parse` cannot change the document. BOM detection supports UTF8,
UTF16LE/BE and UTF32LE/BE; unmarked input defaults to strict UTF8. `ParseOptions.Encoding` selects another encoding
explicitly; when its own BOM is present it takes precedence, so UTF16LE text that starts with U+0000 is not read as
UTF32LE. Parsed encoding/BOM are preserved unless overridden by `EmitOptions`; invalid bytes/characters and
conflicting encodings fail visibly rather than using replacement characters. The newline is not kept line by line:
a parsed document is written with its dominant newline unless `RenderOptions.Newline` says otherwise (see
[Comments and formatting](#comments-and-formatting)).

Invalid input bytes throw `System.Text.DecoderFallbackException`. Its `Index` (also named in the message) is the
absolute byte offset of the first invalid sequence, counted from the first byte of the supplied `byte[]`, or from
the stream's position when `Cs.Parse(Stream)` was called, and it includes any BOM. For UTF-8 BOM + `class C {}` +
`0xFF`, `Index` is 13. `BytesUnknown` holds the rejected bytes. `Cs.Parse(string)` with an encoding that cannot
represent the text throws `EncoderFallbackException`, and `ToBytes`/`WriteTo` do the same for rendered text; there
`Index` is a UTF-16 character index into the text being encoded.

## Known limitations

The current version has these known limitations:

- An `Opaque` `Cs.ParseTree` result (see [Limits and untrusted input](#limits-and-untrusted-input)) renders verbatim
  but cannot be edited: `Replace` and `Insert` throw `GenValidationException` with `SYM001`. This is by design: an
  opaque tree is source that did not validate, so it has no typed nodes to edit. To edit the valid parts of such
  source, parse it with `Cs.Parse`, which keeps only the rejected statement or member as raw source.
- The cost of directives inside a long chain grows with the chain's length; see
  [Limits and untrusted input](#limits-and-untrusted-input).
- By design, source layout is not preserved: every render formats the whole document, so whitespace, indentation,
  blank lines and mixed newlines of parsed source are replaced by the formatter's (see
  [Comments and formatting](#comments-and-formatting)).
- By design, inactive `#if` text and opaque units (raw nodes and recovered source) are written verbatim and are not
  formatted.
- A comment built on a node that is not the owner the parser would choose (see
  [Comments and formatting](#comments-and-formatting)) is written at that node and reads back on the parser's owner,
  which may write it elsewhere: rendering the re-parse of such a model can differ from its first render. Parsed models
  hold every comment on that owner, so their render is a fixed point.
- Some built comment arrangements cannot be written so that they read back on their owner, and their render is refused
  with `GenValidationException` and `EMIT001` rather than move or drop the comment: a trailing comment after a trailing
  line comment (it would start a line of its own), and a comment on a text part of an interpolated string.
- A raw unit whose text starts with a directive (a `#` that did not start its line in the source) continues the line
  before it. Where that line cannot go on (it ends with a line comment or is a directive), and inside a block lambda,
  the unit starts a line and reads back as a directive, so its render is refused with `EMIT001`.

## Build and verify

```bash
dotnet build Neemle.CSharpComposer.slnx -c Release
dotnet test tests/Neemle.CSharpComposer.Tests -c Release
```

Install both .NET 10 and .NET 11 SDKs for the full compiler test suite. When .NET 11 is installed separately,
set `COMPOSER_TEST_DOTNET11` to its `dotnet` executable. CI installs both SDKs. `global.json` builds the repository
with the .NET 10 SDK: coverlet does not report branches of .NET 11 SDK builds reliably, and CI requires 100% line,
branch and method coverage from the unit and the integration tests separately. The C# 15 compiler tests build their
generated projects outside the repository, so they still use the .NET 11 SDK.

To run one category and its coverage gate as CI does, pass `--filter Category=Unit` (or `Category=Integration`) with
the coverage settings, then run the gate on the results directory with the same category name:

```bash
dotnet test tests/Neemle.CSharpComposer.Tests -c Release --filter Category=Unit \
    --settings tests/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory .dev-temp/results/unit
dotnet run tools/CoverageGate.cs -- .dev-temp/results/unit Unit
```

The gate, [`tools/CoverageGate.cs`](tools/CoverageGate.cs), is a C# file-based app that uses only the .NET base class
library, so it needs nothing beyond the .NET 10 SDK. Run it from the repository root so that `global.json` selects that
SDK. It is not part of the solution or the package. It expects exactly one `coverage.cobertura.xml` in the results
directory, ignoring the copies the TRX logger puts under an `In/` directory, so use an empty directory for each run. It
prints line, branch and method coverage, lists the uncovered lines, branches and methods, and exits with 1 unless all
three are 100%. It exits with 2 on a usage error, a missing report or package, or a results directory that does not hold
exactly one report.

Every test class carries exactly one class-level `[Trait("Category", "Unit")]`, `[Trait("Category", "Integration")]` or
`[Trait("Category", "Corpus")]`. A class without one would run in no filtered run and drop out of the gates.
`TestCategoryTests` enforces this: it fails when a test class has no Category trait, several or an unknown one, when a
test method sets its own, or when a category holds no tests.

The `Corpus` category holds the corpus gate, `CorpusPropertyGateTests`. It runs over every `src/**` and `tests/**`
`.cs` file, which the test project embeds, and checks these properties:
- OWN-a: the comments lexed from the source equal the comments of the parsed model, plus the comments inside opaque and
  inactive text and on directive lines. Text is compared line by line with surrounding whitespace trimmed.
- P3: re-parsing the output gives the same coverage and diagnostic codes and a tree equal to the intended model. Added
  parentheses and an `else` braced around one statement are treated as transparent.
- OWN-b: after an edit, the model's comments are the original's. For a removal, they are the original's minus exactly
  the removed subtree's.
- OWN-c: every comment of the re-parse has the same owner, placement and text as in the intended model.
- IDEM: rendering the re-parse gives the same text again.

It checks P3, OWN-c and IDEM once per file on the untouched parse, rendered as every render formats it (with
`SortUsings` off, since sorting reorders the model). It also edits a small seeded sample of nodes per file, at most 5
per variant and one per node kind, and checks P3, OWN-b, OWN-c and IDEM after each edit. The variants are `detach`
(replace the node with `node.Detach()`), `remove` (remove a list item) and `move` (move a list item to the start of its
list). An edit that the library refuses counts as rejected, not as a violation. Source layout is not part of any
property: output is not compared with the source bytes.

CI runs the gate as its own step, without coverage collection, so the Unit and Integration coverage gates never depend
on corpus content. Since GENE-29 F4 the step is enforcing for the untouched files: a render error, a P3 or IDEM
violation of the untouched render, or an OWN-a violation fails it. The edit variants stay report-only until F6. It
writes `corpus-gate.json`, which holds counts per property and variant, the most frequent violation patterns with
examples, and timings. CI uploads that file as the `corpus-gate` artifact. Infrastructure errors always fail the run.
The manual `Corpus gate (full)` workflow runs the same gate in one job with a larger sample, 25 nodes per file and
variant by default, report-only. To run the gate locally on a few files:

```bash
CORPUS_GATE_FILES="Model/CsBody.cs;Tests/LoopTests.cs" CORPUS_GATE_REPORT=.dev-temp/corpus-gate.json \
    dotnet test tests/Neemle.CSharpComposer.Tests -c Release --filter Category=Corpus
```

Settings are environment variables:
- `CORPUS_GATE_FILES` takes semicolon-separated path fragments.
- `CORPUS_GATE_VARIANTS` selects the edits: any of `detach`, `remove` and `move`, comma-separated. The default is
  `detach,remove,move`; `none` runs only the per-file checks.
- `CORPUS_GATE_NODES_PER_FILE` caps the edited nodes per file and variant. The default is 5, and `all` edits every
  eligible node.
- `CORPUS_GATE_UNTOUCHED` is `formatted` (the default) or `none`.
- `CORPUS_GATE_ENFORCE` is `untouched` (the CI step) or `none` (the default).
- `CORPUS_GATE_SEED` changes the sample. The default is 29.
- `CORPUS_GATE_PARALLELISM` sets the number of workers.

Compiler integration lives in [Neemle.GeneratorEngine](https://github.com/ratushnyi-labs/generator-engine); it
consumes Composer as a package and submits documents with explicit generator hint names.

## Demo

[`samples/Neemle.CSharpComposer.Demo`](samples/Neemle.CSharpComposer.Demo) is a console application that uses only the
public API through a project reference. It is not packed and adds no package dependencies. The solution includes
it, so CI builds it, but CI does not run it:

```bash
dotnet run --project samples/Neemle.CSharpComposer.Demo -c Release
dotnet run --project samples/Neemle.CSharpComposer.Demo -c Release -- --iterations 20 --skip-compile
```

It runs four steps and exits with code 1 if any check fails:

1. Builds a filename-free document from scratch and emits it to `byte[]` and to a `Stream`.
2. Parses a CRLF source with comments from a `byte[]` and from a `Stream` that starts at a nonzero position. It
   renames a method, inserts a method and moves a commented method to another class, then emits to `byte[]` and to a
   `Stream`. It checks that the formatted output is a fixed point, that every comment and literal appears exactly
   once and that every line break is CRLF, the source's newline.
3. Compiles the emitted sources with the real C# compiler. Composer and the demo do not reference Roslyn. The demo
   writes the emitted bytes into a temporary net10.0 project (C# 14, nullable, warnings as errors) and runs
   `dotnet build` on it, so it needs the .NET 10 SDK. `--skip-compile` skips this step.
4. Times parse, untouched emit, edit (rename, insert, move) and edited emit over three generated workloads: 3
   warm-up iterations, then `--iterations` measured ones (default 10) on one thread. It prints the runtime, OS and
   load average, then the median, minimum and maximum wall-clock time and the allocated bytes per operation
   (`GC.GetAllocatedBytesForCurrentThread`). It checks that every iteration produces the same output. No time or
   allocation threshold is applied; timings taken on a busy machine are not comparable.

## Continuous integration

[Build and test](.github/workflows/build.yml) runs on pushes and pull requests to `main`, and published releases.
It restores locked dependencies, checks that the library and direct test graphs contain neither Roslyn nor Generator
and builds the Release solution. It then runs the `Unit` and `Integration` test categories as two separate test runs,
each with a TRX log and Cobertura coverage, and `tools/CoverageGate.cs` fails a run unless line, branch and method
coverage of `src/Neemle.CSharpComposer` are all 100%. The integration run and its gate still run when the unit tests or
the unit gate fail, so one CI run reports both categories. Last, it packs a local NuGet artifact. Test and coverage
reports are uploaded even when a step fails; the package is uploaded only after successful checks.

Published releases also run a separate publication job. The release tag must be `v` followed by the project version
(currently `v0.6.1`), and the tagged commit must be contained in `main`. The job downloads the package built and tested
by that same workflow run and pushes it to NuGet; it does not rebuild it. Normal pushes and pull requests never publish.
Rerunning an already published version skips the duplicate package.

Publication uses NuGet Trusted Publishing for the NuGet user `neemle`. Configure its GitHub policy with owner
`neemle`, repository `cs-composer` and workflow `build.yml`, without an environment restriction. Only the release
publication job has `id-token: write`; `NuGet/login` exchanges the GitHub OIDC token for a temporary API key.
No long-lived NuGet API key or repository secret is required. Publication fails if authentication returns no key.

## License

MIT. See [LICENSE](LICENSE). The package includes the redistribution notice along with its DLL and README.
