# Documentation Checklist

Operator reference: which docs to update when X changes. Walk this list at
the end of every sprint or epic before declaring done.

---

## When `data/prompts/*.yaml` changes

- [ ] **`docs/ARCHITECTURE.md` §5 (Prompt catalog)** — update file list, entry
  counts, or role-affiliation notes if new files or sections are added.
- [ ] **`LESSONS_LEARNED.md`** — add an entry if a new failure mode was
  discovered during the change (e.g. word-soup, voice bleed, length violation).
  Do NOT duplicate existing lessons; check before adding.
- [ ] If a new YAML key is added to an existing file, confirm `PromptCatalog`
  and `PromptWiring.Wire()` validate the new key at startup (fail-fast must
  cover it).

**Relevant lessons:** `§PROMPT-BLOAT-FROM-CROSS-ROLE-SECTIONS` (#867),
`§PROMPT-ENFORCEMENT-PARITY` (#874/#869).

---

## When a new `GameDefinition` section is added

- [ ] **Role-affiliation decision** — decide at the moment of addition whether
  the section belongs to `BuildPlayer`, `BuildDatee`, or `Build()` (shared).
  Wire it exclusively to one path. Document the decision in a code comment if
  it is "shared" (the default assumption is role-specific).
  See `ARCHITECTURE.md §5 → Role-affiliation rule`.
- [ ] **ARCHITECTURE.md §5** — update the `BuildPlayer` / `BuildDatee` /
  `Build()` split table if the section materially changes prompt structure.
- [ ] **Regression test** — add a prompt-content test pinning that the new
  section appears in the correct builder output and is absent from the other.
  Reference: `Issue867_DeliveryTokenAuditSpecTests` as the pattern.
- [ ] **Parity audit** — check the symmetric prompt surface. If the new rule
  applies to player delivery, does datee delivery need it too (or vice
  versa)? See `§PROMPT-ENFORCEMENT-PARITY`.

---

## When `Pinder.RemoteAssets` API surface changes

- [ ] **`docs/modules/remote-assets.md`** — update method list and behaviour
  notes.
- [ ] **`docs/specs/character-asset-vocabulary.md`** — update wire-contract
  fields if new fields are added or renamed. Bump the `asset_kind` discriminator
  if a breaking schema change lands (`character/v1` → `character/v2`).
- [ ] **Security review trigger** — any new outbound HTTP call or new field
  accepted from the eigencore response requires a security review:
  (1) scheme enforcement still covered?  (2) response-size cap still
  appropriate? (3) new field validated before use?
  Reference: #859 (HTTPS enforcement), #860 (buffer-size cap).
- [ ] **`pinder-web/LESSONS_LEARNED.md §35`** — re-read the "Eigencore is a
  third-party app" invariant. Confirm the new surface does not leak
  Pinder-domain types into the wire contract.

---

## When the deploy pipeline changes (pinder-web)

- [ ] **`pinder-web/docs/ARCHITECTURE.md`** — update the deploy section and
  any affected service topology diagram.
- [ ] **`pinder-web/docs/deployment-and-staging.md`** (create if missing) —
  update the step-by-step deploy procedure and staging-stack notes.
- [ ] **`TOOLS.md` (agents-extra/pinder)** — update host/container/port
  references if any change.
- [ ] **Staging threat model** — if the deploy touches auth boundaries
  (OAuth clients, JWT validation, tailnet access), re-read the staging threat
  model in `TOOLS.md §Threat model & auth boundary`.

---

## When a new test failure mode is discovered

- [ ] Add an entry to `LESSONS_LEARNED.md` immediately (not deferred).
- [ ] If it is a **flake**, file a tracking issue (e.g. pinder-core#884 for
  the `Issue527` assembly-load flake) and note it in `LESSONS_LEARNED.md`.
- [ ] If it is a **pre-existing failure on main** (not introduced by the
  sprint), file a tracking issue (e.g. pinder-core#880 for 63 pre-existing
  `Pinder.LlmAdapters.Tests` failures) so it is not silently accepted as
  baseline.

---

## Sprint end — mandatory gates

Before any sprint PR is merged into main:

1. Walk every section of this checklist relevant to the sprint's changes.
2. Confirm `ARCHITECTURE.md` prompt-catalog section is accurate.
3. Append a release block to `CHANGELOG.md` with every merged PR and every
   deferred follow-up issue.
4. Confirm `LESSONS_LEARNED.md` captures all non-obvious findings.
5. Refresh this checklist if any procedure was ambiguous or missing.

These gates are not optional. The documentation pass is part of the sprint,
not an afterthought.
