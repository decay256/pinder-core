# Integration Tests

## FullConversationIntegrationTest

End-to-end integration test running a full 8-turn `GameSession` conversation.
Verifies the complete rules stack fires correctly with deterministic dice rolls.

### What it covers

| Turn | Action | Stat | Outcome | Mechanics Verified |
|------|--------|------|---------|--------------------|
| 1 | Speak | Wit | Success | Interest delta, Hard risk tier bonus, momentum start |
| 2 | Speak | Charm | Success | The Setup combo (Wit→Charm), combo interest bonus |
| 3 | Speak | Honesty | Misfire | Failure scaling, momentum reset on fail |
| 4 | Speak | SA | Success | The Recovery combo (fail→SA), Bold risk tier bonus |
| 5 | Speak | Chaos | TropeTrap | Trap activation on miss 6–9, "unhinged" trap |
| 6 | Recover | SA | Success | SA vs DC 12, trap clearing, recovery XP |
| 7 | Speak | Charm | Success | The Triple combo, triple bonus queued |
| 8 | Speak | Charm | Nat 20 | Nat20 → +4, DateSecured outcome, external bonus from Triple |

### Cumulative assertions

- **XP**: 10 + 15 + 2 + 15 + 2 + 15 + 15 + 75 = 149
- **Shadow growth**: Denial +1 (no Honesty success), Fixation −1 (4+ distinct stats)
- **Final interest**: 25 (DateSecured)
- **Game outcome**: `DateSecured`

### Test doubles

- **FixedDice** — deterministic dice queue, throws if exhausted
- **TestTrapRegistry** — returns "unhinged" trap for Chaos, null for others
- **ScriptedLlmAdapter** — returns pre-scripted dialogue options per turn, no API calls

### Running

```bash
dotnet test tests/Pinder.Core.Tests/ --filter "FullConversationIntegrationTest"
```
