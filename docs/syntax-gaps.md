# Syntax audit regressions

Audit date: 2026-11-12. The eight counterexamples below are now fixed and covered by `SyntaxGapRegressionTests`.

The following eight compilation units were accepted without errors by the C# 14
compiler from .NET SDK 10.0.100, with unsafe code enabled. Each initially exposed a gap in Composer using `CsLanguageVersion.CSharp14`.
The regression suite now requires typed parsing, validation, source preservation,
formatting idempotence and compiler acceptance for these cases.
For typed results, reformatted output was checked by the same compiler.

The examples describe the former failures so their fixes remain reproducible.
The 250-form inventory describes node families; combination tests and compiler
execution supply additional evidence beyond that inventory.

## Directives between parts of a construct

Composer handles directives at compilation, member and statement boundaries,
but these valid placements previously produced partial parsing and recovery nodes.

Inside an expression:

```csharp
class C
{
    int M() => (
#if true
        1
#else
        2
#endif
    );
}
```

Inside a parameter list:

```csharp
class C
{
    void M(
#if true
        int x
#else
        string x
#endif
    ) { }
}
```

Between attribute lists:

```csharp
[System.Obsolete]
#if true
[System.Serializable]
#endif
class C { }
```

## Named indexer arguments

```csharp
class C
{
    int this[int index] => index;
    int M() => this[index: 1];
}
```

Composer previously reported `Expected ']'`. Element access previously parsed a plain expression list
instead of argument nodes, losing the supported named-argument grammar here.

## Cast operands starting with address-of or dereference

```csharp
unsafe class C
{
    void M()
    {
        int x = 1;
        int* p = (int*)&x;
    }
}
```

Composer previously reported `Expression expected` and recovers source.

```csharp
unsafe class C
{
    int M(int* p) => (int)*p;
}
```

The second case was more serious: Composer previously reported `Typed` and no validation
errors, but parsed a multiplication instead of a cast. Reformatting emitted
`(@int) * p`, which the compiler rejects. Cast lookahead did not admit `&` or
`*` as the operand's starting token.

## A declared type named var

```csharp
class var { }
class C
{
    void M()
    {
        var x;
        x = new var();
    }
}
```

Composer previously treated `var` unconditionally as an implicitly typed local declaration
and rejected the missing initializer, although it denotes the declared type here.

## Nullable type in an is expression

```csharp
class C
{
    bool M(object x) => x is int?;
}
```

The compiler accepts this form. Composer previously reported `Expression expected` and
recovered source. This is distinct from a declaration pattern with a nullable type.

## Follow-up audit and regression checks

All findings recorded here are fixed. Final full-suite verification: **725 passed,
0 failed, 0 skipped**, with a clean Release build and whitespace verification for
all 147 changed/new C# files. The follow-up audit contributes 73 test cases.

`SyntaxGapRegressionTests` also covers:

- Casts before collection expressions, leading-dot numeric literals,
  interpolated strings and escaped identifiers.
- `nameof` and `await` used as method names; non-async lambda boundaries and
  contextual names inside interpolation.
- Types named `partial`, `async` and `union`, and partial constructors.
- Named and `in` arguments in indexer access.
- Attributed and scoped extension receivers.
- Nested directives, directives inside generic arguments and modifiers,
  CRLF edits, detachment, programmatic semantic attachments, comment ownership,
  newly attached conditionals and concurrent deterministic rendering.

`SyntaxCompositionAuditTests` extends the audit beyond individually named nodes:

- Three executable programs wrap every token, including punctuation, in
  `#if/#else/#endif`, with unterminated literals in inactive text. The tests
  require typed parsing, preserved source, stable formatting, detachment and
  compiler execution.
- **336 numeric and Boolean operator combinations** check runtime results
  after formatting, including multiplication versus pointer-type ambiguity.
- The grammar corpus checks replacement of every exposed child in declaration
  fixtures, including additional field declarators, fixed sizes and receiver
  attributes.
- Array variable initializers in locals, fields, properties and `for` headers,
  plus parenthesized queries, compile and execute.

The public `CsNode.SyntaxTrivia` collection holds typed `CsSyntaxTrivia`
attachments containing `CsDirective` or `CsDisabledText`. Callers attach them to
semantic nodes with leading/trailing placement; lexical alignment metadata is
maintained internally by the parser and formatter. No punctuation assembly is
required in the public construction API.
