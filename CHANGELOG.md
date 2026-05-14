# Changelog

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
