# #1162 char-card cache-prefix reorg — orchestrator/architect design decision

Daniel pre-approved (2026-06-15) the NON-byte-preserving reorg (resolves deferred Q1 from #1154).

## Goal
Make the constant `character_card_framing` a stable cross-character cacheable PREFIX;
push all variable per-character data into a trailing block.

## Current (interleaved) output
```
RULES\n\n
IDENTITY\n- Gender identity: <g>\n- Bio: <b>\n\n
PERSONALITY\n<bullets>\n\n
BACKSTORY\n<bullets>\n\n
TEXTING STYLE\n<bullets>\n\n
ACTIVE ARCHETYPE\n<archetype data | (none resolved)>\n\n
EFFECTIVE STATS\n- Charm.. (6 stat lines)
[ \n\nACTIVE TRAP INSTRUCTIONS\n<instr> ]   (gated)
```

## Target (constant prefix → separator → variable suffix)
Constant prefix (identical for EVERY character — the cacheable block):
```
RULES
IDENTITY
PERSONALITY
BACKSTORY
TEXTING STYLE
ACTIVE ARCHETYPE
EFFECTIVE STATS
ACTIVE TRAP INSTRUCTIONS
```
(These are the 7 framing labels from `character_card_framing` PLUS the constant
`EFFECTIVE STATS` code label, emitted contiguously, one per line, no variable data
interleaved. This whole block is byte-identical across characters.)

Then a constant separator line marking the boundary:
```

=== CHARACTER DATA ===
```

Then the variable per-character block — each section re-labelled with its own
header so the LLM can still associate data with its section, followed by that
section's variable lines:
```
IDENTITY
- Gender identity: <g>
- Bio: <b>

PERSONALITY
<personality bullets>

BACKSTORY
<backstory bullets>

TEXTING STYLE
<texting bullets>

ACTIVE ARCHETYPE
<archetype data | (none resolved)>

EFFECTIVE STATS
- Charm: .. (6 lines)

[ ACTIVE TRAP INSTRUCTIONS\n<instr lines>  — gated on active traps ]
```

## Why this shape
- The leading framing block (8 lines) is a pure constant → identical prefix across
  characters → satisfies AC "constant prefix byte-identical across two characters".
- `RULES` lead-in keeps the `{name}` substitution? NO — `{name}` must NOT appear in the
  constant prefix (it is per-character). In the CURRENT code LeadIn = "RULES" with a
  `.Replace("{name}", displayName)` that is a no-op because the framing value is literally
  "RULES" (no {name} token). Keep the lead-in as the bare constant label in the prefix;
  the display name is already only emitted via IDENTITY's variable block (it is NOT in the
  current output at all except via the no-op replace). So no name leaks into the prefix.
- The two real conditions are PRESERVED: archetype-null → "(none resolved)" inside the
  variable ACTIVE ARCHETYPE block; trap block gated on activeTraps in the variable suffix.

## Test impact
- RE-BASELINE Issue1154 golden (golden_inactive.txt + golden_active.txt) to the NEW layout.
  Capture the new bytes from the NEW builder output and check them in verbatim.
- Issue1154 provenance test (FramingSpans) still holds: framing spans attributed to
  character_card_framing; EFFECTIVE STATS span has no key (unchanged).
- Issue833 + Issue836 `ExtractSection(header,nextHeader)` slice helpers: their header
  PAIRS (PERSONALITY→BACKSTORY, BACKSTORY→TEXTING STYLE, TEXTING STYLE→ACTIVE ARCHETYPE)
  must still hold WITHIN THE VARIABLE BLOCK. Because every header now appears TWICE
  (once in the constant prefix, once in the variable block), `IndexOf(header)` finds the
  FIRST (prefix) occurrence, and `IndexOf(nextHeader, afterHeader)` finds the next header
  AFTER it. Need to ensure these slices still capture the intended bullet bodies. The
  prefix block has the headers in the SAME relative order (PERSONALITY before BACKSTORY
  before TEXTING STYLE before ACTIVE ARCHETYPE), so the FIRST PERSONALITY→ next BACKSTORY
  slice would capture prefix text BETWEEN those two labels (empty — adjacent lines), NOT
  the bullets. THIS BREAKS the slice semantics. → Update Issue833/Issue836 ExtractSection
  callers to target the VARIABLE block occurrence (e.g. search starting after the
  "=== CHARACTER DATA ===" separator), OR change the helper to find the header occurrence
  that is followed by bullet data. Simplest robust fix: in those test helpers, offset the
  initial IndexOf to start searching AFTER the "=== CHARACTER DATA ===" marker. Fix the
  order asserts AND the inter-header slices in the SAME commit (the #1162 trap).
- Issue832 / Issue874 / CharacterSystemTests / CharacterDefinitionLoaderTests use
  order-agnostic Contains → survive (headers still present).
- SessionSystemPromptBuilderTests + Issue543_*.Builder.cs test the OUTER session prompt
  (literal spec string, not a built card) → NOT affected by the card reorg. The ticket
  named them out of caution; verify they still pass (they should, untouched).
