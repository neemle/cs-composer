# Neemle.CSharpComposer

A .NET 10 library for composing, parsing and editing C# source through an immutable DOM. It has no Roslyn or
Generator dependency, performs no path-based file IO and never executes supplied source.

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
`Validate` and `Render`. Paths belong to immutable snapshots. Options and compound symbols participate in
traversal, structural equality and rewriting. Bound edits retain surrounding source; canonical formatting honors
`RenderOptions.Indent`. Validation rejects contradictory or unused options, invalid identifiers, instruction
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
Unedited parsed output preserves bytes, encoding/BOM, comments and newline state. Bound scalar edits patch their
token spans. A newly built node that replaces a parsed expression or statement renders in that node's place only, so
the source around it (line breaks, indentation, directive lines and comments) stays as it was; when it binds less
tightly than the node it replaces, or when the parent itself changed, the smallest owning node renders again from the
model instead. A changed link of a multi-line chain renders again without the links below it, which keep their lines.
In a region that renders again, the code after a directive that splits an expression or a statement continues one
indentation level past the line where that expression or statement starts.

`Replace`, `Remove`, `Insert` and `Move` return new document snapshots. Traversal paths belong to their snapshot;
`Detach()` makes an explicit reusable copy. The original document remains unchanged.

## Comments and rerendering

Line, block and XML documentation comments belong to nodes, including documents, types, methods and statements:

```csharp
var documented = method with
{
    Comments = new[] { new CsComment("/// Computes the value.", CommentKind.Documentation) }
};
```

Adjacent leading/documentation comments attach to the next declaration; same-line trailing comments attach to the
preceding node. Blank-line-separated preamble, container and EOF comments remain with their container. Moving a
node carries its owned comments; container comments stay owned by their container. A container comment stays before
the item that followed it in source, keeping its blank lines; when that item is removed it moves on to the next item
that remains (or to the end of the container), and the blank line that separated the removed item goes with it.
A rendered or reformatted container comment is followed by a blank line before the next item, so that it reads back as
the container's comment, not as that item's leading comment.

`document.Reformat()` rerenders the document. `document.Reformat(path)` rerenders only the selected subtree and
replaces its envelope. Outside source stays unchanged. Literal and comment contents are preserved;
`EmissionDiagnostics` reports opaque regions outside formatting coverage. No separate formatter is invoked.
Edited and rerendered regions start at the indentation of the surrounding source. Line breaks they add follow
the neighbouring source newline (CRLF, LF or CR), and whole-document `Reformat()` uses the document's most
common newline.

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

These nodes participate in traversal, equality, detachment, comments, source-preserving edits and formatting.
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
Unedited source and bound scalar edits preserve header trivia; canonical formatting retains header comments as
loop container comments. Compiler execution tests cover initialization/iterator order, zero/one/many iterations,
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
in traversal, detachment and source-preserving edits. Execution tests cover closures, callbacks, nested lambdas,
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
does not bypass version, null/depth or ownership safeguards. Older targets can use block-scoped namespaces through
`RenderOptions.NamespaceStyle`. New documents default to four-space indentation, LF/final newline and UTF8 without BOM.
`RenderOptions.Newline` (`"\n"` or `"\r\n"`) sets the line break of the output; left null, a built document uses LF and a
parsed one keeps its own. The formatter writes Allman braces, braces every embedded statement (an `else if` chain
stays a chain), separates members and top-level items by one blank line (none between consecutive fields, enum members,
usings or extern aliases, none inside statement lists) and writes `#region` lines at the code's indentation and every
other directive at column 0. A line longer than `RenderOptions.MaxLineLength` (default 120, 0 or less turns width
breaking off) breaks its argument, parameter and type-argument lists, initializers, collection and switch expressions
one item per line, a chain of two or more calls before each `.` after the first call, and `&&`, `||` and `??` chains
before each operator; a line comment inside a line always ends it. Block lambdas keep their braces on lines of their
own, and literals, comments and opaque text are written as they are.

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
  the namespace or top-level item. Siblings stay typed and editable, and untouched output stays byte-identical.
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
conflicting encodings fail visibly rather than using replacement characters.

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
- When a type's header holds a directive of its own and an edit changes the header next to or between its directives
  (for example a base type added inside an `#if` branch), the whole type renders again, so that no text moves into or
  out of a branch. Other header changes render only the header, and its comments and directives keep their place.
- In a raw interpolated string, changing a text segment or a hole's format so that it holds a line break, a quote or a
  brace, or changing text that spans lines, renders the whole literal again in the writer's form. Other edits of an
  interpolated string change only the edited text, format or node.

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

It checks P3, OWN-c and IDEM once per file on the untouched parse, in two ways: rendered by `CsDocument.Render()`, and
rendered fresh from a detached copy. It also edits a small seeded sample of nodes per file, at most 5 per variant and
one per node kind, and checks P3, OWN-b, OWN-c and IDEM after each edit. The variants are `detach` (replace the node
with `node.Detach()`), `remove` (remove a list item) and `move` (move a list item to the start of its list). An edit
that the library refuses counts as rejected, not as a violation. Source layout is not part of any property: output is
not compared with the source bytes.

CI runs the gate as its own step, without coverage collection, so the Unit and Integration coverage gates never depend
on corpus content. It is report-only for now. It writes `corpus-gate.json`, which holds counts per property and
variant, the most frequent violation patterns with examples, and timings. CI uploads that file as the `corpus-gate`
artifact. Violations do not fail the run; only infrastructure errors do. The manual `Corpus gate (full)` workflow runs
the same gate in one job with a larger sample, 25 nodes per file and variant by default. To run the gate locally on a
few files:

```bash
CORPUS_GATE_FILES="Model/CsBody.cs;Tests/LoopTests.cs" CORPUS_GATE_REPORT=.dev-temp/corpus-gate.json \
    dotnet test tests/Neemle.CSharpComposer.Tests -c Release --filter Category=Corpus
```

Settings are environment variables:
- `CORPUS_GATE_FILES` takes semicolon-separated path fragments.
- `CORPUS_GATE_VARIANTS` selects the edits: any of `detach`, `remove`, `move` and `reformat`
  (`node.Detach().Reformat()`), comma-separated. The default is `detach,remove,move`; `none` runs only the per-file
  checks.
- `CORPUS_GATE_NODES_PER_FILE` caps the edited nodes per file and variant. The default is 5, and `all` edits every
  eligible node.
- `CORPUS_GATE_UNTOUCHED` is `preserved`, `fresh`, `both` (the default) or `none`.
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
   renames a method, inserts a method, moves a commented method to another class and reformats one member, then
   emits to `byte[]` and to a `Stream`. It checks that untouched regions are byte-identical, that the moved
   comments appear exactly once and that every line break is still CRLF.
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
