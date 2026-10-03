# Calque

Calque is a focused Clef formatting fork of Fantomas. Preserve its Apache-2.0
license, attribution and the pinned parser's upstream notices. The owner chose
one Calque root commit; retain upstream identities as provenance rather than
importing Fantomas's Git ancestry.

## Scope

- Keep the Oak, trivia and printing pipeline small and source-aware.
- Formatting must preserve syntax, comments, literal spelling and source order.
- Quotations are core source forms: preserve typed/raw delimiters, nesting and
  quoted bodies without evaluation. Reject quotation splices according to the
  current Clef contract; ordinary infix `%` arithmetic remains valid.
- Unsupported Clef syntax produces a diagnostic before any source write.
- Do not translate Clef to F# text or claim the inherited parser validates all Clef.
- Current Clef excludes nulls, CLR widening and OO syntax. Imperative bindings,
  loops and record updates remain valid; do not impose functional style.
- Clef admits no .NET `task`. Refuse `task { ... }` computation expressions;
  do not refuse an ordinary binding or field named `task`.
- Treat HelloArty and HelloWayland as working compatibility evidence. Parser
  refusals can mark adapter gaps. Check Composer PRDs and the Clef spec before
  declaring a source form invalid, including compile-time member constraints.
- Preserve numeric literal spelling; never add widths, suffixes or conversions
  to satisfy the inherited parser.
- Keep semantic PSG, compiler scheduling, proof and execution out of this project.
  Hypergraph-friendly AST construction belongs to a later CCS-owned adapter.
- Calque.Incremental owns demanded source formatting through the pinned shared
  foundation during .NET hosting. Preserve exact buffer/configuration identity,
  cold construction, independent observers, stale-result refusal and joined close.
  Keep its full-document parser contract explicit; syntax reuse and markup remain
  separate work. Preserve and drain diagnostics from withdrawn attempts.
- Use two spaces for new F# and project configuration; default output uses two.
- Centralize package versions in `Directory.Packages.props`.
- Use stable .NET 10; never restore .NET 11 or the upstream RPC/daemon product.

## Validation

On the shared development machine, take Bozzetto's `acquire_full_build_lease`
before `dotnet build Calque.slnx`, and `acquire_test_suite_lease` before the
unfiltered `dotnet vstest src/Calque.Tests/bin/Debug/net10.0/Calque.Tests.dll`.
Release each exact granted id through `release_work_lease` when its process ends,
including failures. Honor wait/refused decisions. No FSI session or daemon restart
is part of the loop. The test suite must execute all its registered cases.

The bootstrap parser is acquired by `scripts/acquire-syntax`; preserve its pinned
identity and bounded downloads. Build outputs and `.deps` are disposable. Do not
weaken refusal controls to obtain a passing format result.
