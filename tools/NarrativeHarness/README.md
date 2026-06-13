# NarrativeHarness (#843)

A **rules-free narrative testbed** for Pinder. It drives ONLY the production
prompt assembly + the real Anthropic transport — **no** roll / shadow /
horniness / weakness / interest-delta / misfire code is touched. The project
does not reference `Pinder.Rules` (it flows in transitively via
`Pinder.LlmAdapters`, but no rule type is ever constructed or called).

A turn is exactly: build the system prompt with
`SessionSystemPromptBuilder.BuildDatee(...)` (the `== CONVERSATION ARC ==`
slot populated by the harness) → `AnthropicTransport.SendAsync(...)` → record
the raw output. That's it.

## The experiment

The harness injects a single **static narrative prompt** (from
`data/prompts/narrative.yaml`) plus the **confession menu** into the
`== CONVERSATION ARC ==` slot on every turn. The prompt is verbatim and
editable: open `data/prompts/narrative.yaml`, change the `narrative_prompt`
block, and re-run. No strategy selection, no polarity toggle — the character
model receives the same arc context each turn and self-selects opportunistically.

## The confession menu (reusable — #842 consumes it)

`ConfessionMenu.Build(name, stake, background)` parses a character's stored
`PsychologicalStake` bullet list into structured `ConfessionEntry` records, each
with:

- a **theme** one-liner,
- detected **named/dated anchors** (people, weekdays, years, times, places),
- a heuristic **depth score** → `ConfessionDepth` band.

**Depth heuristic (documented, inspectable, NOT ground truth):** Raw
shame/vulnerability cue = +3, Tender body/intimacy cue = +2, Light
deflection/mild-embarrassment cue = −1, floored at 0. Bands: `0-1 Light`,
`2-3 Tender`, `4+ Raw`. The menu is printed at the top of every transcript so
its derivation from this character's stake is fully auditable.

The confession menu is appended to the static narrative prompt and injected
together into the arc slot each turn.

`ConfessionMatcher.Detect(...)` is a **best-effort, heuristic** post-hoc matcher
that guesses which confession(s) an utterance drew on by keyword/anchor overlap.
It is labelled HEURISTIC everywhere — not ground truth.

## Flags

```
--character <slug>         DATEE character to load, e.g. brick, velvet (default: brick)
--pursuer-character <slug> OPTIONAL second real character driven as the pursuer
                           (default: none → see fallback below)
--turns <n|range>          e.g. 14 or 10-20 (range → high end, or seeded) (default: 14)
--player-script <file>     scripted pursuer lines (one per line; # = comment);
                           alternative to the LLM pursuer
--seed <int>               seed for range resolution
--out <file>               output markdown path (default: narrative-harness-out.md)
```

### `--pursuer-character <slug>` — a real second character as the pursuer

By default the pursuer side is *not* a real Pinder character: it is either a
scripted reader (`--player-script`) or a generic lightweight LLM persona.
`--pursuer-character <slug>` instead loads a **second real Pinder character**
and drives it as the pursuer through the **SAME production prompt path** as the
datee — `SessionSystemPromptBuilder.BuildDatee(...)` with its own
assembled system prompt — so it **stays in character for the whole transcript**.

`--character` remains the **datee** character.

Precedence / back-compat: when `--pursuer-character` is set it takes precedence;
when it is omitted the pursuer falls back to `--player-script` (if given) or the
generic lightweight LLM persona, exactly as before.

The pursuer side is **REACTIVE**: it receives **no** arc injection
(it is built from the base `GameDefinition`, with no `== CONVERSATION ARC ==`
slot populated). Only the **datee** side gets the narrative prompt + confession
menu, so the datee's arc stays the single independent variable.

## Build & run (no host dotnet — use the cached SDK container)

```bash
# Build (Release):
docker run --rm -v "$PWD":/build -w /build mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet build tools/NarrativeHarness/NarrativeHarness.csproj -c Release

# Run against the real adapter (claude-opus-4-8, dashed — ctor maps to API id):
KEY=$(grep -E '^ANTHROPIC_API_KEY=' /root/projects/pinder-web/.env | cut -d= -f2-)
docker run --rm --network host -e ANTHROPIC_API_KEY="$KEY" \
  -v "$PWD":/build -w /build mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run --project tools/NarrativeHarness -c Release -- \
  --character brick --turns 12 \
  --out /build/out.md
```

### Two real characters (datee vs. pursuer)

Drive a second real character as the pursuer through the same production prompt
path. The datee (`--character`) still carries the arc; the pursuer
(`--pursuer-character`) is reactive:

```bash
dotnet run -- --character brick --pursuer-character velvet --turns 10
```

## Editing the narrative prompt

The injected arc text lives in `data/prompts/narrative.yaml` under the key
`narrative_prompt`. Edit it freely — the harness loads it at startup via
`NarrativePromptLoader` (honors `PINDER_DATA_PATH`, walks up from the base dir).
No recompile needed; just rerun.
