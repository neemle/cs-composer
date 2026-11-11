# Neemle.CSharpComposer

A .NET 10 library for composing, parsing and editing C# source through an immutable DOM. It has no Roslyn or
Generator dependency, performs no path-based file IO and never executes supplied source.

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

Expressions cover identifiers, literals, member access, calls, await, assignment, arithmetic, comparisons and
Boolean operators. Statements include locals/var, return, blocks, if/else, try/catch/finally, throw/rethrow and using:

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
Loops/switch, catch filters, await-using, lambdas and unmodeled C# 15 constructs currently require Raw.

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
