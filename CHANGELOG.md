# Changelog

## [0.2.28] - 2026-08-02

- Add a canonical `GameSession.CreateResimulateData()` export matching the
  existing atomic `RestoreState` boundary for active-process continuation.
- Preserve semantic Pi session snapshots, histories, combo/callback state,
  progression events, psychological selection state, shadow state, and pending
  one-turn mechanics without persisting uncommitted options or dice pools.
- Restore each character's captured base and assembled system prompts as one
  validated pair so a process restart cannot regenerate private setup context.
- Validate restored state before adoption and retain the existing transactional
  turn boundary on provider failure or cancellation.

## [0.2.27] - 2026-08-02

- Run private DATEE emotional direction from a disposable `Pi.Agent.Core`
  session fork based on the pinned upstream repository/fork contracts at
  `583f153d502aa8e958eefdb9af0fbd3344e68f95`.
- Give the director the complete configured DATEE character system prompt and
  canonical typed conversation context without embedding a duplicate transcript
  in its source packet.
- Keep contract retries on the same immutable fork context, discard private
  analysis on success, failure, or cancellation, and commit only accepted
  player/DATEE visible messages to canonical session snapshots.
- Add catalog-owned session and system wrappers so all director prose remains
  editable and runtime-validated rather than hardcoded in orchestration code.

## [0.2.24] - 2026-07-26

- Added sanitized, phase-specific DATEE director and performance diagnostics
  with retry, latency, provider, model, and token-usage metadata.
- Kept private emotional direction, drafts, prompt content, provider bodies,
  and raw exceptions out of operational diagnostic payloads.
- Preserved token-usage reporting through standard transport decorators and
  included optional Anthropic improvement calls in usage totals.

## [0.2.23] - 2026-07-26

- Hardened #1344 DATEE retry/history isolation so successful turns commit only
  the delivered player message and parsed visible DATEE reply to session
  history. Adapter-returned private direction, prompt material, signal blocks,
  failed attempts, or duplicate entries are no longer trusted as canonical
  `GameSession` history.
- Preserved director/performance retry shape: director recovery remains scoped
  to the director call, while DATEE performance retries reuse the one validated
  direction and expose only the visible parsed response to stateful history
  callers.

## [0.2.22] - 2026-07-26

- Required generated emotional director JSON to self-identify with root
  `schema_version: "emotional_director.v1"`. The JSON schema now requires the
  exact version with a const, and the parser rejects missing, non-string, or
  mismatched payload versions with `invalid_schema_version` across native
  structured output and local JSON fallback paths.
- Updated the YAML-owned `emotional-reaction-director` prompt instruction and
  focused #1341/#1342/#1343 fixtures to use the eight-field versioned contract.

## [0.2.21] - 2026-07-26

- Wired the private emotional director into the production DATEE response
  operation. `PinderLlmAdapter.GetDateeResponseAsync(context, history, ct)` now
  requires `DateeContext.EmotionalTurnEvent`, runs
  `GenerateEmotionalDirectionAsync` exactly once, and reuses the validated
  seven-field direction across DATEE performance retries.
- Added the YAML-backed `emotional-reaction-performance-direction` prompt
  wrapper and trace attribution for all seven runtime director fields before the
  final `datee-response-instruction`. The public ordinary
  `SessionDocumentBuilder.BuildDateePrompt/BuildDateePromptEx` path remains
  unchanged for non-production/test prompt callers.
- Preserved DATEE visible history semantics: successful turns append only the
  delivered player message and accepted visible DATEE response; director failure
  or cancellation prevents the performance call.
- Hardened the private director boundary by encoding delivered/history transcript
  values as JSON string literals and rejecting leaked performance-direction
  headers or field labels with a sanitized, retryable contract violation before
  DATEE parsing or history construction.
- Made private direction leak matching case-insensitive and resilient to common
  leading Markdown, heading, and list decoration. Runtime prompt validation now
  fails closed if the editable performance template changes or removes its
  protected header or seven label-placeholder structural lines.
- Generalized line-leading leak normalization to Unicode punctuation, symbols,
  and whitespace, covering inline code, strikethrough, quotes, and parentheses
  while preserving numbered-list handling and strict label/header boundaries.

## [0.2.20] - 2026-07-26

- Added the private `emotional_director` LLM phase and internal
  `PinderLlmAdapter.GenerateEmotionalDirectionAsync` operation. The operation
  compiles #1340 input, uses structured output when available with plain JSON
  fallback, validates the seven-field direction contract (including emotion
  `intensity` and `underlying_feeling`), retries semantic rejections, propagates
  cancellation, preserves compiled source keys, and emits sanitized terminal
  diagnostics when retries are exhausted.
- Added `emotional-reaction-director` prompt catalog entry with
  temperature/max token settings. The director remains private and is not
  invoked from the current DATEE response path.

## [0.2.19] - 2026-07-26

- Added the canonical `RollOutcomeIntensity` contract and compact
  `DateeEmotionalTurnEvent` forwarding for private DATEE emotional reaction
  input compilation.
- Added `EmotionalReactionEventCompiler` plus traced YAML wrapper templates for
  relationship, delivered-message, history, event-meaning, and character
  formulation composition. The compiled artifact remains private and is not
  appended to the current DATEE performance prompt.

All notable changes to pinder-core are documented here.
Format: semver-ish. MAJOR = breaking changes. MINOR = new features. PATCH = fixes.

---

## [0.9.0] — 2026-05-14 (Sprint 2026-05-14-fa5abd)

### Summary

Prompt-catalog YAML migration (60+ const strings moved to `data/prompts/`),
prompt-quality hardening pass (7 tickets), and Pinder.RemoteAssets security
hardening. No breaking API changes; startup wiring now required via
`PromptWiring.Wire()`.

### Pinder.RemoteAssets — security hardening

- **#859** — HTTPS scheme enforcement on `Configuration.BaseUrl`. Constructor
  rejects non-HTTPS URLs unless `allowInsecureBaseUrl` is explicitly set.
- **#860** — `HttpClient.MaxResponseContentBufferSize` cap to prevent unbounded
  memory allocation on large responses.

### YAML migration epic (#871)

- **#872** — `PromptTemplates.cs` const strings → `data/prompts/templates.yaml`
  (37 entries). Dead const fallbacks removed.
- **#874** — `PromptBuilder` structural strings → `data/prompts/structural.yaml`
  (7 entries). Cross-assembly delegate pattern avoids circular dependency.
- **#873** — `ArchetypeCatalog._behaviors` → `data/prompts/archetypes.yaml`
  (20 archetypes).
- **#875** — Phase 5 cleanup: const fallbacks deleted, production wiring
  consolidated in `Pinder.SessionSetup.PromptWiring.Wire()`.

### Prompt-tuning and quality fixes

- **#868** — 15-stem stake prompt (locked in #826 comment).
- **#862** — Meta-prefix strip in `option.intended_text` (regex + prompt rule).
- **#863** — HARD RULE: preserve paragraph count in delivery rewrites.
- **#864** — Horniness Catastrophe word-soup guard (length floor + abstract-noun
  escape hatch).
- **#865** — Shadow Catastrophe length caps (audit pass, 6 stats).
- **#866** — Opponent response length cap: relative window + 600-char ceiling +
  warn-only post-LLM validation.
- **#867** — Delivery prompt token audit: stripped `OpponentFriction` +
  `OpponentCuriosity` from `BuildPlayer`; formalized role-affiliation rule.
- **#869** — Opponent texting-style parity: ported `WORD & PATTERN REPETITION`
  + self-check verify-then-rewrite from dialogue-options to opponent-response.
- **#870** — Opponent voice-isolation `CONTEXT BOUNDARY` guard in
  `opponent-response-instruction`.

### Infrastructure (pinder-web companion)

- **pinder-web#590** — `GameApi.Program.cs` calls `PromptWiring.Wire()` at
  startup; fail-fast on missing `data/prompts/`.
- **pinder-web#583** — `Dockerfile` `COPY`s `pinder-core/data/prompts/` into
  build artifact so the YAML files are present in the deployed container image.

### Tech-debt follow-ups filed (deferred)

- **pinder-web#585** — Workflow-scope PAT needed for data-drift CI gate.
- **pinder-web#588** — Admin frontend YAML editor for prompt YAMLs.
- **pinder-core#877** — XMLDoc for `allowInsecureBaseUrl` constructor parameter.
- **pinder-core#880** — 63 pre-existing test failures in `Pinder.LlmAdapters.Tests`
  on main (not introduced this sprint).
- **pinder-core#883** — Delete dead-code `ArchetypeYamlLoader.LoadFromYaml`.
- **pinder-core#884** — `Issue527` test flake (assembly-load interaction).
- **pinder-core#886** — Workflow-scope PAT needed for prompt-content grep gate.
