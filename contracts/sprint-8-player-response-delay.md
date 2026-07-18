# Contract: Issue #55 - PlayerResponseDelay - Retired

**Status:** Retired and absent from the current API as of 2026-07-18.

The Sprint 8 player-response-delay contract no longer describes implemented or expected behavior. `PlayerResponseDelayEvaluator`, `DelayPenalty`, and player slow-reply interest penalties are intentionally absent from `Pinder.Core`.

Current contract:

- Do not add or restore `src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs`.
- Do not add or restore `src/Pinder.Core/Conversation/DelayPenalty.cs`.
- Do not wire `GameSession`, `GameClock`, or persisted snapshots to compute player response delay penalties.
- Keep datee/NPC response timing (`TimingProfile`, character timing data) separate from the retired player-delay penalty subsystem.

Future slow-reply mechanics require a new contract and new acceptance criteria.
