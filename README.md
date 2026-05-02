# Pinder.Core

Pure C# RPG engine for [Pinder](https://github.com/decay256/pinder) — the comedy dating RPG.

Targets **netstandard2.0** / **C# 8.0**. Zero external dependencies in the core library.

## Assemblies

| Project | Target | Dependencies | Purpose |
|---|---|---|---|
| `Pinder.Core` | netstandard2.0 | None | Domain model, game loop, roll engine, stats, traps, XP, combos |
| `Pinder.Rules` | netstandard2.0 | Core, YamlDotNet | Data-driven rule resolution from YAML |
| `Pinder.LlmAdapters` | netstandard2.0 | Core, Rules, Newtonsoft.Json | LLM prompt assembly + API integration (Anthropic, OpenAI) |
| `session-runner` | net8.0 | Core, LlmAdapters | CLI harness for automated playtesting |

## Roll Formula

```
d20 + statModifier + levelBonus + externalBonus >= DC
DC = 16 + opponent's defending stat modifier
```

### Fail Tiers (miss margin = DC − roll)

| Condition | Tier |
|---|---|
| Nat 1 | Legendary |
| 1–2 | Fumble |
| 3–5 | Misfire |
| 6–9 | Trope Trap |
| 10+ | Catastrophe |

### Success Scale (beat margin = roll − DC)

| Beat by | Interest delta |
|---|---|
| 1–4 | +1 |
| 5–9 | +2 |
| 10+ | +3 |
| Nat 20 | +4 |

## Running Tests

```bash
dotnet test                              # All tests
dotnet test --filter "Category=Core"     # Core game logic (fast)
dotnet test --filter "Category=Rules"    # Rules pipeline + YAML resolution
dotnet test --filter "Category=LlmAdapters"  # Prompt builder + adapter tests
```

## Rules Pipeline

Python tooling in `rules/tools/` converts between YAML and Markdown rule representations:

```bash
python3 rules/tools/rules_pipeline.py check       # Round-trip verification
python3 rules/tools/rules_pipeline.py yaml-to-md   # YAML → Markdown
python3 rules/tools/rules_pipeline.py md-to-yaml   # Markdown → YAML
```

Run `dotnet test --filter "Category=Rules"` after any change to YAML rule files or `Pinder.Rules` code.

## Documentation

- **[Architecture](docs/ARCHITECTURE.md)** — assemblies, game loop, interfaces, data files, constraints
- **[Data Architecture](docs/data-architecture.md)** — two-tier data model, extensibility
- **[Unity Integration Guide](docs/unity-integration.md)** — dropping pinder-core into a Unity project; adapting `IAnatomyRepository` / `IItemRepository` when your assets, anatomy parameters, or stat ranges differ from the shipped defaults
- **[Rules Tools](rules/tools/README.md)** — pipeline commands and YAML enrichment

## Project Structure

```
src/Pinder.Core/           # Domain kernel (zero deps)
src/Pinder.LlmAdapters/   # LLM integration
src/Pinder.Rules/          # YAML rule engine
session-runner/            # CLI playtesting harness
tests/                     # Test projects per assembly
data/                      # YAML + JSON game data
rules/tools/               # Python rules pipeline
design/                    # Design docs + prompt examples
docs/                      # Architecture + data docs
```
