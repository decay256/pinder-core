# Texting-Style Aggregation Rule (v1)

This is the canonical rule for aggregating a character's equipped items
and anatomy parameters into the **9-axis texting-style block** that ends
up in the LLM system prompt and in `CharacterProfile.TextingStyleFragment`.

It replaces the random-pick-2 placeholder shipped during the
[#834 / texting-style-pool] rework. See the parent ticket
[#836](https://github.com/decay256/pinder-core/issues/836) for the full
design space.

---

## The contract

The final aggregated style is **exactly 9 axes**:

- **6 syntax axes** (concrete, mechanical patterns): `emoji`, `shorthand`,
  `grammar`, `structure`, `length`, `tics`.
- **3 tone axes** (stance + register + pacing): `stance`, `register`,
  `pacing`.

Each axis is filled by **at most one rule** drawn from one or more
sources on the character. Two distinct sources never write to the same
axis.

The output is fully deterministic for a given (character_id, equipped
items, anatomy tiers). Two runs against the same configuration produce
the same 9-line aggregate, byte-exact. Players never see this aggregate
directly — they discover it indirectly through the LLM's behaviour.

---

## Item slots own syntax (1:1 fixed mapping)

Each of the 6 item slots owns exactly one syntax subcategory:

| slot       | syntax axis |
|------------|-------------|
| `shoes`    | `emoji`     |
| `hat`      | `shorthand` |
| `shirt`    | `grammar`   |
| `trousers` | `structure` |
| `frame`    | `length`    |
| `accessory`| `tics`      |

If a slot is empty (no item equipped), the axis is **silenced** for that
character — not back-filled from elsewhere. The block emits 9 axis
slots maximum; an unequipped slot drops its axis from the final
fragment list.

The line written to the syntax axis is read from the equipped item's
`texting_style_fragment` block. The block has the canonical shape:

```
SYNTAX:
- emoji: <one line from the syntax→emoji pool>
- shorthand: <one line from the syntax→shorthand pool>
- grammar: <one line from the syntax→grammar pool>
- structure: <one line from the syntax→structure pool>
- length: <one line from the syntax→length pool>
- tics: <one line from the syntax→tics pool>
TONE:
- stance (<key>): <one stance line>
- register (<key>): <one register line>
- pacing (<key>): <one pacing line>
```

The aggregator reads the slot's owned axis from its item — so if the
item in the `shoes` slot has `- emoji: ends every sentence with an emoji
that conveys its emotion`, that's the line written to the final
fragment's `emoji` axis. Items in the `shoes` slot do NOT contribute to
any other axis: only the slot's owned axis is read.

This is the design rule that makes the system **discoverable by
gameplay**: swap one item, see exactly one syntax axis change. Players
cannot fight back across the boundary.

---

## Anatomy parameters lock tone (3:1 group-vote mapping)

The 9 anatomy parameters are partitioned into 3 groups of 3. Each
group decides one tone axis:

| anatomy params                                       | tone axis  |
|------------------------------------------------------|------------|
| `length`, `girth`, `circumcision`                    | `stance`   |
| `vein_definition`, `skin_texture`, `skin_tone`       | `register` |
| `ball_size`, `tattoos`, `eye_style`                  | `pacing`   |

For each group, the aggregator extracts the tone-axis line from each
selected tier in the group. The decision rule is:

1. **Drop empty contributions.** If a tier has no `texting_style_fragment`
   or its TONE block doesn't carry the axis, that source contributes
   nothing.
2. **Majority wins.** Group the remaining lines by their text. The
   text that appears the most often wins.
3. **Tie-break by group order.** When two distinct lines have the same
   highest count, the line from the parameter that appears earliest in
   the group's parameter list wins. (`length` beats `girth` beats
   `circumcision` for stance, etc.)

If the entire group contributes nothing, the tone axis is silenced for
that character — the final fragment list emits 8 lines instead of 9.

Anatomy is **never** read for syntax. The full grouping is fixed and
documented here; designers can verify and operators can predict.

---

## Output format

The aggregator returns a list of strings, one per filled axis, in the
canonical order:

```
emoji: <line from shoes.SYNTAX.emoji>
shorthand: <line from hat.SYNTAX.shorthand>
grammar: <line from shirt.SYNTAX.grammar>
structure: <line from trousers.SYNTAX.structure>
length: <line from frame.SYNTAX.length>
tics: <line from accessory.SYNTAX.tics>
stance: <majority winner from {length, girth, circumcision}.TONE.stance>
register: <majority winner from {vein_definition, skin_texture, skin_tone}.TONE.register>
pacing: <majority winner from {ball_size, tattoos, eye_style}.TONE.pacing>
```

Axes whose source is empty are dropped, not emitted as
`emoji: (empty)` or similar. The downstream consumers
(`PromptBuilder`, `CharacterProfile.TextingStyleFragment`) emit each
remaining axis as its own bullet line in the system prompt's TEXTING
STYLE section.

When the character has no items and no anatomy contributions, the
output is an empty list — the section header may still be emitted but
carries no rules.

---

## Why this rule

1. **9 axes. Always.** No more, no less. 6 items × 1 axis per slot, 3
   anatomy groups × 1 axis per group. No risk of soup.
2. **Discoverable through gameplay.**
   - Layer 1: "swapping clothes never changes my stance" → anatomy
     decides tone. The player figures this out across multiple equipment
     swaps.
   - Layer 2: "swapping shoes changes my emoji rule" → items decide
     syntax, slot-by-slot. The 1:1 slot→axis mapping is the second-order
     discovery — players will notice that swapping the same slot
     consistently changes the same axis.
3. **Not directly controllable.** Players never see the prompt, can't
   say "give me dry stance". The path from intent → result goes through
   anatomy + items, which are physical decisions, not text knobs.
4. **Surfaces both items and anatomy.** Anatomy is back in the wire —
   the previous placeholder silenced anatomy entirely, which broke the
   discoverability layer. Now anatomy is the only path to tone.
5. **Deterministic.** Per-character; per-configuration. No re-roll mid
   conversation. Build-craft is preserved: the player's choices stick.
6. **Auditable.** Every axis has exactly one source. A reviewer reading
   the assembled prompt can trace each line back to a specific slot or
   anatomy parameter without RNG.

---

## What changed from the placeholder

- **Random-pick-2 is gone.** No more seeded RNG over the item fragment
  list. No more "this character got the boring two".
- **Anatomy is back.** The placeholder dropped anatomy entirely; the
  v1 rule reintroduces it as the sole tone author.
- **9 axes always.** The placeholder produced "2 messy fragments";
  the v1 rule produces up to 9 single-axis lines.
- **`TextingStyleFragmentSource.SlotOrParameter` is new.** The
  per-source breakdown now carries the slot ("shoes", "hat", …) or
  anatomy parameter id ("length", "girth", …) so the aggregator can
  do the slot→axis lookup without re-deriving it from item / anatomy
  definitions. This field is a strict superset; existing consumers
  keep their `Kind` / `Source` / `Fragment` reads.

The shape of the prompt section that PromptBuilder emits is unchanged
(still a bullet list under `TEXTING STYLE:`); only the contents become
deterministically authored from the new rule.

---

## Future work

- **Optional reveal mechanic** (issue body, "Discoverability layers"):
  an in-game UI that surfaces the player's current style in plain
  language, earned through gameplay (mirror item, NPC reactions,
  achievement-style cards). Tracked separately, not in v1.
- **Rule revision after playtest.** If a tone axis turns out to be
  dominated by one anatomy parameter that's nearly always the same
  tier, revisit the group composition or switch to weighted voting.
  No revision before playtest data is in.
- **Slot↔axis remap.** The slot→syntax assignment above is a design
  call. Future versions may swap mappings (e.g. accessory→length to
  match items that authentically dictate verbosity). Each swap is a
  new revision of THIS DOCUMENT, not an undocumented engine change.
