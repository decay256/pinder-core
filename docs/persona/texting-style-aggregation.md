# Texting-Style Aggregation Rule (v1)

Note: This relies on item schema v2.

This is the canonical rule for aggregating a character's equipped items
and scalars into the **9-axis texting-style block** that ends
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
items, scalar levels). Two runs against the same configuration produce
the same 9-line aggregate, byte-exact. Players never see this aggregate
directly — they discover it indirectly through the LLM's behaviour.

---

## Item slots own syntax (1:1 fixed mapping)

Each of the 6 item slots owns exactly one syntax subcategory:

| slot       | syntax axis |
|------------|-------------|
| `Special`    | `emoji`     |
| `Head`      | `shorthand` |
| `Body`    | `grammar`   |
| `Hair` | `structure` |
| `Arms`    | `length`    |
| `Face`| `tics`      |

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
item in the `Special` slot has `- emoji: ends every sentence with an emoji
that conveys its emotion`, that's the line written to the final
fragment's `emoji` axis. Items in the `Special` slot do NOT contribute to
any other axis: only the slot's owned axis is read.

This is the design rule that makes the system **discoverable by
gameplay**: swap one item, see exactly one syntax axis change. Players
cannot fight back across the boundary.

---

## Scalars lock tone (3:1 group-vote mapping)

The 9 scalars are partitioned into 3 groups of 3. Each
group decides one tone axis:

| scalars params                                       | tone axis  |
|------------------------------------------------------|------------|
| `length`, `girth`, `circumcision`                    | `stance`   |
| `vein_definition`, `skin_texture`, `skin_tone`       | `register` |
| `ball_size`, `tattoos`, `eye_style`                  | `pacing`   |

For each group, the aggregator extracts the tone-axis line from each
selected level in the group. The decision rule is:

1. **Drop empty contributions.** If a level has no `texting_style_fragment`
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

Scalars are **never** read for syntax. The full grouping is fixed and
documented here; designers can verify and operators can predict.

---

## Output format

The aggregator returns a list of strings, one per filled axis, in the
canonical order:

```
emoji: <line from Special.SYNTAX.emoji>
shorthand: <line from Head.SYNTAX.shorthand>
grammar: <line from Body.SYNTAX.grammar>
structure: <line from Hair.SYNTAX.structure>
length: <line from Arms.SYNTAX.length>
tics: <line from Face.SYNTAX.tics>
stance: <majority winner from {length, girth, circumcision}.TONE.stance>
register: <majority winner from {vein_definition, skin_texture, skin_tone}.TONE.register>
pacing: <majority winner from {ball_size, tattoos, eye_style}.TONE.pacing>
```

Axes whose source is empty are dropped, not emitted as
`emoji: (empty)` or similar. The downstream consumers
(`PromptBuilder`, `CharacterProfile.TextingStyleFragment`) emit each
remaining axis as its own bullet line in the system prompt's TEXTING
STYLE section.

When the character has no items and no scalar contributions, the
output is an empty list — the section header may still be emitted but
carries no rules.

---

## Why this rule

1. **9 axes. Always.** No more, no less. 6 items × 1 axis per slot, 3
   scalar groups × 1 axis per group. No risk of soup.
2. **Discoverable through gameplay.**
   - Layer 1: "swapping clothes never changes my stance" → scalars
     decides tone. The player figures this out across multiple equipment
     swaps.
   - Layer 2: "swapping shoes changes my emoji rule" → items decide
     syntax, slot-by-slot. The 1:1 slot→axis mapping is the second-order
     discovery — players will notice that swapping the same slot
     consistently changes the same axis.
3. **Not directly controllable.** Players never see the prompt, can't
   say "give me dry stance". The path from intent → result goes through
   scalars + items, which are physical decisions, not text knobs.
4. **Surfaces both items and scalars.** Scalars are back in the wire —
   the previous placeholder silenced scalars entirely, which broke the
   discoverability layer. Now scalars are the only path to tone.
5. **Deterministic.** Per-character; per-configuration. No re-roll mid
   conversation. Build-craft is preserved: the player's choices stick.
6. **Auditable.** Every axis has exactly one source. A reviewer reading
   the assembled prompt can trace each line back to a specific slot or
   scalar without RNG.

---

## What changed from the placeholder

- **Random-pick-2 is gone.** No more seeded RNG over the item fragment
  list. No more "this character got the boring two".
- **Scalars are back.** The placeholder dropped scalars entirely; the
  v1 rule reintroduces it as the sole tone author.
- **9 axes always.** The placeholder produced "2 messy fragments";
  the v1 rule produces up to 9 single-axis lines.
- **`TextingStyleFragmentSource.SlotOrParameter` is new.** The
  per-source breakdown now carries the slot ("shoes", "hat", …) or
  scalar id ("length", "girth", …) so the aggregator can
  do the slot→axis lookup without re-deriving it from item / scalars
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
  dominated by one scalar that's nearly always the same
  level, revisit the group composition or switch to weighted voting.
  No revision before playtest data is in.
- **Slot↔axis remap.** The slot→syntax assignment above is a design
  call. Future versions may swap mappings (e.g. Face→length to
  match items that authentically dictate verbosity). Each swap is a
  new revision of THIS DOCUMENT, not an undocumented engine change.

---

## Cross-axis conflict resolution (v1.1 — issue #907)

As of #907, the aggregator applies a **conflict matrix** to the picked axis
values before emitting the final list. The matrix is encoded in
`data/persona/texting-style-conflicts.yaml` and loaded by
`TextingStyleConflicts`.

### Why conflicts arise

Each axis is picked independently (slot → syntax axis; scalars group → tone
axis). There is no constraint across axes during the pick phase. Some
combinations are semantically contradictory even though each individual pick
is valid:

- `structure: wall-of-text` + `length: never sends more than 5 words` — the
  LLM resolves this by applying the stricter/more concrete rule (`≤5 words`),
  silently overriding the engine's `playerLen` length hint from #866.
- `pacing: fast, breathless` + `structure: measured whitespace` — mutually
  incompatible style demands.

### Resolution algorithm

After all per-axis picks are assembled:

1. Walk the picked set in the order they were assembled (canonical axis order).
2. For each candidate value, check it against all already-kept values using
   the conflict matrix.
3. On conflict: drop the candidate (the later-picked value). The earlier-kept
   value wins — deterministic, replayable.
4. Emit one `ConflictDropEntry` per dropped value into the audit log.
5. Callers use `AggregateWithAudit()` to retrieve both the final lines and
   the audit log.

### Auditor tool

`tools/TextingStyleAuditor/` is a data-hygiene console app. Run it when
adding new items to `data/items/starter-items.json`:

```bash
dotnet run --project tools/TextingStyleAuditor
```

Exit code 0 = all detected conflict pairs are covered by the matrix.
Exit code 1 = unregistered conflicts found — add a matrix entry or rewrite
the item fragment.

### Adding a new conflict

Add one entry to `data/persona/texting-style-conflicts.yaml`:

```yaml
  - axis_a: { axis: <name>, value: "<parsed-value>" }
    axis_b: { axis: <name>, value: "<parsed-value>" }
    reason: "<why these can't coexist>"
```

Values must match the **parsed** axis value (the text after `:` in the
fragment line, with the parenthetical sub-key stripped from the axis name).
The constraint is bidirectional — encode it once, the resolver checks both
orderings.

### Length-hint defensive rule (#907 belt-and-braces)

`SessionDocumentBuilder` appends a priority statement after the standard
`playerLen` length hint:

> "The length rule above is a stylistic guideline, NOT a hard cap. For this
> message, aim for ~{playerLen} characters as the engine specifies.
> Style-rule length axes apply ONLY when they are compatible with the
> engine-specified length."

This ensures that even if the conflict resolver misses a case, the engine's
length floor takes priority over a style-rule hard cap.
