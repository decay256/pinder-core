# Tone lab: simulating a whole Pinder chat in one model call

The tone lab answers one question: what is the smallest prompt that makes a model
write a good, funny Pinder chat in one pass, while the game's rules decide every
outcome? It replaces the earlier "lightweight session simulation" approach. That
approach worked without the rules and gave the model two seed facts per character;
it was abandoned in September 2026.

The goals the output is judged against are in
[dialogue-experience-goals.md](dialogue-experience-goals.md). The approaches already
ruled out, and the failure patterns seen so far, are in
[simulation-requirements-and-gap-analysis.md](simulation-requirements-and-gap-analysis.md).

## How it is split

A run has three inputs, and each lives in exactly one place.

**Rules: the engine, never the prompt tool.** `session-runner --mechanics-only` plays
the session with the real `GameSession` and a model-free adapter. Option draws,
rolls, failure tiers, horniness, shadow and steering checks, traps, combos and interest
are all the engine's own, and they are written per turn to `mechanics.json`. With
`--seed` and `--clock` a run is fully reproducible. The prompt tool only reads that
file, so no prompt change can change a die. The previous Python simulator kept its
own copy of the rules. That copy had drifted from the engine: a fixed DC of 15 instead
of 16 plus the datee's defending stat, failure penalties by miss margin instead of by
tier, a made-up trap penalty, and a "lost at the turn cap" outcome that the game does
not have.

**Characters: the bundled character files.** The card for each character is filled from
`data/characters/<slug>.json`: the bio, the consolidated personality, a few
claim-versus-truth pairs from `backstory_categories`, the stake lines, the diagnosed
defence and how they react to each stat. Which fields appear, and how many, is decided
by the variant. Nothing is invented at run time.

**Writing: the variant.** `tools/tonelab/variants/<name>/` holds everything that shapes
the prose:

| File | Role |
| --- | --- |
| `prompt.md` | The whole user prompt: framing, ground rules, arc, output format |
| `character.md` | The card used for each character |
| `turn.md` | The brief for one turn, repeated for every turn the engine played |
| `system.md` | The system prompt |
| `variant.json` | Knobs: `model`, `turns` (turn cap), `secrets` or `secret_categories`, `stake_lines` |

Templates use `${name}` placeholders. An unknown placeholder stops the run and lists the
available ones. The per-turn brief reuses the game's own consequence text from
`data/i18n/en/consequences.yaml`, so a misfire is described the way the game describes it.

## Running it

```sh
tools/tonelab/tonelab.py run --player reuben --datee brick --seed 42
tools/tonelab/tonelab.py run --variant my-variant --seed 42 --dry-run   # prompt only, no model call
tools/tonelab/selftest.py                                              # reproducibility and rendering check, no model call
```

The model is called once, through the subscription Claude CLI in print mode. The call
has no tools, no settings files and a neutral working directory, so only the prompt
reaches the model. API credentials are stripped from the child environment.

Each run is saved under `docs/tone-lab/runs/<utc>-<variant>-<player>-<datee>-s<seed>/`:

| File | Contents |
| --- | --- |
| `mechanics.json` | What the engine decided, turn by turn |
| `prompt.md` | The exact system and user prompt sent |
| `output.md` | The chat as returned |
| `run.json` | Variant file hashes, knobs, prompt hash, repo commit, seed, clock, outcome, the model **the CLI reports** it used, timing, token usage and simple format checks |

The model in `run.json` comes from the CLI's own usage report, not from a default label.
The earlier simulator labelled every run "Sonnet" whatever actually ran.

## Experimenting

1. Copy `variants/baseline` to a new directory and change one thing.
2. Run it on the same seeds as the baseline. The dice then stay fixed, and the only
   difference between two runs is the variant. A single seed has been ruled out as
   evidence, so use a few seeds that give different arcs (a win, a long grind, a loss).
3. Read the outputs side by side against the experience goals.

To change the arc itself (start interest, turn cap, the time of day and so the
horniness band), change the run parameters, not the prompt. To change what the arc
*means* for the writing, change the arc section of `prompt.md`.

## What the mechanics export cannot show

Some mechanics come from model output in the real game: tells, weakness windows,
callbacks and datee signals. A model-free run cannot produce them, so they never fire in
the tone lab. Interest from those sources is missing, and arcs run slightly harder than
in a played session. The player's choices come from the runner's heuristic scoring
agent, not from a person.
