# Agent Journal Invocation Ownership

This document is the checked-in review note for
`contracts/agent-journal-invocation-ownership.v1.json`. It uses the terminology
from `docs/agent-journals.md` and the merged Core #1370 contracts. The manifest
is deliberately closed: wiring work must consume these rows and must not choose
new ownership, visibility, or retention classifications while coding.

## Contract Shape

Each row fixes:

- lifecycle owner and journal destination;
- whether a Pi Agent Session exists for the invocation;
- provider-context membership and player-delivery visibility;
- required owner and correlation identifiers;
- retention-policy key;
- provenance builder identifiers;
- implementation matchers for the static verifier; and
- status, status evidence, activation rule, and verifier group.

The accepted status values are `live_production`,
`provider_capable_dormant`, and `dead_with_proof`. This v1 approves no dead
rows. `game.datee.interest-change-beat` is provider-capable dormant, not dead.

## Closed V1 Inventory

| ID | Status | Owner / destination | Pi Agent Session |
|---|---|---|---|
| `game.datee.performance` | `live_production` | Game Run Bundle / DATEE journal | yes |
| `game.avatar.reply` | `live_production` | Game Run Bundle / avatar journal | yes |
| `game.avatar.emotional-director` | `live_production` | Game Run Bundle / private avatar branch | yes, disposable branch |
| `game.emotional-director` | `live_production` | Game Run Bundle / private DATEE branch | yes, disposable branch |
| `game.dialogue-options` | `live_production` | Game Run Bundle / one-shot record | no |
| `game.setup.dramatic-arc` | `live_production` | Game Run Bundle / setup one-shot | no |
| `game.prefetch.option-branch` | `live_production` | Game Run Bundle / branch-local DATEE/avatar journals | yes, restored from cloned Game State snapshots |
| `game.speculation.option-branch` | `live_production` | Game Run Bundle / branch-local DATEE/avatar journals | yes, restored from cloned Game State snapshots |
| `character.synthesis` | `live_production` | character-creation operation journal | no Game Run journal |
| `admin.temporary-chat` | `live_production` | admin authoring execution / temporary-chat owner | separate temporary-chat owner |
| `admin.prompt-speculation` | `live_production` | admin authoring execution / authoring owner | separate authoring owner |
| `narrative.harness` | `live_production` | Narrative Harness run | harness-owned |
| `session.simulation` | `live_production` | simulation run | simulation-owned |
| `game.delivery.success-improvement` | `live_production` | Game Run Bundle / one-shot record | no |
| `game.delivery.horniness-question` | `live_production` | Game Run Bundle / one-shot append record | no |
| `game.delivery.steering-question` | `live_production` | Game Run Bundle / one-shot append record | no |
| `game.datee.interest-change-beat` | `provider_capable_dormant` | no runtime journal wiring until re-planned | no runtime journal |

## Dormant Activation Guard

`game.datee.interest-change-beat` remains implemented by
`PinderLlmAdapter.GetInterestChangeBeatAsync`, but the current production code
has no caller. The verifier searches `src`, `session-runner`, and `tools` for
`GetInterestChangeBeatAsync(` and permits only:

- `src/Pinder.Core/Interfaces/ILlmAdapter.cs`;
- `src/Pinder.Core/Conversation/NullLlmAdapter.cs`; and
- `src/Pinder.LlmAdapters/PinderLlmAdapter.cs`.

Any new production caller fails verification. Reactivation requires planning
before journal wiring; wiring workers must not silently turn the dormant row
into a live row.

## Web-Owned Review

web-review:admin.temporary-chat

`admin.temporary-chat` is host-owned by Pinder Web/admin authoring. Core records
the ownership row because wiring consumers need the closed matrix, but Core does
not implement, reclassify, or assign Game Run identifiers to this path. The row
requires a `temporary_chat_id` and `admin_execution_id`; `game_run_id` and
`agent_session_id` are explicitly forbidden.

web-review:admin.prompt-speculation

`admin.prompt-speculation` is host-owned by Pinder Web/admin authoring. Core
records the row for the closed matrix only. The row requires an
`authoring_execution_id` and `admin_execution_id`; `game_run_id` and
`agent_session_id` are explicitly forbidden.

## Boundary Notes

Game Run rows may require `game_run_id`; non-Game-Run rows must not. Provider
calls are not automatically Agent Sessions. One-shot delivery/setup/dialogue
rows must not mint `agent_session_id`; DATEE/avatar rows must use the relevant
Agent Session or branch identifiers; branch rows must keep branch-local
correlations.

`character.synthesis` groups current character creation and synthesis provider
paths under the character-creation operation journal. It is not a Game Run
Agent Journal Bundle member.

`narrative.harness` and `session.simulation` are live provider-capable paths,
but they are owned by their run types rather than by a Game Run Bundle.

## Verification

Run:

```powershell
pwsh ./scripts/verify-agent-journal-ownership.ps1
```

The verifier checks the exact 17-ID inventory, row field completeness, live /
dormant / dead counts, implementation matcher counts, the dormant no-caller
proof, static scan coverage, focused xUnit tests, and `git diff --check`.
