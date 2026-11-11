# Neemle.CSharpComposer

A small .NET10 library for constructing, inspecting, validating and rendering C# source. Composer has no
Roslyn or Generator dependency. It renders directly from immutable snapshots; it never executes caller text.

```csharp
using Neemle.CSharpComposer;
using Neemle.CSharpComposer.Model;

CsDocument document = Cs.Document(CsLanguageVersion.CSharp14)
    .Namespace("Example").Class("Logic").Public()
    .Method<int>("Compute").Public().Parameter<int>("input").Body(
        Cs.Var("result", Cs.Binary(CsOperator.Multiply, Cs.Id("input"), Cs.Literal(2))),
        Cs.If(Cs.Binary(CsOperator.GreaterThan, Cs.Id("result"), Cs.Literal(0)),
            Cs.Body(Cs.Return(Cs.Id("result"))), Cs.Block(Cs.Return(Cs.Literal(0)))))
    .End().End();
string source = document.Render();
```

`Cs` is the root factory. DOM records/enums live in `Model`; fluent builders, rendering options and diagnostics
live in `Builders`, `Rendering` and `Validation`. Existing declaration/type/generic/attribute/constraint APIs remain.
Collections copy their inputs; snapshots and ordered children participate in structural equality.

Logic supports typed `Local` or initialized `Var`, `Block`, `If` with optional block/if else, `Try` with ordered
`Catch`/`CatchAll` and optional finally, `Throw`, contextual `Rethrow`, and synchronous `Using` over an expression
or initialized local declaration. They compose with identifiers/literals/member access/calls/await/assignment/return.
`Cs.Binary` accepts Add, Subtract, Multiply, Divide, Modulo, Equal, NotEqual, LessThan, LessThanOrEqual,
GreaterThan, GreaterThanOrEqual, AndAlso and OrElse. `Cs.Unary` accepts UnaryPlus, Negate and Not.
Grouping preserves the submitted tree, including right-nested subtraction/division and composite receivers.

```csharp
var guarded = Cs.Try(
    Cs.Body(Cs.Statement(Cs.Id("Work").Call())),
    new[] { Cs.Catch(Cs.Type<Exception>(), Cs.Body(Cs.Rethrow()), "error") },
    Cs.Body(Cs.Statement(Cs.Id("Cleanup").Call())));
var scoped = Cs.Using(Cs.Var("owned", Cs.Id("Open").Call()), Cs.Body(guarded));
```

Document language targets are explicit7.3,8,9,10,11,12,13,14; the default is14. `Cs.Document(version)` and
`new CsDocument(version)` select a target. A target is snapshot/equality state; unknown/latest/preview values
are rejected. Known-target builders and final document checks reject unavailable structured features with GEN073.
`RenderOptions.Validate=false` does not bypass language guards or bounded null/depth protection. It remains possible
to render an older target with block-scoped namespaces through `RenderOptions.NamespaceStyle`.

| Minimum | Structured features |
| --- | --- |
| 7.3 or earlier | Ordinary declarations/types/attributes, tuples, global qualification, ref/readonly structs, ref/in parameters; all new logic forms |
| 8 | Nullable annotations/directives/constraints, readonly members, implemented/private/static interface members |
| 9 | Record classes/primary parameters, init, enhanced partial methods |
| 10 | Record structs, global usings, file-scoped namespaces, parameterless struct constructors and instance initializers |
| 11 | Required, scoped ref/in, static abstract/virtual interface members |
| 12 | Ref readonly parameters |
| 13 | Identifiable non-array params collections and ref-struct interface lists |

Record classes use `record` at target9 and retain `record class` from10. Selecting a newer language does not add
unmodeled syntax: class/struct primary parameters, loops/switch, catch filters, await-using and a lambda DSL remain
outside this API. Type binding, inheritance relationships and definite assignment belong to the caller/compiler.

Raw type/expression/statement/body/member escapes are trusted caller text. Ordinary rendering does not parse them or infer
syntax/language availability. Raw cannot disable surrounding structured validation. Literal/identifier escaping,
ordered members and the existing LF/final-newline/indent options remain. Default rendering throws
`GenValidationException` with stable code/path/message diagnostics; new structured/version failures use GEN064–073.


Run pure tests/build with only this checkout mounted:

```bash
mkdir -p .dev-temp/tmp artifacts/gene9-tests
docker run --rm -v "$PWD:/work" -w /work \
  -e DOTNET_CLI_HOME=/work/.dev-temp/dotnet -e NUGET_PACKAGES=/work/.dev-temp/nuget \
  -e TMPDIR=/work/.dev-temp/tmp mcr.microsoft.com/dotnet/sdk:10.0 bash tests/verify-container.sh
```

The local package is `.dev-temp/packages/Neemle.CSharpComposer.0.3.0.nupkg`. Independent tests use xunit.core/assert;
compiler integration stays in Generator's checkout. Paired verification there is `bash tests/verify.sh` with optional
`COMPOSER_CHECKOUT=/absolute/path`. It checks actual packages, SourceText/AddSource, the real compiler host and an
explicit C#14 compiler/runtime witness. Each run retains its own original input inventory, TRX and coverage report.

## Parse and edit supplied source

```csharp
using Neemle.CSharpComposer.Parsing;
var parsed = Cs.Parse(bytes); // string, byte[] or readable Stream
CsDocument document = parsed.Root;
var location = document.Descendants().Single(x => x.Node is CsMethod { Name: "Compute" });
var method = (CsMethod)location.Node;
var edited = document.Replace(location.Path, method with { Name = "Calculate" });
byte[] output = edited.ToBytes();
```

`CsNode.Children` projects the same typed records used by construction. Attached line/block/XML comments are node
content. `NodePath` selects an occurrence; Replace/Remove/Insert/Move return immutable documents. Detach permits an
explicit copy; paths returned by traversal are bound to their snapshot. `CsRaw.Parse(new ParseContext(...))` explicitly
parses a document/member/expression/statement sequence; normal raw rendering never executes or implicitly parses text.

Parsed unchanged output preserves copied source bytes, encoding/BOM and newline state. IO reads/writes caller streams
at their current positions, never seeks/truncates/disposes them, and propagates strict decoding/IO errors.
Supported BOMs: UTF8,UTF16LE/BE,UTF32LE/BE; unmarked input defaults to strictUTF8. New documents use UTF8/noBOM.
`Reformat()` or `Reformat(path)` requests formatting; `EmissionDiagnostics` identifies opaque formatting limitations.
Unsupported syntax remains raw/recovery with positioned diagnostics; this is not a complete C# compiler/parser.
Bound scalar edits preserve token spans; changes without a token binding rerender their owning subtree.
