# Calque design and integration

## Formatting scope

The first slice retains Oak, the source-aware trivia pipeline and the printer,
with a small API and CLI. The inherited parser currently accepts a guarded
subset of Clef syntax. Unsupported constructs are refused before file writes;
a dedicated Clef parser adapter remains required for broader language support.
For now, this is F# formatting with explicit Clef exclusions, rather than a
complete language validator. HelloArty and HelloWayland are working compatibility
examples over a developing language surface; an inherited-parser refusal marks
a formatter gap and does not make those programs invalid. Imperative bindings,
loops and record updates remain ordinary source, with their order preserved.

## Components and responsibilities

`Calque.Core` contains the Oak and formatting pipeline. `Calque.Syntax` retains
the pinned parser sources needed by this bootstrap; its resource generator is
build tooling. `Calque` supplies the CLI, and `Calque.Tests` exercises precision,
trivia, idempotence and refusal behavior.

Formatting produces source text. It does not infer program semantics, publish
PSG facts, discharge proofs or grant execution authority. A later CCS-owned
adapter can investigate hypergraph-friendly syntax construction while preserving
source identity and trivia.

## Syntax preservation

Clef source excludes nulls and OO patterns, including CLR widening through
`:> obj`. Calque refuses these forms rather than giving them a formatted Clef
appearance. Future JavaScript targeting may need widening, but the intended
direction is a principled source syntax with representation changes handled
downstream near the target. Target support alone does not relax these rules.

Clef likewise admits no .NET `task`. Calque refuses `task { ... }` computation
expressions, including inside quotations; a binding or field named `task`
remains ordinary source.

Quotations are first-class source: preserve typed `<@ ... @>`, untyped
`<@@ ... @@>` and nested quotation structure, comments and literal spelling.
Their compile-time interpretation belongs to Clef; formatting does not evaluate
them. The current quotation contract excludes `%expr` and `%%expr` splices;
ordinary infix `%` arithmetic remains supported.

The current Numeric Selection and Deferred Inference references place posit,
IEEE, fixed-point and quire representation decisions downstream of ordinary
source numeric kinds and dimensions. They do not establish additional posit or
quire source grammar for this bootstrap. Preserve source arithmetic and literal
spelling while leaving range inference, numeric commitment and accumulation
adequacy to their compiler owners.

## File writes

All requested files must format successfully before the first write.
File replacement preserves the UTF-8 BOM and Unix permission bits, checks for
edits made since preparation, and stages output beside the source before an
atomic replacement. Symbolic-link inputs are refused to preserve link identity.

## Incremental formatting

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

The foundation carries explicit `WorkCancellation` to each evaluator, withdraws
obsolete publication before cancellation, and joins evaluator and callback
cleanup before replacing the same work. The formatter checks that signal at
phase boundaries and within parser token delivery, Oak construction, trivia and
dialect walks, printing, output assembly and conditional-result merging. A stop
unwinds the current synchronous work at its next checkpoint. Every started
conditional branch still joins; an independent sibling fault takes precedence
over the operation's own stop. Parser control failures never become source
diagnostics. The checkpoint contract uses plain F# functions internally, with
no additional scheduler or ambient cancellation authority.

These are cooperative checkpoints, not preemption or a wall-clock latency
guarantee. A single lexer token, string operation or intervening helper still
runs to its next check. Source preparation and the initial parse are cold, and
releasing one consumer does not interrupt formatting still demanded by a peer.

This source-presentation path must proceed independently of Baker settlement,
proof dispatch and target compilation. Incomplete editing buffers still need
responsive syntax presentation when safe formatting is refused. The current API
parses and formats whole documents or prints caller-authored Oak trees;
incremental syntax/trivia reuse and markup results remain undelivered. Lattice's
development VS Code client now offers an explicit immutable preview through a
selected Bozzetto Composer session, including unsaved-buffer identity checks.
Saving/applying that preview and shared compiler overlays remain separate work.
Edit coalescing, incremental syntax reuse and edit-to-visible-result latency
budgets remain acceptance work: replacing shared work waits for older attempts
to drain. Tests exercise withdrawal inside the actual parser and printer, joined
replacement and peer demand; they establish ownership and checkpoint behavior,
not a design-time performance budget.

## Future presentation work

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
