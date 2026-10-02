# Calque provenance and bootstrap history

## Name and origin

Calque's name comes from the French
[calque](https://www.collinsdictionary.com/dictionary/french-english/calque),
meaning a tracing or tracing paper: precise work on the shape of source text.

This checkout began at FidelityFramework/fantomas commit
`6e286d12a12f023face10866c9b205cb92f4ed01`. Fantomas is credited to its original
authors and contributors; see [LICENSE.md](../LICENSE.md) and
[UPSTREAM_CHANGELOG.md](../UPSTREAM_CHANGELOG.md). Vendored compiler sources retain
their own upstream notices.

The original CLI, RPC client, benchmarks, documentation site, broad F# regression
harness and upstream release automation have been removed from this working tree.
The Apache-2.0 license and upstream attribution remain. The parser acquisition helper
fetches only pinned source inputs and their upstream license/notice files.

## Repository history

Calque's canonical repository is
[FidelityFramework/Calque](https://forge.spkez.dev/FidelityFramework/Calque), on
`main`. Its Git history begins with one snapshot of this thin implementation;
Fantomas's inherited ancestry was intentionally omitted. The original local Git
metadata is archived outside this checkout. The GitHub fork is retained as the
`fantomas-fork` remote, with `fsprojects/fantomas` as `upstream`; their exact local
URL exceptions preserve access alongside the machine's Forgejo rewrite.
Retrieval registration is managed in `speakez-lab`.

## Bootstrap validation — 2026-10-02

Bootstrap acceptance on 2026-10-02 passed the full 71-case suite, including ten
quotation cases, and a packaged-CLI smoke check. Checks on copies of real source
accepted all three HelloArty files, 22 of 23 HelloWayland files and two Composer
dimensional-proof files; all 27 accepted files were idempotent. The remaining
HelloWayland file and two additional Composer samples exposed adapter gaps for
large unsuffixed integers and `eager`. All originals and refused copies stayed
unchanged. These are syntax-preservation checks; compiler execution remains a
separate acceptance boundary.
