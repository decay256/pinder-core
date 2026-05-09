# Texting-Style Schema — Investigation Before Rework

Investigation conducted before reworking the per-item `texting_style_fragment`
field to use the new pool taxonomy (`docs/persona/texting-style-pool.md`).

## 1. Where item definitions live

- **Path:** `data/items/starter-items.json`
- **Format:** JSON array, 44 items, single file.
- No YAML item definitions exist. The `rules/extracted/items-pool-enriched.yaml`
  file is unrelated rules-extraction output, not a runtime item source.

Each item object has the following shape:

```json
{
  "item_id": "...",
  "display_name": "...",
  "slot": "shirt | trousers | shoes | hat | accessory | frame",
  "tier": "common | uncommon | rare",
  "stat_modifiers": { "<stat>": <int>, ... },
  "personality_fragment": "...",
  "backstory_fragment": "...",
  "texting_style_fragment": "...",
  "archetype_tendencies": ["..."],
  "response_timing_modifier": { ... },
  "flavor": { "shop_description": "...", "equip_text": "..." }
}
```

## 2. Texting-style field shape

`texting_style_fragment` is a **single free-text prose string** per item.
Examples from the current data:

- `vintage-band-tee`: *"lowercase, minimal punctuation; occasionally drops a
  song lyric with no attribution and no context; uses '...' more than commas;
  never uses exclamation marks"*
- `hiking-boots`: *"blunt and plain; says the thing and moves on without
  softening it; no hedging words; the only emoji is an occasional 🌲 and it
  means something when it appears"*
- `rubber-duck`: *"occasionally includes a brief parenthetical from the
  duck's perspective with complete sincerity..."*

The field is unstructured: no axes, no bullets, no tags. It mixes mechanical
rules (lowercase, no exclamation marks) with vibe descriptors (blunt and
plain, says the thing). This is exactly the abstraction problem the new
taxonomy is designed to fix.

## 3. Coverage

- **Total items:** 44
- **Items with a non-null `texting_style_fragment`:** 44 (100%)
- **Items missing the field:** 0

Every item has a free-text texting-style line. There is no opt-out, no null,
no empty string.

## 4. Downstream consumption

The data flows through these C# components:

1. **Load** — `src/Pinder.Core/Data/JsonItemRepository.cs:54` reads
   `texting_style_fragment` as a plain string into `ItemDefinition`
   (`src/Pinder.Core/Characters/ItemDefinition.cs:22`,
   `TextingStyleFragment` property, type `string`).

2. **Aggregate per character** —
   `src/Pinder.Core/Characters/CharacterAssembler.cs:142` collects each
   worn item's `TextingStyleFragment` into a list of
   `TextingStyleFragmentSource` records and stores them on the character's
   `FragmentCollection` (`src/Pinder.Core/Characters/FragmentCollection.cs:20`,
   `TextingStyleFragments` and `TextingStyleSources`).

3. **Build prompt — character sheet** —
   `src/Pinder.Core/Prompts/PromptBuilder.cs:59` joins all per-item
   fragments with `" | "` under a `TEXTING STYLE` header in the character
   sheet block.

4. **Build prompt — runtime injection** —
   `src/Pinder.SessionSetup/CharacterDefinitionLoader.cs:152` joins them
   with `" | "` again into the `PlayerTextingStyle` field on
   `DialogueContext`. `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs:102`
   then injects this just before the ENGINE block as:

   ```
   YOUR TEXTING STYLE — follow this exactly, no deviations:
   <joined texting style>
   ```

   This is a directive injection — the LLM is told to follow it exactly.

**Implication for rework:** the field is treated as an opaque string. Newlines
inside the JSON string survive the load (standard `\n` escaping) and will
appear in the joined output. Replacing prose with a bulleted markdown block
means the joined output will look like:

```
- emoji rule: ...
- shorthand: ...
...
**stance:** ...
**register:** ...
**pacing:** ...
 | - emoji rule: ...
- shorthand: ...
...
```

Where the ` | ` separator now divides item-blocks rather than item-sentences.
This is a slight visual oddity but is well-formed for an LLM directive — and
arguably *better* than the current prose blob, because each item's
contribution is now structurally distinct.

## 5. Items where texting style does not apply

None. Every slot category contributes texting style:

- `shirt`, `trousers`, `shoes`, `hat`, `accessory`, `frame`

This includes items one might intuitively think don't affect speech — e.g.
`gold-hoop-earrings`, `friendship-bracelet-handmade`, `nose-ring-septum`,
`barefoot`. The current design treats every worn item as an expressive
modifier. The new pool inherits that assumption.

The pool itself does flag pacing as optional ("for items where a particular
axis genuinely doesn't fit (e.g. a piece of jewelry that doesn't change
pacing), it's okay to skip that axis"), but no item is exempt from
contributing a texting-style block entirely.

## 6. Decision: proceed to B3

The schema is consistent and uniform:

- ✅ Single field, single string type
- ✅ Present on every item (no edge cases)
- ✅ Loaded as opaque text — bullets-in-string is well-formed
- ✅ Downstream is a directive injection — bullets are clearer than prose
- ✅ Tests reference the field generically (no parsing of the prose)

**Proceeding to B3.** No blockers.

### Selection strategy for B3

- 44 items. Pool sizes:
  - emoji: 11 bullets
  - shorthand: 13
  - grammar: 17
  - structure: 12
  - length: 5
  - tics: 8
  - stance: 22
  - register: 21
  - pacing: 6
- Total pool size: 115 bullets across 9 categories
- Per item: 6 syntax + 1 stance + 1 register + 1 pacing = **9 bullets per item**
- Total bullet-uses across 44 items (with all axes filled): 44 × 9 = 396
- Strategy: round-robin within each category, tracking usage. Categories
  smaller than 44 (length=5, pacing=6, tics=8, emoji=11, structure=12,
  shorthand=13, grammar=17, register=21, stance=22) will all see every
  bullet used at least once with comfortable headroom.
- For the `length` (5 bullets) and `pacing` (6 bullets) categories, every
  bullet will be used roughly 44/5 ≈ 9 times and 44/6 ≈ 7 times
  respectively. Acceptable — no two items will have identical full bullet
  sets across the 9 axes.
- Skip rule: the pool's "okay to skip pacing for jewelry" allowance is
  honored sparingly; preferred behavior is to always pick something so the
  prompt doesn't go quiet on an axis. Skips will be noted in the commit
  message.
