# Sprint 2026-05-15-9056ee — Roll/Event/Texting Unification

**Authorization:** Daniel — "Now plan the sprint and eigentakt all. Tickets in one go." (2026-05-15 22:34 UTC)

Standing-yes for the full drain per SOUL.md drain rule. Orchestrator acts on documented defaults; no per-sub-action approval requests.

## Theme

This sprint converges five threads that surfaced from staging session `ce5a6f82-efbe-45cb-9e6a-8415b056a8a5` (2026-05-15):

1. **Engine unification** — collapse four duplicated dice-check engines into one.
2. **UI unification** — one event-box family for every game event, with collapsed contracts and expandable diffs.
3. **Player-visible bugs** — meta-prefix leaks, texting-style contradictions, internal-issue-ref leak.
4. **Information visibility** — opponent defenses, ghost risk, weakness window, trap countdown.
5. **Mechanic clarity** — horniness ordering, tier naming, demotion explanation.

## Scope — 18 tickets

### pinder-core (8)

- #899  Horniness text overlay last (after trap AND shadow)
- #901  ★ RollEngine.ResolveCheck + named modifier bag + single tier ladder
- #902  ★[BUG] Meta-prefix strip per-stage (not just at parser)
- #903  Expose opponent_defense_snapshot on TurnStart
- #904  Disambiguate TropeTrap tier label per kind (display only)
- #905  Expose ghost_probability_per_turn on GameStateSnapshot
- #906  Expose roll.defending_stat on TurnResult
- #907  ★[BUG] TextingStyleAggregator conflict matrix

### pinder-web (10)

- #592  ★ Unified RollFormula (modifier-bag) + RollEventBox component family
- #593  Foldable Tell + Weakness Window hint banner
- #594  Opponent defense-stat block visible on conversation screen
- #595  [BUG] Strip "per #271" leak from NAT 20 explainer
- #596  Drop Annotations frame — combo/triple/tell/callback through unified widget
- #597  Single pipeline-ordered event stack; diffs inside expandable; trap-overlay-applied event
- #598  Collapsed event-box contract: severity + stat/subject + result (+ demotion marker)
- #599  Active-trap countdown via ActiveTrapBadge; ghost-risk banner + red frame at Bored
- #600  ★ EventBox header: wrapping subtitle + slot region; atoms library
- #601  Roll formula attacker → defender annotation

★ = foundational (others depend on it)

## Dependency graph

```
core
  901 (engine unification) ──┬─→ 904 (display label per kind)
                             └─→ 906 (defending_stat)
  903 (defense_snapshot)       — independent
  905 (ghost_probability)      — independent
  899, 902, 907                — independent

web
  600 (header + atoms) ──┐
  592 (RollEventBox)    ─┼─→ 596, 597, 598, 599 banner
                         │
  592 needs 901 (modifier bag on wire)
  594 needs 903
  599 ghost banner needs 905
  601 needs 906
  593 — independent of foundations
  595 — independent (pure copy fix)
```

## Sprint plan (rung-paced eigentakt)

### Rung 0 — Bugs, quick wins, all prerequisite wire fields (parallel-friendly, run sequential per eigentakt rules)

1. **#595** — Strip `per #271` from NAT 20 explainer. ≤30 min. (web, bug, copy fix)
2. **#902** — Meta-prefix strip per-stage. (core, bug, regression-test)
3. **#907** — TextingStyleAggregator conflict matrix. (core, bug)
4. **#899** — Horniness text overlay last. (core, ordering invariant flip)
5. **#905** — ghost_probability_per_turn on snapshot. (core, additive DTO)
6. **#903** — opponent_defense_snapshot on TurnStart. (core, additive DTO)

### Rung 1 — Foundations (must land before their consumers)

7. **#901** — RollEngine.ResolveCheck + modifier bag + tier ladder. (core, arch)
   - Unblocks: #904, #906, web#592
8. **#906** — roll.defending_stat. (core, additive on top of 901 if landed first; else additive on existing roll)
9. **#904** — TropeTrap display label per kind. (core, depends on 901's RollCheckKind)
10. **#600** — EventBox two-row header + atoms library. (web, no core dep)
11. **#592** — Unified RollFormula + RollEventBox. (web, consumes 600 atoms; consumes 901 wire shape via adapter)

### Rung 2 — UX surfaces on the unified foundation

12. **#601** — Roll formula stat-matchup annotation. (web, consumes 906)
13. **#596** — Drop Annotations frame; route through unified widget. (web, consumes 592/600)
14. **#598** — Collapsed contract: severity + stat + result + demotion marker. (web, consumes 592/600)
15. **#597** — Single pipeline-ordered event stack + trap-overlay event + diffs in expandable. (web, consumes 592/600/598)
16. **#594** — Opponent defense-stat block. (web, consumes 903)
17. **#599** — Trap countdown + ghost-risk banner. (web, consumes 905)
18. **#593** — Foldable Tell + Weakness Window hint banner. (web, consumes 600 layout)

### Sprint-end docs pass (mandatory per AGENTS.md)

Walk `docs/documentation-checklist.md` after #593 merges:

- Update `ARCHITECTURE.md` (engine unification, event-box family, wire field additions).
- Update `LESSONS_LEARNED.md` (sanitization invariants per LLM stage; conflict matrix for style aggregator; demotion UX requirement).
- Update `CHANGELOG.md` (release block, semver MINOR — many additive features + 3 bugs).
- Refresh `docs/persona/texting-style-aggregation.md` to document the conflict matrix.
- Refresh `docs/specs/` for new wire fields (`defending_stat`, `opponent_defense_snapshot`, `ghost_probability_per_turn`, optional `RollCheckResult`).

## Definition of Done per ticket

Per each ticket's own DoD. Plus the standard eigentakt DoD:

- ACCEPT_WORK orchestrator gate passes
- `## DoD Evidence` and `## Research Log` blocks on every PR
- session-runner snapshot regression confirmed clean (or intentional update documented)
- Tests added per each ticket's §Tests
- PR pushed; reviewer merges via eigentakt review agent, not orchestrator

## Operator-in-the-loop pause points

- **Visual review** for #594, #599 (red frame), #600 (slot recipes). Surface preview screenshots to channel; resume on Daniel's "go".
- **Wire DTO removals** are NOT in scope (cleanup phase deferred per #901 / #903 / #905 / #906 plans).

## Notes

- The two `arch-concern` web tickets (#592, #597, #598) and the two `arch-concern` core tickets (#901) are deliberately split into foundational vs consumer to keep PRs reviewable.
- Tier 0 model routing is loaded from `model-routing.yaml` (already current per HISTORY.md). No model bumps needed for this sprint.
- 4 bugs total (#595, #902, #907 core, #595 web) all in Rung 0 so they land fast for staging re-verification.
