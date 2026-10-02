# Contributing to Calque

Start with [AGENTS.md](AGENTS.md), then build `Calque.slnx` and run the complete
focused test suite. Add a precision or refusal regression before changing syntax
handling. Preserve source-aware trivia; idempotence alone does not prove that a
formatting change preserves Clef syntax.

Keep upstream borrowing and future Clef syntax support explicit. No contribution
should reintroduce the removed RPC client, compiler service or runtime machinery.
