# Texting-Style Aggregation

This document describes the implemented `TextingStyleAggregator` contract.
The implementation is authoritative when this document and code disagree.

## Canonical output

Core recognizes nine canonical axes and emits filled axes in this order:

```text
emoji, shorthand, grammar, structure, length, tics,
directness, affect, rhythm
```

The first six are syntax axes sourced from equipped items. The final three are
expression axes sourced from anatomy. An empty slot or an anatomy group with no
usable contribution omits its axis, so the result contains **up to nine**
lines. Missing axes are not back-filled or emitted as placeholders.

The aggregate is a soft set of expressive influences. Downstream prompts tell
the model to use only tendencies that fit naturally and to preserve meaning,
personality, emotional state, and conversational variation. Selection does not
promise that a tendency will be visible in every generated message.

## Item slot mapping

Each equipped item contributes only the syntax axis owned by its slot:

| Current slot | Syntax axis | Legacy alias accepted |
| --- | --- | --- |
| `Special` | `emoji` | `shoes` |
| `Head` | `shorthand` | `hat` |
| `Body` | `grammar` | `shirt` |
| `Hair` | `structure` | `trousers` |
| `Arms` | `length` | `frame` |
| `Face` | `tics` | `accessory` |

Lookups are case-insensitive. `Waist` has no syntax mapping. Tattoo and Sticker
items may contribute to other character channels but do not own a texting-style
syntax axis.

An item may author all six syntax axes for reuse in different slots, but the
aggregator reads only the axis assigned to the item's equipped slot. If more
than one source claims the same slot, source order decides and the first source
wins; that situation is a content/assembly error rather than a blending rule.

## Anatomy expression mapping

Anatomy parameters are partitioned into three ordered groups:

| Expression axis | Parameters in tie-break order |
| --- | --- |
| `directness` | `trunkLengthBase`, `trunkLengthMid`, `trunkLengthTip`, `trunkGirth`, `trunkCurvature` |
| `affect` | `skinHue`, `skinSat`, `skinVal`, `freckles`, `smoothness`, `veins` |
| `rhythm` | `glansScale`, `glansWidth`, `scrotumScale`, `leftTesticleScale`, `rightTesticleScale`, `scrotumDrop`, `isCircumcised` |

For each parameter, Core parses the parameter's selected anatomy-band fragment
and chooses at most one candidate for the group's axis. It then resolves the
group by majority vote over the selected candidate text:

1. Parameters without a usable candidate do not vote.
2. Identical candidate text is grouped using ordinal comparison.
3. The text with the most votes wins.
4. A tie is won by the candidate whose first contributing parameter appears
   earliest in the table's parameter order.
5. If no parameter contributes, the expression axis is omitted.

Anatomy is not read for syntax axes.

## Authoring format and migration aliases

New content uses this shape:

```text
SYNTAX:
- emoji: may use a soft emoji as terminal punctuation when warmth is visible
- shorthand: tends to use "ngl" before a candid thought
- grammar: is comfortable writing casual messages in lowercase
- structure: tends to split layered thoughts across a few short lines
- length: usually keeps replies compact without losing the emotional beat
- tics: may echo one important word before answering
EXPRESSION:
- directness: tends to imply attraction before naming it directly
- affect: is comfortable letting guarded warmth show through dry phrasing
- rhythm: tends to accelerate when emotionally engaged
```

For migration compatibility, the parser also accepts the `TONE:` section and
normalizes old expression keys as follows:

| Legacy input key | Canonical axis |
| --- | --- |
| `stance` | `directness` |
| `register` | `affect` |
| `pacing` | `rhythm` |

These aliases are for existing data, not new authoring. Parsed output,
attribution, conflict validation, and final aggregate lines use canonical axis
names.

## Candidate pools and deterministic selection

An axis can contain a single candidate or an indented candidate pool:

```text
EXPRESSION:
- affect:
  - tends to keep warmth understated until trust is established
  - may let enthusiasm become obvious when a specific interest is shared
```

Before slot extraction or anatomy voting, Core selects exactly one candidate
per source and axis. A single candidate is returned directly. Multiple
candidates are selected with SHA-256 over a length-delimited canonical input
containing:

- the selector version salt;
- `seedKey` (normally the character identity);
- source kind, name, source id, slot or parameter, and anatomy band index;
- canonical axis name; and
- the candidate count plus every candidate in authored order.

The first eight hash bytes select `hash % candidate_count`. The same complete
input produces byte-for-byte stable selection across calls and sessions.
Different seeds may give different characters different candidates. Editing,
reordering, adding, or removing candidates intentionally changes selector input
and may change the chosen candidate.

Selection is bounded: Core does not concatenate the pool and does not ask the
LLM to choose among it. For anatomy, candidate selection occurs before the
majority vote.

## Output, attribution, and conflicts

`AggregateWithAudit` returns:

- `Lines`: canonical `axis: value` strings in canonical axis order;
- `AttributedLines`: the same kept lines with `SourceName`, `SourceKind`,
  `SourceId`, `SlotOrParameter`, and optional `BandIndex`; and
- `Drops`: entries removed by conflict resolution, including the conflicting
  kept value and the catalog reason.

After all axes are selected, Core walks them in canonical order. A candidate
that conflicts with an already-kept value in
`data/persona/texting-style-conflicts.yaml` is dropped; the earlier canonical
axis wins. Conflict matching is bidirectional and case-insensitive for axis and
value. The catalog must use the exact parsed value text. Its axis keys are
normalized to the canonical model when loaded.

Example result:

```text
emoji: may use a soft emoji as terminal punctuation when warmth is visible
grammar: is comfortable writing casual messages in lowercase
directness: tends to imply attraction before naming it directly
affect: is comfortable letting guarded warmth show through dry phrasing
rhythm: tends to accelerate when emotionally engaged
```

The result can have fewer than nine lines when sources are missing or a conflict
drops a later value. `Aggregate` returns those lines joined for the prompt;
`AggregateWithAudit` is the traceable domain result.

## Discoverability contract

- Changing an equipped item can change the one syntax axis owned by its slot.
- Changing an anatomy band can change the vote for its expression group.
- A stable character/configuration/seed combination remains stable across
  sessions.
- The style influences delivery without overriding character psychology,
  emotional direction, game state, or what the message needs to communicate.

Players do not edit these prompt axes directly. They encounter their effects
through equipment, anatomy, and generated conversation.

## Authoring and validation

Use [texting-style-pool.md](texting-style-pool.md) for fragment wording. When
adding or changing content:

1. Use canonical section and axis names.
2. Keep each candidate a concise tendency.
3. Confirm the source's slot or anatomy group actually consumes that axis.
4. Run the texting-style auditor when changing item or anatomy content:

   ```bash
   dotnet run --project tools/TextingStyleAuditor
   ```

5. Add a bidirectional conflict once in
   `data/persona/texting-style-conflicts.yaml` when two exact parsed values
   cannot coexist.

The conflict catalog schema is:

```yaml
conflicts:
  - axis_a: { axis: <canonical-axis>, value: "<exact-parsed-value>" }
    axis_b: { axis: <canonical-axis>, value: "<exact-parsed-value>" }
    reason: "<why the values cannot coexist>"
```

Historical design investigations and sprint notes may use the old vocabulary.
They are not active authoring guidance.
