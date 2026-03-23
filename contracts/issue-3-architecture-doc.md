# Contract: Issue #3 — Architecture Doc (Rules-to-Code Sync)

## Component
`docs/architecture.md` — pure documentation, no code changes.

## Maturity
Prototype

## What It Produces
A markdown file at `docs/architecture.md` containing:

1. **Architecture overview** — module map, data flow, key patterns
2. **Rules-to-Code Sync table** — every C# constant mapped to its rules section
3. **Drift detection process** — how to check and update when rules change
4. **Known gaps** — what rules sections have no code yet

## Interface
This is a documentation artifact. No programmatic interface.

**Consumers**: All developers working on Pinder.Core, future agents updating constants.

## Acceptance Criteria (from issue)
- `docs/architecture.md` exists
- Rules-to-Code Sync section covers all sync-table items
- Clear enough for a new developer to keep rules and code in sync

## Dependencies
- None (issues #1 and #2 are already merged)

## NFR
- Prototype: no latency target (documentation only)
