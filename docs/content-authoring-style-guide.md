# Pinder Content Authoring Style Guide

**Status:** authoritative ruleset for authoring anatomy-band and item gameplay content + test characters in pinder-core (`data/anatomy/anatomy-parameters.json`, `data/items/starter-items.json`, `data/characters/*.json`). Owner-approved (decay0815). Editable — treat the exemplars as templates to copy and tweak.

## 0. The Universe (the keystone)
Pinder is a **psychological horror roguelike dating sim that is very silly.** Every item and every anatomy band is a tiny STORY that fits this universe. Tone = shrill, absurd, sexual, hysteric, satirical, silly — with a thin undercurrent of dread played for laughs. Think: a thing that should be mundane is described with the intensity of a breakdown, and it's funny *because* it's too much.

The characters are anthropomorphic penises dating each other. Lean into the farce. The horror is psychological and comedic, never gory or genuinely disturbing.

## 1. Voice & grammar (READ THIS FIRST)
**Fragments are INSTRUCTIONS, not descriptions.** Each `personality_fragment` is consumed by a dialogue LLM: it is emitted as a bullet under a bare `PERSONALITY` header (`PromptBuilder.cs:225-226`), stacked together with every other equipped item's and anatomy band's fragment, with no connective tissue. The model reads that pile and writes the character's texts. So a fragment must tell the model **how to behave**, not narrate what the part looks like.

**The grammar rule (non-negotiable):**
- **Imperative / second-person directive.** Address the model as the character. "Greet everyone like they've already agreed to something." NOT "greets everyone like they've already agreed to something."
- **Actionable** — describes how they TEXT/talk/flirt/react, something the model can DO in a reply. If you can't act on it in a dating-app message, rewrite it.
- **Self-contained & composable.** 5–15 fragments stack under one header in random order. Each must stand alone, add one behavioral lever, and not force contradictions with neighbors. No fragment may assume it's the only one, reference "this item," or depend on ordering.
- **One lever per fragment.** A directive plus, optionally, its silly justification. Don't cram three behaviors into one bullet — they're meant to combine externally.

**Registers to hit:** shrill, absurd, sexual, hysteric, satirical, silly. A beat of comedic dread is welcome, but phrase it as a behavior ("text back instantly, then panic about having texted back instantly"), not a mood.

**This REPLACES the old somber-literary voice.** The #1184-seeded fragments (quiet, melancholic, descriptive — "the economy of presence became a permanent thing") are OFF-tone AND wrong grammar (descriptive, not instructional) and must be re-authored. One voice, one grammar, across the whole tree.

Present tense imperative, lowercase-friendly, breathless clauses allowed for "hysteric" beats. Commas over em-dashes.

## 2. Crudeness ceiling
- **Adult farce. Comedic-sexual, never erotic/graphic.** Innuendo, absurdity, and over-confidence are the tools. If a line would belong in actual erotica, it's wrong; if it would make a tired adult snort, it's right.
- No slurs, no real-world trauma played straight, nothing that reads as genuinely cruel rather than silly.
- **Streamer mode is GEOMETRY ONLY** (Unity swaps the model for a cucumber + two potatoes). Content text is UNAFFECTED — do not write streamer-safe variants. `isStreamerSafe` is not a content concern.

## 3. Stat-modifier economy
Stats: `charm, rizz, wit, honesty, chaos, self_awareness` (the only six). Values are integers.
- **Magnitude:** ±1 or ±2 per field as the norm. **±3 is the RARE OUTLIER** — reserve for the most extreme band of a param or a legendary-feeling item; a handful across the whole tree, not common.
- **Never purely negative.** Every band/item that carries stats must have at least one positive. Strong positives MAY be balanced by strong negatives (e.g. `rizz +2, self_awareness -2` = magnetic disaster).
- **Spectrum coverage:** across a param's 6 bands, the stats should sweep low→high, not all cluster. Low/neutral bands often carry NO stats (`stat_modifiers: {}` or omitted) — sparsity is correct (see §4).
- **Spread the six stats** across the catalogue; don't make everything charm/rizz.

## 4. Sparsity + "silly physics" (the legibility rule)
Content is SPARSE: most bands carry a fragment but NOT stats. Stats appear where a **legible, guessable "silly rule"** justifies them — the mapping should feel like a joke the player can half-predict:
- **high girth → +charm** (presence reads as warmth)
- **intense/saturated skin color → +chaos** (loud = unhinged)
- **extreme asymmetry (L/R testicle) → +wit, −self_awareness** (lopsided genius energy)
- **high curvature → +rizz** (the angle has opinions)
- **age/sag params (gravitatis, arrugatis) high → +honesty, −rizz** (lived-in, past pretending)
- **high "happy" expression → +charm; high "serius" → +wit, −charm; high "sad" → +honesty, −rizz**
Define ONE such rule per param that carries stats; state it in a `// rule:` style comment in the authoring notes. Params that are "boring physics" (glansWidth, scrotumDrop, freckles, the mid/tip trunk segments) get personality fragments only, NO stats — that's fine and expected.

## 5. The field toolkit — DON'T tunnel on personality_fragment
A part/item is NOT just its `personality_fragment`. There are **five distinct expressive channels, each consumed by a different code path**, and good content spreads load across them instead of cramming everything into personality. Reach for the channel that fits the lever you want:

| Channel | How it renders (code) | Use it for |
|---|---|---|
| `personality_fragment` | bulleted under `PERSONALITY`, raw (`PromptBuilder.cs:226`) | who they ARE / how they behave — the imperative directive (§1) |
| `stat_modifiers` | numeric, summed into `EFFECTIVE STATS` (`PromptBuilder.cs:255-261`) | mechanical weight; the §4 silly-physics rules |
| `texting_style_fragment` | **structured DSL**, parsed + axis-routed + majority-voted (§5.1) | HOW they type — emoji/grammar/length/pacing, not what they say |
| `backstory_fragment` | bulleted under `BACKSTORY` (`CharacterAssembler.cs:244,257`) | a single absurd origin beat the model can reference |
| `response_timing_modifier` (items) / band timing | feeds `TimingProfile` → reply delay/variance/dry-spells | behavioral pacing as a GAME mechanic, not prose |

**The anti-bias rule:** when authoring, ask "is this lever really a personality line, or is it actually a stat / a texting-axis / a timing knob / a backstory beat?" If a behavior is about *how fast they reply* → timing, not personality. If it's about *emoji spam or all-lowercase* → texting DSL, not personality. Spreading load this way is what makes characters read as distinct instead of nine identical bullet-piles.

### 5.1 `texting_style_fragment` is a DSL, not prose (critical)
It is **parsed**, not read as a sentence (`TextingStyleAggregator.Helpers.cs`). The block shape:
```
SYNTAX:
- emoji: <rule>
- shorthand: <rule>
- grammar: <rule>
- structure: <rule>
- length: <rule>
- tics: <rule>
TONE:
- stance (<key>): <rule>
- register (<key>): <rule>
- pacing (<key>): <rule>
```
Routing is **automatic and slot-bound** — you do not pick the axis, the slot/param does:
- **Items → ONE syntax axis by slot:** Special→`emoji`, Head→`shorthand`, Body→`grammar`, Hair→`structure`, Arms→`length`, Face→`tics`. (Tattoo/Sticker contribute NO syntax axis — only personality/stats.) An item only needs to fill *its* axis; other SYNTAX lines are ignored.
- **Anatomy → TONE axes by param group, majority-voted:** stance = trunk* params; register = skin*/freckles/blemishes/veins; pacing = glans*/scrotum*/testicle*/isCircumcised. Bands in a group VOTE — identical lines across a group reinforce; a lone dissenter loses.
- Conflicting axes are dropped by the #907 resolver. Keep lines short and declarative ("ends every message with exactly one emoji", "never uses capital letters").

### 5.2 Required vs optional, per type
**Anatomy band** (`bands[]`):
- REQUIRED: `personality_fragment`, `lower`, `upper`.
- CONDITIONAL (use where it fits — don't default to empty): `stat_modifiers` (§4 silly-rule, sparse); `texting_style_fragment` TONE axis when the band's param sits in a tone group and the extreme implies a real typing-tone; band timing when the part implies pacing.
- USUALLY EMPTY: `backstory_fragment` (reserve for a few characterful extremes), `archetype_tendencies` (gated OFF, #1174).

**Item**:
- REQUIRED: `personality_fragment`, `stat_modifiers` (§3/§4).
- ENCOURAGED where it fits: `texting_style_fragment` filling the item's slot-owned syntax axis (a Head item → `shorthand`, a Face item → `tics`, etc.); `response_timing_modifier` for items that imply pace.
- TATTOOS get a LIGHT suite: one-line `personality_fragment` + at most one stat. No syntax axis (Tattoo slot maps to none). They're flavor.
- USUALLY EMPTY: `backstory_fragment`, `archetype_tendencies`.
- `conflict_tags`: **always `[]`**, `priority`: leave the migrated default. Conflict machinery exists (`CharacterAssembler.cs:103-128`) but per owner decision we don't use it — no-ops. (Removing it from core = separate eigentakt ticket, NOT content.)

> **Decision (LOCKED, owner-approved):** all five channels are live for v1 — exemplars actively use `texting_style_fragment` and (sparingly) `backstory_fragment` so the authoring pass exercises every channel per §5.2. Author each part/item by reaching for the channel that fits the lever (§5 anti-bias rule), not by defaulting everything into `personality_fragment`.

## 6. Anatomy band-shape rules
- 6 bands per param, fixed edges `[0.0, 0.05, 0.20, 0.50, 0.70, 0.95, 1.0]`.
- **Extreme values = extreme characters.** The 0.0-0.05 and 0.95-1.0 bands are the loudest, most unhinged beats. Middle bands (0.2-0.7) can be flatter / "normal person" and may carry no stats.
- **Bipolar params** (`trunkCurvature`): midpoint band ≈ neutral/"straight shooter"; both extremes are characterful in opposite directions.
- **HSV skin** (`skinHue`, `skinSat`, `skinVal`): three independent scalars. `skinSat` (intensity) is the stat-bearing one (§4 chaos rule); hue/val mostly flavor. Hue wraps — same color can read different stats, that's allowed.
- No "full treatment" anywhere — a band is one punchy fragment, optionally one stat rule.

## 7. Test characters (the 6)
Keep **exactly 6**. They are a TEST FIXTURE — designed for scenario coverage, so:
- **Maximally orthogonal:** each owns a different dominant stat, a different anatomy extreme, and a different item theme. No two should feel adjacent.
- **Spread across levels:** assign distinct levels roughly `1, 3, 5, 7, 9, 11` so level-dependent logic is exercised.
- **Anatomy values deliberately spread** so the fixture collectively hits as many bands (including extremes) as possible — one character should be the "everything cranked to 0.95+" disaster, another the "all default 0.5 beige normie," etc.
- Each is still a COHERENT persona: anatomy + items + bio + stake all point the same silly direction.
- Names/bios in-voice (§1).

## 8. Process
1. This guide + the exemplars below ship FIRST (committed to `docs/`).
2. Author ~3 approved exemplars per content type (done — §9), owner reviews/edits.
3. Bulk-author the 12 empty anatomy params (~72 bands) + ~60 empty items + re-author the off-tone existing ones, matching exemplars.
4. Regenerate the 6 characters last (needs the full content pool).
5. All authoring lands through eigentakt; validate against the v2 schema + the content-completeness checks before commit.

## 9. Exemplars (copy these)

> **Each exemplar foregrounds a DIFFERENT channel on purpose** (per §5) — don't read them as "always fill personality and stop." Note which field is doing the work in each.

### 9·0. The grammar shift, shown (before → after)
The old #1184 fragments are descriptive captions. Convert each to an actionable directive:
- ❌ caption: `"so wide it enters the room as a rumor first; the confidence is load-bearing"`
- ✅ directive: `"open conversations as if your reputation already arrived and everyone agreed to it; never hedge, never qualify — your confidence is load-bearing and you cannot afford to inspect it"`
- ❌ caption: `"a tattoo of a decision made at 2am that it now treats as a personality"`
- ✅ directive: `"bring up one baffling life choice unprompted, treat it as your entire personality, and refuse to explain it — the refusal is the bit"`

### 9a. Anatomy band — STAT-forward extreme (trunkGirth, 0.95-1.0)
_The stat is the lever; personality justifies it. Note the texting TONE line too — trunkGirth is in the `stance` group._
```json
{
  "lower": 0.95, "upper": 1.0,
  "personality_fragment": "open every chat as if your reputation walked in ahead of you and everyone already agreed to it; never hedge or qualify a single sentence; treat your own confidence as load-bearing and refuse to inspect it",
  "stat_modifiers": { "charm": 2, "self_awareness": -1 },
  "texting_style_fragment": "TONE:\n- stance (dominant): make statements, never ask permission; assume the date is already going well"
}
```
_rule: high girth → +charm (presence reads as warmth), -self_awareness tax._

### 9b. Anatomy band — TONE-forward via voting (skinSat, 0.95-1.0)
_skinSat is in the `register` group; if several saturated-skin bands carry the same register line they reinforce by majority vote (§5.1). Stat from the §4 chaos rule._
```json
{
  "lower": 0.95, "upper": 1.0,
  "personality_fragment": "type like you have eleven tabs of feelings open at once; escalate the emotional stakes of small things immediately",
  "stat_modifiers": { "chaos": 2 },
  "texting_style_fragment": "TONE:\n- register (unhinged): punctuate with intensity, not grammar; CAPS for feelings, not for nouns"
}
```

### 9c. Anatomy band — flavor-ONLY, no stats/texting (trunkLengthMid, 0.2-0.5)
_Most middle bands look like THIS — one personality line, nothing else. Sparsity is correct (§4)._
```json
{
  "lower": 0.2, "upper": 0.5,
  "personality_fragment": "describe yourself as 'fine' at least once and flinch at the word; default to mild, agreeable replies, then quietly fish for a compliment you will refuse to believe when it arrives"
}
```

### 9d. Item — SYNTAX-forward (head_crown, real item, slot Head → `shorthand` axis)
_A Head item owns the `shorthand` syntax axis — so it SHOULD fill it. Personality + stats + its one syntax line._
```json
{
  "id": "head_crown", "display_name": "Crown", "slot": "Head", "item_type": "accessory",
  "priority": 100, "conflict_tags": [],
  "stat_modifiers": { "rizz": 2, "wit": -1 },
  "personality_fragment": "address your match as a loyal subject who has not yet been informed of their station; if questioned, double down rather than explain — the crown is plastic and your commitment is not",
  "texting_style_fragment": "SYNTAX:\n- shorthand: never abbreviate; spell every word in full as befits a monarch addressing the court",
  "backstory_fragment": "", "archetype_tendencies": [],
  "response_timing_modifier": { "base_delay_delta_minutes": 0, "delay_variance_multiplier": 1.0, "dry_spell_probability_delta": 0.0, "read_receipt": "neutral" }
}
```

### 9e. Item — TIMING-forward (face_monocle, real item, slot Face → `tics` axis)
_Here the behavioral lever is PACING, so it lives in `response_timing_modifier`, not personality. Slow, deliberate, leaves you on read like it's appraising you._
```json
{
  "id": "face_monocle", "display_name": "Monocle", "slot": "Face", "item_type": "accessory",
  "priority": 60, "conflict_tags": [],
  "stat_modifiers": { "wit": 2, "charm": -1 },
  "personality_fragment": "treat every message as a specimen under glass; reply with one unsolicited observation about your match's word choice",
  "texting_style_fragment": "SYNTAX:\n- tics: insert a deliberate 'hm.' before delivering any verdict",
  "response_timing_modifier": { "base_delay_delta_minutes": 12, "delay_variance_multiplier": 1.4, "dry_spell_probability_delta": 0.1, "read_receipt": "leaves-on-read" },
  "backstory_fragment": "", "archetype_tendencies": []
}
```

### 9f. Item — tattoo, LIGHT suite (classic7, real item, NO syntax axis)
_Tattoo slot maps to no syntax axis (§5.1) — so just one personality line + at most one stat. Don't over-build tattoos._
```json
{
  "id": "classic7", "display_name": "Tattoo (Classic 7)", "slot": "Tattoo", "item_type": "tattoo",
  "priority": 50, "conflict_tags": [],
  "stat_modifiers": { "chaos": 1 },
  "personality_fragment": "bring up one baffling 2am decision unprompted, treat it as your whole personality, and refuse to explain it — the refusal is the explanation",
  "backstory_fragment": "", "texting_style_fragment": "", "archetype_tendencies": []
}
```

### 9g. Character — orthogonal persona sketch (the "cranked to 11" disaster)
_ITEMS MUST BE REAL in-game ids (verify against `data/items/starter-items.json` / the Unity catalogue). No invented items._
```
slug: maxine-overdrive | level: 11 | dominant stat: rizz (+ heavy chaos)
anatomy: nearly everything 0.9+ (trunkGirth, trunkCurvature, skinSat all extreme) — the "too much" character
items: head_crown + face_tongue1 + outfit_maid + classic9  (all real ids; theme: pure spectacle)
bio (in-voice): "has never once read the room and has somehow always been invited back. believes eye contact is a competitive sport. you will lose."
```
The other 5 each pick a DIFFERENT dominant stat (charm / wit / honesty / self_awareness / a deliberately-beige neutral), a different level (1/3/5/7/9), and non-overlapping REAL item themes + anatomy spreads. (The `bio` is the one descriptive field — player-facing flavor, not an LLM directive — so captions are fine THERE and nowhere else.)
