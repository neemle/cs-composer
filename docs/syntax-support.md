# Full syntax support: implementation and acceptance plan

Target: a semantic, typed DOM for C# 7.3 through 15. A symbol owns its
name and nested options; compounds hold ordered symbols; instructions share
the tree. Callers never need to assemble keyword or punctuation tokens.

The [follow-up audit](syntax-gaps.md) fixes the eight original counterexamples
and adds compiler-backed tests of contextual names, extension receivers,
operator combinations, directive placement and semantic-tree editing. The matrix
inventories node classes; combination and execution tests are separate checks.

A syntax form is complete only when all of these work:

1. Construct it through typed public model properties without `Raw`.
2. Parse it back into semantic nodes, including all nested operands.
3. Traverse and replace those operands through the common tree.
4. Reject incompatible payloads, missing operands and unsupported versions.
5. Format and reparse without changing operator binding or statement scope.
6. Preserve unchanged source and comments when editing parsed documents.
7. Exercise positive and negative tests, and compile executable fixtures where
   the installed compiler supports that language version.

C# 15 coverage is tied to .NET 11.0.100 and the Microsoft language reference
as of 2026-11-12. Passing C# 14 compiler tests does not certify C# 15 features.
Memory safety remains a separate preview feature; it is not silently enabled
by selecting the stable C# 15 version.

## Work order

| Stage | Required families | State |
| --- | --- | --- |
| Grammar audit | Inventory concrete Roslyn syntax forms; group by semantic role and language version | Implemented: [250-form matrix](syntax-coverage.json), reference hash and executable fixtures |
| Generator expressions | Element access, casts, typeof/default/is, conditional access/coalescing/assignment/ternary, array creation, nested object/collection initializers | Implemented; regression and compiler fixtures added |
| Remaining expression primitives | All unary/binary/assignment operators, generic references, argument modifiers/names, tuples, ranges, collection/spread, anonymous objects, stackalloc, with, checked/ref/this/base | Implemented |
| Patterns and bindings | Recursive/property/positional/list/slice patterns, nested and deconstructed designations, ref locals/foreach bindings | Implemented |
| Executable instructions | Complete switch/try/using projection into common tree; catch filters; lock/fixed/unsafe/checked; yield/goto/labels; local functions; declaration groups | Implemented |
| Functions and queries | Anonymous methods, all lambda signatures/attributes/defaults/return modifiers; every LINQ query clause/continuation | Implemented |
| Type syntax | Qualified and alias-qualified constructed names, unbound generics, function pointers and calling conventions, ref/scoped forms | Implemented |
| Declarations | Delegates, destructors, operators/conversions, indexers, explicit interfaces, extension blocks, primary constructor bases, partial properties/events/constructors and new constraints | Implemented |
| Compilation units | Top-level instructions, extern aliases, all using forms, directives and structured documentation trivia | Implemented |
| Integration | Full coverage manifest, version boundary tests, all regression suites, formatting of every changed/new C# file | Verified: Release build, full suite and formatting checks pass |

The implementation ships in 0.5.0; NuGet 0.4.0 predates these changes. The matrix
maps semantic aggregates rather than requiring punctuation nodes in the public API.
Malformed input (`IncompleteMember`, `SkippedTokensTrivia`, `BadDirectiveTrivia`)
uses diagnostic recovery. Disabled conditional text is a data node, not executable
source hidden inside a raw expression.

## Verification

- The grammar corpus rejects every `CsRaw`, `CsRawMember`, raw expression,
  statement or type in its parsed and reformatted trees.
- Every stable C# target (7.3, 8, 9, 10, 11, 12, 13, 14, 15) has a compilation
  and execution fixture using the corresponding compiler language mode.
- .NET 11.0.100 additionally compiles and executes the explicitly gated memory
  safety preview fixture.
- Invalid option payloads, language boundaries, graph ownership, recursion,
  source edits, CRLF preservation and formatting idempotency have regression tests.
- The DOM is not a replacement compiler: unresolved name binding, conversions,
  overload resolution and definite assignment are checked by the C# compiler.

Final verification: **725 tests passed, 0 failed, 0 skipped**. Release build:
**0 warnings, 0 errors**. Coverage: **96.63% lines**, **84.68% branches**.
Whitespace formatting was applied to all **147 changed/new C# files** and verified
with `dotnet format whitespace --verify-no-changes`; `git diff --check` also passes.
The follow-up audit adds **73 regression/audit test cases**, including **336**
operator combinations checked through compiler execution.
