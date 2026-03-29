# Contract: Issue #50 — Tells (§15)

## Component
Tell detection storage and +2 hidden roll bonus in `GameSession`

## Dependencies
- #139 Wave 0: `RollEngine.Resolve(externalBonus)` for flowing bonus into roll
- `Tell` class and `OpponentResponse.DetectedTell` already exist (merged PR #114)

---

## Data Flow

1. **Previous turn**: `OpponentResponse.DetectedTell` returns `Tell?` (stat + description)
2. **GameSession** stores `_activeTell` from previous turn's opponent response
3. **StartTurnAsync**: For each dialogue option, check if `option.Stat == _activeTell.Stat` → set `DialogueOption.HasTellBonus = true` (field already exists)
4. **ResolveTurnAsync**: If chosen option stat matches active tell, add +2 to `externalBonus` for `RollEngine.Resolve()`
5. **After resolution**: Clear `_activeTell` (one-turn only)
6. **Store new tell**: After this turn's opponent response, store any new `DetectedTell` for next turn
7. **TurnResult**: Set `TellReadBonus = 2` and `TellReadMessage = "📖 You read the moment. +2 bonus."` when tell was read

---

## Behavioral Invariants
- Tell bonus is **hidden** — displayed percentage does NOT include it
- Tell lasts exactly one turn
- +2 bonus flows through `externalBonus` (combined with callback + triple)
- `DialogueOption.HasTellBonus` is a preview for UI 📖 icon
- If player doesn't pick the tell stat, the +2 is not applied and the tell expires anyway
- Only one tell can be active at a time (latest from opponent response)
