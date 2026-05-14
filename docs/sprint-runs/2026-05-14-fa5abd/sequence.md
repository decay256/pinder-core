# Sprint 2026-05-14-fa5abd — execution order

Refiner pass added 4 child issues (#871 split → #872 #873 #874 #875)
and pinned design decisions on the design-q tickets. Final scope: 16
implementable tickets.

## Order

1. **#583** [pinder-web] GameApi stale yaml — fix Dockerfile + CI drift gate.
2. **#859** [pinder-core] RemoteAssets: enforce https.
3. **#860** [pinder-core] RemoteAssets: HttpClient buffer cap.
4. **#872** [pinder-core] Phase 2: PromptTemplates.cs → yaml.
5. **#874** [pinder-core] Phase 3: PromptBuilder structural strings → yaml.
6. **#873** [pinder-core] Phase 4: ArchetypeCatalog → yaml.
7. **#875** [pinder-core] Phase 5: cleanup + CI gate + pinder-web admin.
8. **#868** [pinder-core] Ship 15-stem stake prompt.
9. **#862** [pinder-core] Strip meta-prefixes from option intended_text.
10. **#863** [pinder-core] HARD RULE: preserve paragraph count.
11. **#864** [pinder-core] Horniness Catastrophe word-soup guard.
12. **#865** [pinder-core] Shadow Catastrophe length soft-guidance.
13. **#866** [pinder-core] Opponent response length cap.
14. **#867** [pinder-core] Delivery prompt token audit + section-strip.
15. **#869** [pinder-core] Opponent texting-style parity.
16. **#870** [pinder-core] Opponent voice-isolation guard.

## Rationale

- #583 is isolated (different repo) and has prod-ops urgency.
- #859/#860 are independent RemoteAssets-scope security fixes.
- yaml-migration epic (#872 → #875) lands BEFORE the prompt-tuning
  tickets so each tuning ticket edits yaml (small diff) instead of
  fighting the C# → yaml migration in rebases.
- #868 ships next because it's a pure yaml-data change once the
  migration is done.
- The remaining 8 prompt-tuning tickets run in ticket-number order;
  most touch `PromptTemplates.cs` or yaml-equivalents and will have
  ordinary rebase costs after the migration epic.

## Skip-classified

None — all 16 tickets are implementable post-refiner.

## Note on parallelism

Eigentakt is sequential. The refiner's "873/874 parallel ok" hint
is overridden by the skill's one-subagent-at-a-time invariant.
