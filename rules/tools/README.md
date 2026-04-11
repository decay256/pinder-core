# Rules Pipeline Tools

Single CLI entry point: `python3 rules/tools/rules_pipeline.py <command>`

## Commands

| Command | Description |
|---------|-------------|
| `extract` | Extract structured YAML from design Markdown |
| `enrich` | Run enrichment pass (add condition/outcome fields) |
| `yaml-to-md` | Generate Markdown from enriched YAML |
| `md-to-yaml` | Parse Markdown back to YAML |
| `check` | Round-trip verification (YAML→MD→YAML), report diff count |
| `check-diff` | Round-trip + LLM classification (FORMATTING_ONLY / CONTENT_LOSS) |
| `game-def` | Generate game-definition.md from YAML |
| `test` | Run all pipeline tests via pytest |

## Running Tests

```bash
# Python tests only
python3 rules/tools/rules_pipeline.py test

# C# integration tests (shells out to Python pipeline)
dotnet test --filter "Category=Rules"

# All tests (Core + Rules)
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj
```

## Test Categories

- **Core** — C# unit tests, always run
- **Rules** — Pipeline integration tests, run when rules files change. Requires `python3` + `pyyaml`. The `check-diff` test calls the Anthropic API when `ANTHROPIC_API_KEY` is set; skips gracefully without it.

Internal modules are prefixed with `_` (e.g. `_extract.py`) — import via `rules_pipeline.py`, don't call directly.
