# Spec: `weakness_dc_reduction` on `TurnStart` (pinder-core side of #593)

**Issue:** pinder-web#593 (FoldableHintBanner for Tell + Weakness Window)  
**Companion:** pinder-web PR implementing the UI component  
**Date:** 2026-05-16

---

## Problem

The frontend needed to display the DC-reduction magnitude (e.g. "DC -3") inside
a `FoldableHintBanner` without re-implementing the active-window state that the
engine already tracks as `_activeWeakness.DcReduction`.

Previously the frontend only knew *whether* a weakness window was open on a given
option (`has_weakness_window: bool`), but not *by how much* it reduced the DC.

---

## Solution

Add `int? WeaknessDcReduction` to `TurnStart`:

- **Non-null** when `_activeWeakness != null` at the moment `StartTurnAsync` builds
  the `TurnStart`; the value is `_activeWeakness.DcReduction`.
- **Null** when no window is open.

The field is a pre-resolution hint — it reflects the active window's magnitude
before any player pick or resolve occurs.

### Wire key

`weakness_dc_reduction` (snake_case, via `[JsonPropertyName]`).

---

## Files changed

| File | Change |
|------|--------|
| `src/Pinder.Core/Conversation/TurnStart.cs` | Added `int? WeaknessDcReduction` property + optional constructor param |
| `src/Pinder.Core/Conversation/GameSession.cs` | Compute `weaknessDcReduction = _activeWeakness?.DcReduction` and pass to `TurnStart` ctor |
| `session-runner/Snapshot/SessionSnapshot.cs` | Added `int? WeaknessDcReduction` to `TurnSnapshot`; updated `// Fields covered:` comment |
| `session-runner/Program.cs` | `BuildTurnSnapshot` accepts and populates `weaknessDcReduction`; call site passes `turnStart.WeaknessDcReduction` |
| `tests/.../Issue593_WeaknessDcReductionOnTurnStartTests.cs` | Unit + serialization tests |

---

## Constraints

- The `_activeWeakness` field is read-only at `StartTurnAsync` time and is
  cleared at the start of `ResolveTurnAsync`. `WeaknessDcReduction` is therefore
  always consistent with what the engine will apply if the player picks the window
  option this turn.
- **No breaking change** — `weaknessDcReduction` is a trailing optional parameter;
  all existing callers continue to compile unchanged.

---

## Testing

See `tests/Pinder.Core.Tests/Conversation/Issue593_WeaknessDcReductionOnTurnStartTests.cs`:

- AC1: `WeaknessDcReduction` is null when no window is open.
- AC2: `WeaknessDcReduction` equals `_activeWeakness.DcReduction` when a window is active.
- AC3: After a window is consumed by resolve, the next turn's start has null.
- AC4a: JSON contains `"weakness_dc_reduction":3` when active.
- AC4b: JSON contains `"weakness_dc_reduction":null` when not active.
