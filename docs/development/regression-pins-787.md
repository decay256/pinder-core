# Phase 0 Regression Pins — fast-gameplay refactor (#787)

> Live as of `pinder-core` `fix/787-regression-pinning`. Owner: backend-engineer
> drain (Wave 2 of #393). This document is normative for engineers touching
> `GameSession.ResolveTurnAsync` during Phases 1–5 of the fast-gameplay
> campaign — read before opening a PR that mutates the engine.

## Why this exists

Phase 0 of the fast-gameplay rollout (#393 / plan PR #422) ships before any
behaviour-changing refactor lands on the engine. Daniel: *"make enough
regression tests before."* The pins here are the externally observable contract
that Phases 1–4 (#788, #789, #424, #790) MUST preserve. If any Phase-1+ PR
breaks one of these, that's a regression: either narrow the change, or open a
discussion before merging.

The pins live in `tests/Pinder.Core.Tests/Phase0/`. They run by default under
`dotnet test`; CI-gating them is automatic via the existing pinder-core test
gate.

---

## I1 — Opponent conversation history (BEHAVIOR-BASED)

**File:** `Phase0_I1_OpponentHistoryContent.cs`

**What it locks:** the BYTES that cross the `ILlmTransport.SendAsync` wire for
the `opponent_response` phase preserve continuity across turns. Specifically,
opponent_response call N+1 contains the assistant content from call N in its
user message.

**What it deliberately doesn't lock:** the storage location of that history.
Today (pre-#788) the state lives in adapter-private fields. Phase 1 will
relocate it into `GameSession`. The test reads only what crosses the
transport boundary, never adapter internals — so the move can happen without
modifying the test.

**If this fails after Phase 1 ships:** the move broke wire-level continuity.
Don't update the test; fix the move.

## I2 — Static dice budget (`PlaybackDiceRoller.IsDrained`)

**File:** `Phase0_I2_DiceBudget.cs`

**What it locks:** the per-turn `IDiceRoller.Roll` count is a static function
of `(option choice, advantage flag, interest state, traps, shadow thresholds)`
— never of LLM output content. The fixture turn is run with a Lukewarm
interest, no advantage/disadvantage, no traps, no shadow ≥1 setup; under that
fixture the budget is exactly **3 draws per turn** (1 d10 ctor + 1 d20 main +
1 d100 timing).

**Enumerated draw sites in `ResolveTurnAsync` (and the `StartTurnAsync` /
ctor entry/exit boundaries it shares with):**

| # | Site | File | Line | Sides | Conditional |
|---|------|------|------|-------|-------------|
| 1 | Session horniness | `src/Pinder.Core/Conversation/GameSession.cs` | 165 (ctor) | 10 | Always |
| 2 | Ghost trigger | `src/Pinder.Core/Conversation/GameSession.cs` | 417 (`StartTurnAsync`) | 4 | iff `InterestState == Bored` |
| 3 | Madness T3 unhinged-option index | `src/Pinder.Core/Conversation/OptionFilterEngine.cs` | 107 | options.Length | iff Madness shadow ≥18 |
| 4 | Main d20 | `src/Pinder.Core/Rolls/RollEngine.cs` | 52 | 20 | Always |
| 5 | Advantage 2nd d20 | `src/Pinder.Core/Rolls/RollEngine.cs` | 53 | 20 | iff `hasAdvantage \|\| hasDisadvantage` |
| 6 | Opponent timing variance | `src/Pinder.Core/Conversation/TimingProfile.cs` | 53 | 100 | Always |

Steering / shadow / horniness rolls use a SEPARATE `Random` instance
(`SteeringEngine` / `HorninessEngine`) and do NOT consume `_dice`. They are
explicitly out of scope for the I2 budget.

**Architectural canary for Phase 2 (#789):** if a future PR adds a draw site
whose count depends on intermediate LLM output, the static budget breaks and
I2 fails. That's an architectural regression, not a test brittleness — surface
it to the orchestrator.

## I3 — Audit-log byte-determinism

**File:** `Phase0_I3_AuditDeterminism.cs`

**What it locks:** for a deterministic fixture (seeded steering RNG,
seeded stat-draw RNG, fixed dice queue, canned LLM responses), two runs of
the same single turn produce byte-identical `(phase, system_prompt,
user_message, response, temperature, max_tokens)` tuples. Verified directly
via tuple-equality and via SHA-256 signature.

**Why this isn't a snapshot file:** prompt-builder copy may legitimately drift
under unrelated PRs. Two-run equivalence locks the engine's purity — it's a
function of `(dice, transport responses, profiles, turn input)` alone — without
freezing prompt strings.

## I4 — State-mutation order

**File:** `Phase0_I4_MutationOrder.cs`

**What it locks:** the canonical `TurnProgressStage` sequence emitted via
`IProgress<TurnProgressEvent>` from `ResolveTurnAsync` AND the canonical
`LlmPhase` order at the transport. On the happy-path fixture the sequence is:

```
SteeringStarted → SteeringCompleted →
DeliveryStarted → DeliveryCompleted →
OpponentResponseStarted → OpponentResponseCompleted
```

with `LlmPhase.DialogueOptions ≺ LlmPhase.Delivery ≺ LlmPhase.OpponentResponse`
at the transport. (Horniness / shadow / trap-overlay phases appear conditionally
and are not in the happy-path fixture.)

**Sentinel-style observation:** the per-mutator order on private state isn't
externally observable, but the externally observable signals (progress stages,
transport phase order) are tight enough to catch any reorder that changes
behaviour.

## I5 — Snapshot equivalence

**File:** `Phase0_I5_SnapshotEquivalence.cs`

**What it locks:** a session played turn-by-turn to turn N produces the same
public-state surface (`GameStateSnapshot`: interest, momentum streak, active
traps, turn number, triple-bonus pending) as a session played to turn M (M&lt;N),
snapshot-restored via `RestoreState(ResimulateData, ITrapRegistry)`, and continued
to turn N. The snapshot/restore is a pure equivalence on the public surface.

**Why this isn't a hash assertion of all state:** Phase 1 (#788) will refactor
internal-state layout (move opponent history into `GameSession`). The public
`GameStateSnapshot` surface is what consumers (session-runner, replay tool)
actually use; that's what we lock.

## I6 — Cancellation discipline

**File:** `Phase0_I6_Cancellation.cs`

**What it locks:** when an `ILlmTransport.SendAsync` call throws mid-resolve
(simulated `OperationCanceledException`, simulated 429-style HTTP exception,
simulated network reset), the exception propagates AND the engine's turn
counter does NOT advance. `StartTurnAsync` failures don't leak `_currentOptions`.

**Documented gap:** pinder-core's non-streaming path has NO `CancellationToken`
plumbing on `ResolveTurnAsync` or `ILlmTransport.SendAsync`. Cancellation is
effected today by throwing from inside the transport — a `CancellationToken`
would be an engine-level API change. F3 (cancellation mid-stream) is therefore
tested via "throw OCE during opponent_response phase," which is the closest
fixture pinder-core can support.

**Existing engine behaviour finding (flagged here, NOT fixed in this PR):**
when `ResolveTurnAsync` throws between the dice roll and the delivery LLM call,
state mutations from earlier in the resolve pipeline (interest delta application,
momentum update, combo recording, XP, shadow growth) HAVE already been applied
to the session. The session is observably mutated post-throw. The engine has no
"rollback on transport failure" semantics today. `F4` therefore asserts the
conservative invariant — exception propagates, turn-number does not advance —
rather than asserting full state non-corruption. If full rollback semantics are
desired, that's a separate cancellation-rollback issue (file alongside review
of this PR).

---

## F1–F4 — Failure-mode integration tests

**File:** `Phase0_F_FailureModes.cs`

| # | Failure | Throws on phase | Exception |
|---|---------|-----------------|-----------|
| F1 | Rate-limit (HTTP 429) hit during a turn | opponent_response | shim-`HttpRequestException` |
| F2 | Network blip during opponent reply | opponent_response | `SocketException` |
| F3 | Cancellation token fires mid-stream | opponent_response | `OperationCanceledException` |
| F4 | Disk full during audit write | delivery | `IOException` |

**Scope boundary:** F1–F4 in the original spec talk about "no half-written
turn_records row." pinder-core has no postgres / `turn_records` concept (those
live in pinder-web's session-runner consumer). The pinder-core invariant that
maps to "no half-written audit row" is "the engine fails cleanly without
leaving the session in a corrupted state": exception propagates, turn counter
does not advance, session is still usable. The postgres-rollback half of the
invariant is the consumer's responsibility — not in pinder-core's testable
scope.

---

## How to extend these pins

1. New invariant: add a `Phase0_I*` test class. Use `[Trait("Category", "Phase0")]`.
2. Reuse `RecordingLlmTransport`, `PlaybackDiceRoller`, `Phase0Fixtures`. Don't
   duplicate fixture infra.
3. Document the new invariant in this file under a new `## I*` heading.
4. If the pin reveals existing engine misbehaviour, **flag in the PR body**;
   do not fix in the same PR (Phase 0 is tests-only).
