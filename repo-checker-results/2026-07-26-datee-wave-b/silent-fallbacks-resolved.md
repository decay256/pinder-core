> Resolution: current #1338/#1339 sprint changed files.

## Finding 1

No code change. Generated-output normalization intentionally selects the canonical
required diagnosis fields and discards model-supplied extras. This is the
established boundary contract: generation cannot persist unapproved keys, while
the loader separately preserves string extras already present on legacy
characters so they can reach regeneration. Focused tests explicitly lock both
behaviors.

## Finding 2

No code change. Runtime validation deliberately enforces structural prompt
contracts, not subjective prose classification. Actionability, game-mechanic
leakage, and reply-drafting constraints are checked against the authored catalog
by content regressions. Adding keyword heuristics to admin reload would create a
second, brittle prompt-policy implementation and reject valid editorial changes.

## Finding 3

Resolved. `PromptCatalog.ValidateRuntimeCatalog()` now derives diagnosis prompt
validation from `TherapistDiagnosisContract.RequiredFields` and rejects a prompt
that omits any canonical JSON key before publication. A mutation regression
removes `self_awareness_reaction` from a copied runtime catalog and verifies the
reload gate fails with an actionable diagnosis-key error.

Verification:

- `PromptCatalogValidationTests` plus
  `Issue1339_EmotionalReactionPromptCatalogTests`: 19 passed, 0 failed.
