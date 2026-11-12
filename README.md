# Neemle.CSharpComposer

A .NET 10 library for composing, parsing and editing C# source through an immutable DOM. It has no Roslyn or
Generator dependency, performs no path-based file IO and never executes supplied source.

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

`Cs.ParseTree(source, version)` parses a statement sequence and projects declarations, values, calls, constructor
arguments and these instructions into the common model. It preserves unedited text and source bindings. Existing
switch/try/lambda and multi-variable for declarations currently retain their compatible typed nodes. Unsupported
or malformed syntax reports diagnostics and retains its source explicitly; inspect coverage before editing.
The symbol model is implemented; this does not mean every C# 7.3–15 syntax form has been migrated or that a full
semantic compiler has been added.

The tree supports `Children`, `Descendants`, `At`, `Replace`, `Insert`, `Remove`, `Move`, `Detach`, `Reformat`,
`Validate` and `Render`. Paths belong to immutable snapshots. Options and compound symbols participate in
traversal, structural equality and rewriting. Bound edits retain surrounding source; canonical formatting honors
`RenderOptions.Indent`. Validation rejects contradictory or unused options, invalid identifiers, instruction
slots, malformed arguments, invalid transfer contexts, null/deep/duplicated-origin graphs and version violations.
Rendering uses the existing typed backend; type resolution, conversions and overload selection remain compiler
responsibilities. Compilation/execution tests cover the symbol model under C# 8 and 14.

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

Inspect diagnostics before relying on typed coverage. Unsupported or malformed regions retain source in explicit
Raw/recovery nodes with positioned diagnostics; Composer is not a complete C# parser or semantic compiler.
Unedited parsed output preserves bytes, encoding/BOM, comments and newline state. Bound scalar edits patch their
token spans; changes without a binding conservatively rerender their owning subtree.

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
node carries its owned comments; container comments stay owned by their container.

`document.Reformat()` rerenders the document. `document.Reformat(path)` rerenders only the selected subtree and
replaces its envelope. Outside source stays unchanged. Literal and comment contents are preserved;
`EmissionDiagnostics` reports opaque regions outside formatting coverage. No separate formatter is invoked.

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
explicit delegate or expression-tree target type. Async lambdas cannot have ref/out/in parameters.

Lambda bodies establish a new function boundary: outer loops, switches and catches do not authorize a break,
continue, goto-case/default or rethrow inside them. Loops and catches within the lambda establish their own
contexts. `Cs.ThrowExpression` also works as a lambda expression body. Parameters and block bodies participate
in traversal, detachment and source-preserving edits. Execution tests cover closures, callbacks, nested lambdas,
async/ref functions and LINQ method calls under C# 7.3, 9 and 14. These nodes do not perform full signature,
overload, capture or return-type analysis; the compiler remains authoritative for those checks.

### Semantic building blocks still to implement

The semantic DOM is still incomplete. Missing forms below need typed expression, declaration, binding or modifier
support; arbitrary token sequences are not a replacement for those nodes. Existing Raw escapes remain available
while the semantic model is completed.

| Area | Missing semantic forms |
| --- | --- |
| Control flow | ordinary labels/goto, labeled break/continue, `yield`, local functions, ref/deconstructing foreach |
| Resource/exception blocks | catch filters, `lock`, `fixed`, checked/unchecked blocks |
| Expressions | array/collection creation and object/collection initializers, indexing, casts, `is`/`as`, `this`/`base`, `typeof`, `default`, conditional/null-conditional/null-coalescing operators, bitwise/shift operators and their compound assignments, query expressions, structured interpolation, tuple expressions/deconstruction, `with` |
| Advanced anonymous functions | lambda attributes, explicit return types, default/params parameters, modifiers on inferred parameters, anonymous `delegate` expressions |
| Patterns | recursive property/positional patterns and list/slice patterns |
| Declarations | delegates, indexers, destructors, operator/conversion declarations, explicit interface implementations, C# 15 unions |

## Language targets and validation

Supported output targets are **C# 7.3–15; default 15**. Select one with `Cs.Document(CsLanguageVersion.CSharp7_3)`
or `new CsDocument(version)`. The library targets `net10.0` and its implementation builds with C# 14; output language
selection is independent. Known structured features are checked against their minimum version. Selecting 15 does
not introduce a typed API for every C# 15 feature; unions and other unmodeled forms remain Raw. Preview memory-safety
features are not part of the standard 15 target.

Rendering validates by default and throws `GenValidationException` with stable diagnostics. `RenderOptions.Validate=false`
does not bypass version, null/depth or ownership safeguards. Older targets can use block-scoped namespaces through
`RenderOptions.NamespaceStyle`. New documents default to four-space indentation, LF/final newline and UTF8 without BOM.

## Streams and encoding

Parsing reads a stream from its current position to EOF; writing uses `document.WriteTo(stream)` at the destination's
current position. Nonseekable streams are supported. Composer never seeks, truncates, flushes or disposes caller
streams, including on failure. IO errors propagate and partial writes are not transactional.

Byte input is copied. BOM detection supports UTF8, UTF16LE/BE and UTF32LE/BE; unmarked input defaults to strict UTF8.
`ParseOptions.Encoding` selects another encoding explicitly. Parsed encoding/BOM are preserved unless overridden by
`EmitOptions`; invalid bytes/characters and conflicting encodings fail visibly rather than using replacement characters.

## Build and verify

```bash
dotnet build Neemle.CSharpComposer.slnx -c Release
dotnet test tests/Neemle.CSharpComposer.Tests -c Release
```

Use the .NET 10 SDK or newer. Compiler integration lives in
[Neemle.GeneratorEngine](https://github.com/ratushnyi-labs/generator-engine); it consumes Composer as a package and
submits documents with explicit generator hint names.

## Continuous integration

[Build and test](.github/workflows/build.yml) runs on pushes and pull requests to `main`, and published releases.
It restores locked dependencies,
checks that the library and direct test graphs contain neither Roslyn nor Generator, builds the Release solution,
runs tests with TRX/Cobertura coverage and packs a local NuGet artifact. Reports are uploaded even when a step fails;
the package is uploaded only after successful checks.

Published releases also run a separate publication job. The release tag must be `v` followed by the project version
(currently `v0.4.0`), and the tagged commit must be contained in `main`. The job downloads the package built and tested
by that same workflow run and pushes it to NuGet; it does not rebuild it. Normal pushes and pull requests never publish.
Rerunning an already published version skips the duplicate package.

Publication uses NuGet Trusted Publishing for the NuGet user `neemle`. Configure its GitHub policy with owner
`neemle`, repository `cs-composer` and workflow `build.yml`, without an environment restriction. Only the release
publication job has `id-token: write`; `NuGet/login` exchanges the GitHub OIDC token for a temporary API key.
No long-lived NuGet API key or repository secret is required. Publication fails if authentication returns no key.

## License

MIT. See [LICENSE](LICENSE). The package includes the redistribution notice along with its DLL and README.
