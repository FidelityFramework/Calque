# Calque

Calque is a small formatting project for Clef, derived from
[Fantomas](https://github.com/fsprojects/fantomas). Its name comes from the French
[calque](https://www.collinsdictionary.com/dictionary/french-english/calque),
meaning a tracing or tracing paper: precise work on the shape of source text.

The first slice retains Oak, the source-aware trivia pipeline and the printer,
with a small API and CLI. The inherited parser currently accepts a guarded
subset of Clef syntax. Unsupported constructs are refused before file writes;
a dedicated Clef parser adapter remains required for broader language support.
For now, this is F# formatting with explicit Clef exclusions, rather than a
complete language validator. HelloArty and HelloWayland are working compatibility
examples over a developing language surface; an inherited-parser refusal marks
a formatter gap and does not make those programs invalid. Imperative bindings,
loops and record updates remain ordinary source, with their order preserved.

## Use

Build with the stable .NET 10 SDK pinned in `global.json`:

```sh
dotnet build Calque.slnx
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --help
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --check path/to/Program.clef
dotnet src/Calque/bin/Debug/net10.0/Calque.dll path/to/Program.clef
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --stdin < path/to/Program.clef
dotnet vstest src/Calque.Tests/bin/Debug/net10.0/Calque.Tests.dll
```

Calque formats explicit UTF-8 `.clef` files with two-space indentation. `--check`
leaves source untouched. Exit codes are 0 for success or clean input, 1 when
check mode finds a formatting change, and 2 for a diagnostic or I/O error.
All requested files must format successfully before the first write.
File replacement preserves the UTF-8 BOM and Unix permission bits, checks for
edits made since preparation, and stages output beside the source before an
atomic replacement. Symbolic-link inputs are refused to preserve link identity.

Bootstrap acceptance on 2026-10-02 passed the full 71-case suite, including ten
quotation cases, and a packaged-CLI smoke check. Checks on copies of real source
accepted all three HelloArty files, 22 of 23 HelloWayland files and two Composer
dimensional-proof files; all 27 accepted files were idempotent. The remaining
HelloWayland file and two additional Composer samples exposed adapter gaps for
large unsuffixed integers and `eager`. All originals and refused copies stayed
unchanged. These are syntax-preservation checks; compiler execution remains a
separate acceptance boundary.

## Boundaries

`Calque.Core` contains the Oak and formatting pipeline. `Calque.Syntax` retains
the pinned parser sources needed by this bootstrap; its resource generator is
build tooling. `Calque` supplies the CLI, and `Calque.Tests` exercises precision,
trivia, idempotence and refusal behavior.

Formatting produces source text. It does not infer program semantics, publish
PSG facts, discharge proofs or grant execution authority. A later CCS-owned
adapter can investigate hypergraph-friendly syntax construction while preserving
source identity and trivia.

Clef source excludes nulls and OO patterns, including CLR widening through
`:> obj`. Calque refuses these forms rather than giving them a formatted Clef
appearance. Future JavaScript targeting may need widening, but the intended
direction is a principled source syntax with representation changes handled
downstream near the target. Target support alone does not relax these rules.

Quotations are first-class source: preserve typed `<@ ... @>`, untyped
`<@@ ... @@>` and nested quotation structure, comments and literal spelling.
Their compile-time interpretation belongs to Clef; formatting does not evaluate
them. The current quotation contract excludes `%expr` and `%%expr` splices;
ordinary infix `%` arithmetic remains supported.

`Calque.Incremental` provides a .NET-hosted formatting coordinator through the
reviewed Fidelity.FSharp.Incremental.Hosting preview.6 package, usable directly
or through Atelier and Bozzetto. Calque and the compiler/provider consume the
same reviewed preview.6 package input; each retains its own source or compiler
authority. Each document incarnation has one explicitly
started host. Requests retain immutable source, revision and configuration
snapshots; reusing their labels for different input is refused. Shared demand,
reservation, cancellation and draining use the foundation. Command admission and
outstanding demand are bounded, and a new snapshot fences older previews.
Parsing, trivia and layout remain Calque's work; no preview writes source or
grants compiler, edit or execution authority.

`DocumentFormatter.create` is cold, `start` explicitly starts coordination, and
`request` retains the admitted operation. `observe` is a cold, repeatable wait;
cancelling one observer leaves shared demand intact. `release` removes that
consumer's demand, while `cancelCurrent` immediately fences presentation and
requests withdrawal. `beginClose` seals admission; `awaitClose` joins actual
owned work, including conditional parsing and printing branches. Owners drain
typed host diagnostics even for withdrawn attempts. A result may be presented
only for its current identity, with a further editor revision check before
applying formatted text.

This source-presentation path must proceed independently of Baker settlement,
proof dispatch and target compilation. Incomplete editing buffers still need
responsive syntax presentation when safe formatting is refused. The current API
parses and formats whole documents or prints caller-authored Oak trees;
incremental syntax/trivia reuse, markup results and editor integration remain
undelivered. Edit coalescing, cooperative interruption of synchronous parsing and
edit-to-visible-result latency budgets remain acceptance work: replacing shared
work waits for older attempts to drain. Protocol lifecycle tests establish
ownership and freshness, not a design-time performance budget.

The current Numeric Selection and Deferred Inference references place posit,
IEEE, fixed-point and quire representation decisions downstream of ordinary
source numeric kinds and dimensions. They do not establish additional posit or
quire source grammar for this bootstrap. Preserve source arithmetic and literal
spelling while leaving range inference, numeric commitment and accumulation
adequacy to their compiler owners.

As Atelier grows, Calque may provide a common formatting interface with separate
adapters for MLIR, LLVM IR, proof text such as Alethe, and other edited formats.
Each adapter would own its syntax preservation and presentation policy, including
Fidelity markup. Those adapters and CCS hypergraph construction are later-horizon
work; this bootstrap provides the small Clef formatting surface.

Tier-2 proof dispatch, including CVC5, may later contribute richer layout needs.
Atelier should offer demanded source, obligation, proof and hypergraph views side
by side, linked by source-owned identities and exact revision provenance. Proof
details need not be inserted into code. Calque can supply text layout and position
mapping; CCS owns semantic correspondence, Composer owns proof acceptance, and
Bozzetto coordinates hosted workspace demand and work lifetime. Formatting or
displaying a result never renews its authority for a changed source revision.
These are presentation design notes, not delivered solver, editor or graph
integration.
Annotation economy is a design constraint: ordinary source stays readable,
generated obligations and solver detail are inspected on demand, and authored
constraints have deliberate controls instead of requiring pervasive annotations.
The existing Lattice/VS Code Clef Proofs tree is the presentation starting point:
compact source-obligation counts near code, with statements, anchors, premises
and solver queries available in an expandable view. Future Atelier panels should
build on that separation rather than requiring proof details in the source buffer.
A proof-entry hover may show a bounded raw Alethe preview, with an action to open
the full proof alongside source. The preview retains the obligation identity and
revision, and is presented on demand.

The original CLI, RPC client, benchmarks, documentation site, broad F# regression
harness and upstream release automation have been removed from this working tree.
The Apache-2.0 license and upstream attribution remain. The parser acquisition helper
fetches only pinned source inputs and their upstream license/notice files.

## Provenance

This checkout began at FidelityFramework/fantomas commit
`6e286d12a12f023face10866c9b205cb92f4ed01`. Fantomas is credited to its original
authors and contributors; see [LICENSE.md](LICENSE.md) and
[UPSTREAM_CHANGELOG.md](UPSTREAM_CHANGELOG.md). Vendored compiler sources retain
their own upstream notices.

Calque's canonical repository is
[FidelityFramework/Calque](https://forge.spkez.dev/FidelityFramework/Calque), on
`main`. Its Git history begins with one snapshot of this thin implementation;
Fantomas's inherited ancestry was intentionally omitted. The original local Git
metadata is archived outside this checkout. The GitHub fork is retained as the
`fantomas-fork` remote, with `fsprojects/fantomas` as `upstream`; their exact local
URL exceptions preserve access alongside the machine's Forgejo rewrite.
Retrieval registration is managed in `speakez-lab`.
