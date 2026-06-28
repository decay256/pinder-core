# Content Authoring Style Guide

## Character Backstory Migration

As of Issue #1259, character definitions are being migrated to include a structured `backstory_categories` section. This ensures LLM system prompts can selectively surface biographical anchors rather than dumping a massive monolithic bio block.

### Biographical-Anchor Rules
1. **Anchors must be specific, not generic.** (e.g., "Left my favourite jacket on the N train in 2019" rather than "I am forgetful".)
2. **Anchors must be emotionally resonant.** They must tie into the character's Shadow stats (e.g., Fixation, Despair, Horniness).
3. **No more than 3 anchors should be injected per conversation.** The game engine will selectively rotate them.

### The 20 Category Keys
Every migrated character JSON must have exactly these 20 keys in the `backstory_categories` dictionary:
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
