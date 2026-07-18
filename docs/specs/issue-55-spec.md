# Spec: PlayerResponseDelay - Retired

**Status:** Retired and absent from the current API as of 2026-07-18.

Issue #55 previously proposed a `PlayerResponseDelayEvaluator` and `DelayPenalty` subsystem that would penalize the player for slow replies. That subsystem is not part of the current engine contract and must not be reintroduced as a compatibility fallback.

Current state:

- `Pinder.Core.Conversation.PlayerResponseDelayEvaluator` does not exist.
- `Pinder.Core.Conversation.DelayPenalty` does not exist.
- `GameSession` does not compute elapsed real-world reply delays or apply slow-reply interest penalties.
- `IGameClock`/`GameClock` are used for current game-time mechanics such as time-of-day horniness context, not for player-response-delay penalties.
- `TimingProfile` remains a datee/NPC response presentation configuration owned by character/session setup; it is not a player-delay penalty API.

Any future work that wants slow-reply mechanics must start from a new current contract instead of reviving this retired spec.
