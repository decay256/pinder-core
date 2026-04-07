# Contract: Issue #655 — Data Architecture Document

## Component
`docs/` (documentation)

## Description
A new markdown file `docs/data-architecture.md` that explicitly defines the system's data management strategy, split into two tiers:
1. Rules DSL (engine-enforced mechanics, generated from MD)
2. Configuration data (runtime-loaded, source of truth)

It must explain the extensibility model (extensible collections vs singleton config), detail what the LLM receives, and give instructions on how to extend the game data (items, archetypes, traps, game direction).

## Interface
- **File**: `docs/data-architecture.md`
- **Link**: Must be linked from `docs/architecture.md`
- **Format**: Markdown, adhering to the headings and outline specified in the issue.
