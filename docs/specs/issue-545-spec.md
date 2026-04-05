# Issue #545 — Create game-definition.yaml with Pinder Game Vision, World Rules, and Meta Contract

**Module**: `docs/modules/llm-adapters.md` (update existing)

---

## Overview

Pinder's creative direction — game vision, world description, character role descriptions, meta contract, and writing rules — is currently either hardcoded in `GameDefinition.PinderDefaults` or does not exist as a standalone data artifact. This issue creates a YAML data file (`game-definition.yaml`) containing all five sections of Pinder's creative identity so that `GameDefinition.LoadFrom()` (from issue #543) can parse it and feed it into `SessionSystemPromptBuilder.Build()`. This makes creative direction data-driven: a game designer can edit the YAML without recompilation.

---

## File Location and Schema

### File Path

```
/root/.openclaw/agents-extra/pinder/data/game-definition.yaml
```

### YAML Schema

The file has exactly 7 top-level keys. All are required. All values are YAML block-scalar strings (using the `|` indicator for multi-line literal content).

```yaml
name: "Pinder"
vision: |
  <multi-line game vision text>
world_description: |
  <multi-line world rules text>
player_role_description: |
  <multi-line player role text>
opponent_role_description: |
  <multi-line opponent role text>
meta_contract: |
  <multi-line meta contract text>
writing_rules: |
  <multi-line writing rules text>
```

### Key Definitions

| Key | Type | Description |
|-----|------|-------------|
| `name` | `string` | The game's display name. Value: `"Pinder"`. |
| `vision` | `string` (multi-line) | The game's creative premise and tonal identity. Establishes what Pinder is, the comedy register, and the emotional core beneath the absurdity. |
| `world_description` | `string` (multi-line) | The rules of Pinder's world. Describes the multiplayer structure, what characters are (sentient penises on a dating app), the stat/shadow system, and how conversations work mechanically. |
| `player_role_description` | `string` (multi-line) | Instructions for how the LLM should generate content from the player's perspective. Covers: the player is the one initiating, their dialogue options should reflect their character build (stats, items, personality fragments), and their voice must match their texting style. |
| `opponent_role_description` | `string` (multi-line) | Instructions for how the LLM should portray the opponent character. Covers: the opponent is another player's uploaded character being puppeted by the LLM, they maintain resistance below Interest 25, their personality comes from their own items/anatomy/fragments, and they react to game events (failures, traps, shadow taint). |
| `meta_contract` | `string` (multi-line) | Rules the LLM must never break. Covers: never break character, never reference game mechanics explicitly in dialogue, never add content the player didn't choose, never resolve the date before Interest 25, maintain two distinct character voices throughout. |
| `writing_rules` | `string` (multi-line) | Stylistic constraints for all LLM-generated text. Covers: texting register (not formal prose), message length limits, emoji usage conventions, no asterisk actions, comedy through character voice not narration, and the principle that strong rolls improve phrasing rather than adding ideas. |

---

## Content Requirements

Each section must contain genuine Pinder creative direction derived from the game's design documents (primarily `design/systems/rules-v3.md` and `design/systems/character-construction.md`). The content must NOT be generic boilerplate — it must be specific to Pinder's identity as a comedy dating RPG where players are sentient penises.

### `name`

A single string: `"Pinder"`.

### `vision`

Must establish:
- The core premise: sentient penises on a Tinder-like dating app
- The tone: comedy first, but with genuine emotional stakes underneath the absurdity
- The mechanical identity: RPG with dice rolls, stats, and shadows that corrupt your best qualities
- The social contract: it's funny AND it matters — players should laugh but also feel tension when shadows grow or interest drops
- That this is a multiplayer structure: every opponent is another real player's uploaded character, puppeted by the LLM

### `world_description`

Must establish:
- Characters are sentient penises who dress up, build stats, and upload themselves to a dating server
- The stat/shadow pair system (6 positive stats, 6 shadows that grow and penalize their paired stat)
- The dating conversation structure: d20 rolls against opponent's defence DC, success/failure scale, interest meter (0–25)
- The multiplayer async structure: your character exists on a server and other players encounter it independently
- Shadows grow from in-conversation events (not player choice) and represent the character's psychological state
- The interest meter is the conversation's health bar — Bored (1–4) risks ghosting, Date Secured (25) is victory

### `player_role_description`

Must establish:
- The LLM generates 4 dialogue options per turn, each tied to one of the 6 stats
- Options must reflect the player character's personality (assembled from items + anatomy fragments)
- The player's texting style fragment is the voice authority — options must sound like THIS character, not generic
- Horniness mechanics can force Rizz options (at shadow threshold ≥6, ≥12, ≥18)
- Combo and callback opportunities should appear naturally in option content when available

### `opponent_role_description`

Must establish:
- The opponent is another player's character being puppeted — their personality prompt is their bible
- Below Interest 25, the opponent maintains resistance proportional to their current interest state
- The opponent reacts to mechanical events: failure tiers affect their response tone, shadow taint affects their perception
- The opponent has their own texting style that must remain distinct from the player's
- At Date Secured (Interest 25), resistance dissolves genuinely — not abruptly, but as earned warmth

### `meta_contract`

Must establish:
- Never break character — the LLM is always in-world
- Never reference dice, DCs, stats, interest meters, or any game mechanic in dialogue text
- Never add ideas the player didn't choose — success delivery improves phrasing, doesn't expand content
- Never resolve the date early — Interest must reach 25 mechanically
- Maintain two distinct character voices — the player and opponent should never sound alike
- [ENGINE] blocks are out-of-character mechanical context — never quote or reference them in dialogue

### `writing_rules`

Must establish:
- All dialogue is texting register — short messages, informal, platform-appropriate
- Message length: typically 1–3 sentences for options, 1–4 for opponent responses
- Emoji: use sparingly and only when it matches the character's texting style fragment
- No asterisk actions (`*walks over*`) — this is a text-based dating app, not roleplay
- Comedy comes from character voice, not narration or winking at the audience
- Strong rolls sharpen phrasing; they do NOT add new ideas or topics
- Failed deliveries corrupt the message — typos, awkward phrasing, wrong tone — proportional to failure tier

---

## Input/Output Examples

### Input: Raw YAML file content (abbreviated)

```yaml
name: "Pinder"
vision: |
  Pinder is a comedy dating RPG where every character is a sentient penis
  on a Tinder-like app. You dress up, build stats, and try to charm other
  players' characters into going on a date — using dice rolls, real-time
  stat checks, and an LLM that generates the actual conversation.
  
  The tone is absurdist comedy with genuine emotional stakes. You will laugh
  at a mushroom-hat-wearing penis named Gerald trying to quote Dostoevsky,
  but you will also feel the tension when his Dread shadow hits 18 and every
  option starts sounding like a cry for help.
world_description: |
  Characters are sentient penises who exist on a dating server. Each has
  6 positive stats (Charm, Rizz, Honesty, Chaos, Wit, Self-Awareness) and
  6 shadow stats (Madness, Horniness, Denial, Fixation, Dread, Overthinking)
  that grow during conversations and penalize their paired positive stat.
  ...
player_role_description: |
  You generate dialogue options for the player character. Each turn produces
  4 options, each tied to one of the 6 stats. Options must sound like the
  player's character — use their texting style fragment as the voice bible.
  ...
opponent_role_description: |
  You portray the opponent character — another player's uploaded creation
  being puppeted by you. Their personality prompt is your character bible.
  ...
meta_contract: |
  Never break character. Never reference dice, DCs, interest meters, or
  any game mechanic in dialogue. Never add content the player didn't choose.
  ...
writing_rules: |
  All dialogue uses texting register. Messages are short (1-3 sentences for
  options, 1-4 for responses). No asterisk actions. Comedy through voice.
  ...
```

### Output: Parsed by `GameDefinition.LoadFrom(yamlContent)`

A `GameDefinition` instance with all 7 properties populated as non-null, non-empty strings. Each multi-line value preserves its line breaks (YAML `|` block scalar semantics: trailing newline, internal newlines preserved).

```
GameDefinition.Name           → "Pinder"
GameDefinition.Vision         → "Pinder is a comedy dating RPG where every character is a sentient penis\non a Tinder-like app..."
GameDefinition.WorldDescription → "Characters are sentient penises who exist on a dating server..."
GameDefinition.PlayerRoleDescription → "You generate dialogue options for the player character..."
GameDefinition.OpponentRoleDescription → "You portray the opponent character..."
GameDefinition.MetaContract   → "Never break character. Never reference dice..."
GameDefinition.WritingRules   → "All dialogue uses texting register..."
```

---

## Acceptance Criteria

### AC1: File exists at correct path

The file MUST be created at `/root/.openclaw/agents-extra/pinder/data/game-definition.yaml`. This follows the existing data file pattern used by other Pinder data files (`traps/traps.json`, `items/starter-items.json`, `anatomy/anatomy-parameters.json`, `characters/*.json`).

### AC2: All 5 sections present with Pinder-specific content

All 7 YAML keys (`name`, `vision`, `world_description`, `player_role_description`, `opponent_role_description`, `meta_contract`, `writing_rules`) must be present. Note: while the issue AC says "5 sections", the contract specifies 7 keys — `name` and `writing_rules` are in addition to the 5 content sections (vision, world, player role, opponent role, meta contract). All values must contain substantive, Pinder-specific content — not placeholder text or generic game design boilerplate.

### AC3: `GameDefinition.LoadFrom` can parse it successfully

The YAML must be valid and parseable by `GameDefinition.LoadFrom(string yamlContent)` as defined in the #543 contract. Specifically:
- Valid YAML syntax (no tabs, correct indentation)
- All 7 top-level keys present as strings
- Multi-line values use block scalar `|` syntax
- No nested objects or arrays — all values are scalar strings
- UTF-8 encoding, no BOM

### AC4: Content reads as genuine creative direction, not boilerplate

Each section must:
- Reference Pinder-specific concepts (sentient penises, stat names, shadow names, interest states, dating app framing)
- Use the game's tonal voice (comedy with emotional stakes)
- Be actionable by an LLM reading it as system prompt context
- Be internally consistent with `rules-v3.md` and `character-construction.md`

### AC5: Build clean (file is data, not code)

No C# code changes are required for this issue. The file is a pure data artifact. The build remains clean because no `.csproj` references this file.

---

## Edge Cases

### Empty or whitespace-only values

Not permitted. Every key must have at least one non-whitespace line of content. `GameDefinition.LoadFrom()` (implemented in #543) is expected to throw `FormatException` if any key is missing or empty.

### YAML special characters in content

Content may include colons (`:`), quotes, hyphens, and other characters common in English prose. Using block scalar `|` syntax avoids the need to escape these. Implementer must ensure no bare `:` on a line that could be misinterpreted as a YAML key — the `|` block scalar prevents this.

### Trailing newlines

YAML block scalar `|` includes a final trailing newline. This is expected behavior and `GameDefinition.LoadFrom()` should handle it (trim or preserve — that's #543's concern, not this issue's).

### Line length

No hard line-length limit, but lines should be wrapped at ~80–100 characters for readability in editors. This is a style guideline, not a parsing requirement.

### Character encoding

UTF-8 without BOM. No emoji in the YAML file itself (emoji usage is described in writing_rules but the rules text uses words, not emoji characters).

---

## Error Conditions

Since this issue produces a data file (not code), error conditions relate to file validity:

| Condition | Expected Outcome |
|-----------|-----------------|
| Missing key (e.g., no `meta_contract`) | `GameDefinition.LoadFrom()` throws `FormatException` — but this is a #543 concern. This issue must ensure all 7 keys are present. |
| Invalid YAML syntax (tabs, bad indentation) | YAML parser throws. This issue must produce valid YAML. |
| File not found at expected path | `GameDefinition.PinderDefaults` is used as fallback by #543. This issue must place the file at the correct path. |
| Value is generic/placeholder text | Fails AC4 (content quality). Reviewer rejects. |

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Issue #543 (`GameDefinition.LoadFrom`) | Consumer — this file is parsed by #543's implementation | In this sprint (Wave 3) |
| `rules-v3.md` (external design doc) | Content source — game rules and mechanics referenced in the YAML | Exists at `design/systems/rules-v3.md` |
| `character-construction.md` (external design doc) | Content source — character assembly, prompts, texting style | Exists at `design/systems/character-construction.md` |
| `risk-reward-and-hidden-depth.md` (external design doc) | Content source — combos, callbacks, tells, momentum | Exists at `design/systems/risk-reward-and-hidden-depth.md` |
| `async-time.md` (external design doc) | Content source — multi-session, time-of-day, delay penalties | Exists at `design/systems/async-time.md` |

No code dependencies. No build dependencies. This is Wave 1 (no dependencies on other sprint issues).

---

## Implementation Notes

- This issue is **pure content authoring** — no C# code changes.
- The implementer should read `design/systems/rules-v3.md`, `design/systems/character-construction.md`, and `design/systems/risk-reward-and-hidden-depth.md` thoroughly before writing content.
- The file must be parseable by a standard YAML parser (YamlDotNet 16.3.0 as specified in the #543 contract).
- The `GameDefinition.PinderDefaults` hardcoded fallback in #543 should contain equivalent content — but this YAML file is the canonical, editable source.
- Validate the file with any YAML linter (e.g., `python3 -c "import yaml; yaml.safe_load(open('game-definition.yaml'))"`) before committing.
