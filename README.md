# Calque

Calque formats Clef source. It is a focused fork of
[Fantomas](https://github.com/fsprojects/fantomas), with a library, incremental
formatting API and CLI hosted on .NET 10. It retains Fantomas's Oak syntax tree,
comment and trivia handling, and printing pipeline, adapted to Clef's source
conventions.

## Formatting scope

Calque currently formats the Clef syntax supported by its inherited parser and
explicit syntax checks. Clef's language surface is still developing; a parser
refusal can indicate a formatter gap, including in working programs such as
HelloArty and HelloWayland. Broader support requires a Clef parser adapter.

- Comments, literal spelling and source order are preserved. Numeric widths,
  suffixes and conversions are left to the source and compiler.
- Imperative bindings, loops and record updates are supported source forms.
- Typed `<@ ... @>`, untyped `<@@ ... @@>` and nested quotations retain their
  structure and contents without evaluation. Quotation splices (`%expr` and
  `%%expr`) are refused under the current Clef contract; infix `%` remains valid.
- Nulls, OO syntax and CLR widening such as `:> obj` are refused.
- Clef admits no .NET `task`: `task { ... }` computation expressions are refused.
  A binding or field named `task` remains ordinary source.

## Components and integration

| Component | Responsibility |
| --- | --- |
| `Calque.Core` | Oak, trivia handling and source layout |
| `Calque.Syntax` | Pinned parser and syntax support |
| `Calque.Incremental` | Formatting requests coordinated through Fidelity.FSharp.Incremental |
| `Calque` | Command-line formatter |
| `Calque.Tests` | Preservation, idempotence, refusal and lifecycle tests |

`Calque.Incremental` binds each request to an immutable document revision and
formatting configuration. It shares work between consumers, supports
cancellation and rejects superseded results. Bozzetto uses this API to provide
formatting previews alongside Composer's incremental compilation sessions.
Applying a preview requires a current source revision; compiler diagnostics,
proofs and execution remain owned by the compiler workflow.

The coordinator currently parses and formats whole documents. Incremental
syntax and trivia reuse, syntax markup and automatic editor application are
future work; the current incremental API manages requests and their lifetimes.

## Build and use

Use the SDK pinned in `global.json`:

```sh
dotnet build Calque.slnx
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --help
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --check path/to/Program.clef
dotnet src/Calque/bin/Debug/net10.0/Calque.dll path/to/Program.clef
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --stdin < path/to/Program.clef
dotnet vstest src/Calque.Tests/bin/Debug/net10.0/Calque.Tests.dll
```

The CLI formats UTF-8 `.clef` files with two-space indentation. `--check` leaves
files unchanged. Exit codes: `0` for success, `1` for changes found by `--check`,
and `2` for diagnostics or I/O errors. Unsupported syntax is refused before writes.

## Documentation

- [Design, syntax support and integration](docs/design.md)
- [Provenance and bootstrap history](docs/history.md)
- [Contributing and validation](CONTRIBUTING.md)
- [Apache-2.0 license](LICENSE.md) and [upstream changelog](UPSTREAM_CHANGELOG.md)
