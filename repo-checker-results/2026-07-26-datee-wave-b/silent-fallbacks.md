> Scope: current modified and untracked files for pinder-core #1338/#1339 and dependent pinder-web changes (excluding repo-checker-results).

### Finding 1: Diagnosis generation silently discards schema-invalid fields
**File**: `tests/Pinder.Core.Tests/Issue1253_SequentialSynthesisTests.cs:123`
**Issue**: The changed test sends a diagnosis containing `"extra_note": "ignored"` and explicitly requires `Assert.DoesNotContain("extra_note", result.Keys)`. The production normalization path therefore accepts an LLM object that violates the prompt's "exactly these string keys" contract and silently projects it onto `TherapistDiagnosisContract.RequiredFields` instead of rejecting and retrying it.
**Impact**: Prompt drift and model hallucinations are hidden as successful generation. A structurally invalid diagnosis can be persisted without any diagnostic signal, so operators cannot distinguish exact-contract output from repaired output and future mistakenly named fields can disappear silently.
**Urgency**: U1 - topic default; this is a production generation boundary that fully suppresses a contract violation.
**Fixer-Agent Action Plan**: Make diagnosis validation reject every generated key outside the canonical required-field set after case/whitespace normalization, route that rejection through `SemanticOutputRecoveryExecutor`, and replace this acceptance test with retry/final-failure coverage that asserts no patch occurs.

### Finding 2: Runtime validation accepts malformed emotional-reaction prompt bodies
**File**: `src/Pinder.LlmAdapters/EmotionalReactionPromptCatalog.cs:102`
**Issue**: `ValidateRuntimeCatalog` only calls `RequireSystemPrompt` for interest and event entries, which checks that `system_prompt` is nonblank. The changed tests define stronger runtime-relevant constraints at `Issue1339_EmotionalReactionPromptCatalogTests.cs:204-224` (actionable prose, no enum/numeric-band language, and no final-reply drafting), but those checks run only against the bundled fixture. An admin-edited value such as `"respond with hello"` or `"Charm 10-15"` remains nonblank and passes production reload validation.
**Impact**: Malformed live prompt configuration is published without an error and silently changes DATEE behavior from internal reaction direction to leaked game mechanics or prewritten replies.
**Urgency**: U1 - topic default; malformed production prompt content is accepted during the startup/reload gate with no surfaced failure.
**Fixer-Agent Action Plan**: Move the enforceable prompt-shape checks into `EmotionalReactionPromptCatalog.ValidateRuntimeCatalog`, validate every interest/transition/event entry on each runtime binding build, and add mutation tests proving reload rejects short labels, enum/numeric-band notation, and final-reply instructions.

### Finding 3: Diagnosis prompt validation is not tied to the expanded eleven-field contract
**File**: `src/Pinder.LlmAdapters/PromptCatalog.cs:251`
**Issue**: Runtime validation treats `diagnosis` as a generic complete prompt and checks only `{backstory}` and `{stakes}` in its user template. It never verifies that the system prompt requests all eleven `TherapistDiagnosisContract.RequiredFields`. The changed synthesis test at `Issue1253_SequentialSynthesisTests.cs:120` demonstrates that an arbitrary `"SYSTEM PROMPT"` is accepted and sent to the LLM; an old two-field diagnosis prompt would likewise pass startup/reload validation.
**Impact**: A stale or partially edited diagnosis prompt becomes active successfully, then every diagnosis generation spends all three recovery attempts before failing. The configuration error is discovered only on a user-triggered synthesis path instead of atomically at configuration publication.
**Urgency**: U1 - topic default; the runtime configuration gate accepts a known-incompatible prompt and defers a deterministic production outage to request time.
**Fixer-Agent Action Plan**: Add a diagnosis-specific runtime contract validator derived from `TherapistDiagnosisContract.RequiredFields`, require every canonical JSON key and the new formulation guardrails before publishing a prompt binding, and add reload tests for an old two-key prompt and one omitted reaction field.

## Summary

Findings: 3 total (U1: 3, U2: 0, U3: 0).

No additional silent-fallback findings were found in scope: missing or blank emotional-reaction catalog entries fail runtime validation; missing or blank required diagnosis values exhaust recovery and fail without patching; loaded legacy string extras remain explicit data rather than fabricated defaults; and dependent `CharacterSynthesisService` coverage confirms diagnosis and bio are patched only after the full generation sequence succeeds.
