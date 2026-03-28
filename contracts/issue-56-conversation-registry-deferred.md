# Contract: Issue #56 — ConversationRegistry (DEFERRED)

## Status: **DEFERRED TO NEXT SPRINT** per ADR-5

## Rationale
- Most complex piece in the sprint (multi-session, cross-chat shadow bleed, fast-forward)
- Deepest dependency chain (needs GameClock, SessionShadowTracker, all session features)
- Single-session gameplay is sufficient at prototype maturity
- All other 17 issues provide full value without ConversationRegistry
- Reduces sprint risk significantly

## What Would Be Needed (for future sprint planning)
- `IGameClock` injectable clock (from #54)
- `GameSession` fully featured (all other issues complete)
- `SessionShadowTracker` with cross-session persistence API
- Energy system ownership resolution (VC-75)

## Recommendation
Implement in a dedicated sprint after all single-session features are stable and tested.
