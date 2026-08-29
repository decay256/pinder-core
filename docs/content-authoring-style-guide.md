# Content Authoring Style Guide

## Character Backstory Migration

Character definitions define a structured `backstory_categories` section containing 20 canonical backstory reality facts (`tragic_reality`). 

This architecture allows the engine and `EmotionStemSelector` to dynamically select and inject specific biographical quirks turn-by-turn across the 4 Dramatic Arc Phases (Setup -> Escalation -> Turning Point -> Resolution) rather than dumping a static 40-entry backstory table or obsolete static lies into the base system prompt.

### Biographical-Anchor Rules
1. **Anchors must be specific, not generic.** (e.g., "Left my favourite jacket on the N train in 2019" rather than "I am forgetful".)
2. **Anchors must be emotionally resonant.** They must tie into the character's Shadow stats and neuroses (e.g., Fixation, Despair, Horniness).
3. **Dynamic runtime injection.** The game engine selectively injects relevant backstory facts at runtime according to the active dramatic phase and transition manner.

### The 20 Category Keys
Every character JSON defines authoritative reality facts across these 20 keys in the `backstory_categories` dictionary:
1. `childhood_memory`
2. `core_wound`
3. `proudest_moment`
4. `greatest_fear`
5. `hidden_talent`
6. `embarrassing_secret`
7. `relationship_with_authority`
8. `stress_response`
9. `comfort_habit`
10. `attitude_towards_money`
11. `romantic_ideal`
12. `dealbreaker`
13. `physical_insecurity`
14. `defining_loss`
15. `pet_peeve`
16. `weird_obsession`
17. `guilty_pleasure`
18. `recurring_dream`
19. `reaction_to_failure`
20. `view_on_mortality`

## Texting-Style Fragments

Texting style is a soft delivery layer. It may shape visible wording and
cadence, but it must not override the character's psychology, emotional state,
game state, conversational meaning, or need for variation.

New fragments use exactly these canonical axes:

```text
SYNTAX: emoji, shorthand, grammar, structure, length, tics
EXPRESSION: directness, affect, rhythm
```

Use `EXPRESSION:` for new content. `TONE:` and the old `stance`, `register`,
and `pacing` names are compatibility aliases for existing data, not authoring
vocabulary.

Write each candidate as one concise tendency. Prefer phrasing such as `may`,
`tends to`, `usually`, and `is comfortable with`. Do not make every selected
trait mandatory on every message. A strict absolute is allowed only for a
deliberate signature quirk; label it `strict` and keep it narrow enough that it
cannot distort the message's intent.

An axis can contain an indented pool of candidates. Core deterministically
selects one candidate per source and axis from the character seed, source
identity, axis, and ordered pool. It does not concatenate the candidates.

Before authoring, check which axis the source can affect:

- Items: `Special -> emoji`, `Head -> shorthand`, `Body -> grammar`,
  `Hair -> structure`, `Arms -> length`, `Face -> tics`.
- Anatomy: trunk parameters vote on `directness`; skin, freckle, smoothness,
  and vein parameters vote on `affect`; glans, scrotum, testicle, and
  circumcision parameters vote on `rhythm`.

See [the pool](persona/texting-style-pool.md) for examples and
[the aggregation contract](persona/texting-style-aggregation.md) for exact
parameter mappings, deterministic selection, voting, attribution, and conflict
resolution.
