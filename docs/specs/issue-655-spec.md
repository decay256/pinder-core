**Module**: docs/data-architecture.md (create new)

## Overview
This issue introduces a new architectural documentation file, `docs/data-architecture.md`, which details the two-tier data model of Pinder.Core (Rules DSL vs Configuration Data) and its extensibility approach. It also updates the existing `docs/architecture.md` to cross-reference this new document, providing clarity on how game mechanics and content are loaded, extended, and maintained.

## Function Signatures
N/A - This is a documentation-only feature. No code changes are required.

## Input/Output Examples
N/A - Documentation artifact only.

## Acceptance Criteria

### 1. docs/data-architecture.md exists and covers all five sections
The file `docs/data-architecture.md` must be created and structured with the following five sections:
- **1. Two-tier data model**: Detail the difference between Tier 1 (Rules DSL, enforced mechanics, loaded via `Pinder.Rules.RuleBook`) and Tier 2 (Configuration data, runtime-loaded via `JsonItemRepository` and `GameDefinition.LoadFrom`).
- **2. Configuration data files**: Provide a directory tree mapping `data/` including `game-definition.yaml`, `traps/traps.json`, `items/starter-items.json`, and future paths like `archetypes/` and `anatomy/`.
- **3. Extensibility model**: Explain how collections (items, archetypes) can be extended without code changes via JSON/YAML schema validation, whereas singleton configs (`game-definition.yaml`) require code updates to `GameDefinition`.
- **4. What the LLM receives**: Describe the system prompt assembly pipeline, including `SessionSystemPromptBuilder`, `GameDefinition` injection, and `CharacterProfile` (items + anatomy).
- **5. How to extend**: Provide concrete steps for adding a new item, archetype, trap, or changing game direction.

### 2. Consistent with current code (verify paths and loader class names)
The document must accurately reflect the existing codebase paths and classes, specifically:
- `Pinder.Rules.RuleBook`
- `JsonItemRepository`
- `GameDefinition.LoadFrom`
- `SessionSystemPromptBuilder`
- `CharacterAssembler`
- `data/game-definition.yaml`
- `data/traps/traps.json`
- `data/items/starter-items.json`
- `rules/extracted/rules-v3-enriched.yaml`

### 3. Cross-referenced from docs/architecture.md
The existing `docs/architecture.md` must be updated. A link or reference to `docs/data-architecture.md` must be added in the appropriate place (e.g., in a "Data" or "Architecture Documents" section) to ensure developers can discover it.

## Edge Cases
- Ensure paths in the documentation are relative to the project root and accurate based on the current Sprint 14 state.
- Handle future modules explicitly (e.g., marking `archetypes/` and `anatomy/` as future integration points if they aren't fully integrated yet).

## Error Conditions
N/A

## Dependencies
- Must align with the current architecture (Sprint 14) which relies on stateless LLM interactions and Dependency Injected configuration loading.
