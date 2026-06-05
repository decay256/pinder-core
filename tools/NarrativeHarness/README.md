# NarrativeHarness (#843)

A **rules-free narrative testbed** for Pinder. It drives ONLY the production
prompt assembly + the real Anthropic transport — **no** roll / shadow /
horniness / weakness / interest-delta / misfire code is touched. The project
does not reference `Pinder.Rules` (it flows in transitively via
`Pinder.LlmAdapters`, but no rule type is ever constructed or called).

A turn is exactly: build the system prompt with
`SessionSystemPromptBuilder.BuildOpponent(...)` (the `== CONVERSATION ARC ==`
slot populated by the harness) → `AnthropicTransport.SendAsync(...)` → record
the raw output. That's it.

## The experiment

We're testing how to drive a dramatic arc as an **opportunistic gradient**
through a character's own 15 psychological-stake confessions, rather than a
pre-ranked climb. The arc-shape is a **strategy interface** (`IArcStrategy`):

- **`ingestion`** (primary hypothesis): inject ALL 15 confessions
  (pre-summarized as *text + depth*) into the arc slot every turn, plus a
  **soft bias** (early→lighter material, late→deeper) and a **register derived
  from confession depth** (far/light = guarded/playful; near/deep = raw/short).
  No per-turn selector LLM call — the character model self-selects.
- **`romcom`** (A/B control): an imposed rom-com 7-beat spine, proportionally
  spread over the turn count; each beat injects a directive.

`--polarity on` adds a direction-of-change nudge (ingestion: "let the emotional
temperature shift this phase"; romcom: the beat's authored polarity). `off`
injects no enforced direction. The flag visibly changes the injected text.

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

`ConfessionMatcher.Detect(...)` is a **best-effort, heuristic** post-hoc matcher
that guesses which confession(s) an utterance drew on by keyword/anchor overlap.
It is labelled HEURISTIC everywhere — not ground truth.

## Flags

```
--character <slug>      e.g. brick, velvet (default: brick)
--arc-shape <shape>     ingestion | romcom (default: ingestion)
--polarity <on|off>     enforce per-beat direction-of-change (default: off)
--turns <n|range>       e.g. 14 or 10-20 (range → high end, or seeded) (default: 14)
--player-script <file>  scripted pursuer lines (one per line; # = comment);
                        alternative to the LLM pursuer
--seed <int>            seed for range resolution
--out <file>            output markdown path
```

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
  --character brick --arc-shape ingestion --polarity off --turns 12 \
  --out /build/out.md
```

## Samples

See `samples/`:
- `ingestion-brick-12turn.md` — primary hypothesis, real 12-turn run on Brick.
- `romcom-brick-polarity-on-6turn.md` — control shape + polarity toggle.
- `ingestion-velvet-6turn.md` — menu generator reused on a second character.
