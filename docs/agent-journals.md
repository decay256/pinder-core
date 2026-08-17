# Agent Sessions, Snapshots, and Journals

This document defines the canonical vocabulary and architectural boundary for
Pi-backed LLM state and diagnostics in Pinder. Agents and tickets must use these
terms consistently. Do not use the bare term **session log**: it has historically
referred both to a complete game conversation and to LLM execution history.

## Canonical Terminology

| Term | Meaning |
|---|---|
| **Game Run** | One complete Pinder playthrough, including game mechanics, turns, events, progression settlement, and player-visible conversation. Existing `GameSession` code and `game_sessions` storage retain their names for compatibility, but new architecture prose should say Game Run. |
| **Game Run Timeline** | The player/game-oriented projection of a Game Run. It contains conversation entries, selected options, rolls, effects, interest changes, and other gameplay events. It is not an Agent Journal. |
| **Agent Session** | One Pi-managed LLM context with its own entry tree and active leaf. Current examples include the DATEE and avatar. Private analysis branches are also Agent Sessions while they exist. Future roles may add other Agent Sessions without changing this definition. |
| **Agent Snapshot** | A versioned serialized representation sufficient to restore one Agent Session. A snapshot is a resumable state artifact, not a complete execution audit. |
| **Agent Journal** | The read-only, inspectable entry history materialized from an Agent Snapshot plus any durable Pinder extension records. This is the object visualized by the Agent Journal Debugger. |
| **Agent Journal Bundle** | The Game Run-scoped read model containing all available Agent Journals, deleted/private branch records, and their cross-session invocation correlations at one persisted game-state revision. This is the default object opened by the debugger. |
| **LLM Invocation** | One actual provider request attempt, including its phase, model settings, exact input documents, output or error, validation result, usage, and correlation identifiers. Retries are separate invocations, including retries of the same game turn. |
| **Prompt Provenance** | Versioned annotations mapping ranges of a compiled LLM input document to configuration sources, keys, runtime values, and configuration revisions. |
| **Agent Journal Debugger** | The read-only administrative viewer for annotated Agent Journals. It does not resume, mutate, delete, or execute Agent Sessions. |

## North Star

At any point in a Game Run, an authorized diagnostic host can take the currently
persisted Agent Snapshots, materialize their Agent Journals, combine them with
durable Pinder extension records into an Agent Journal Bundle, and render enough
information to explain:

1. what each Agent Session knew at each active or historical branch;
2. what exact system and user documents each LLM Invocation received;
3. which configured prompt fragments and runtime values produced each range;
4. what the model returned, including rejected retries and failures;
5. which branch or result was adopted, discarded, or deleted; and
6. how an authorized administrator can navigate to the relevant configuration.

Materializing a journal must never alter the Agent Snapshot or trigger an LLM
call. Inspection remains possible after process restart and after a private Pi
branch has been deleted from the active store.

## Architectural Boundary

`Pi.Agent.Core` remains an upstream-compatible generic dependency. Pinder extends
it through composition over its public session interfaces and versioned Pi
`CustomEntry` records. Pinder must not add fields to Pi `MessageEntry`, fork the
Pi public API for game-specific concepts, or inject diagnostic custom entries
into provider context.

The Pinder adaptation layer owns typed extension contracts such as:

- `pinder.llm-invocation.v1` for exact invocation inputs and attempt metadata;
- `pinder.llm-result.v1` for output, usage, usage completeness, validation, and terminal status; and
- `pinder.message-link.v1` for connecting accepted semantic entries to the
  invocation that produced them.

These schema names are immutable wire names. The checked-in CLR contracts remain
provider-neutral under `Pinder.Core.Diagnostics.AgentJournals` and target
`netstandard2.0`/C# 8. Canonical JSON fixtures use `snake_case`, deterministic
property ordering, and additive-field tolerance.

Unknown future `pinder.*` custom-entry versions must be preserved as bounded
opaque JSON with a compatibility warning. They must never activate arbitrary CLR
types or be silently discarded.

## Snapshot-to-Journal Materialization

The canonical materializer accepts an Agent Snapshot and returns a normalized,
read-only journal document. It must use Pi's codec and entry semantics rather
than reimplementing parentage or context reconstruction in a UI. Pinder custom
entries are decoded by a registry keyed by `customType`; unknown custom entries
remain visible as opaque, safe JSON instead of being discarded.

An Agent Snapshot alone cannot describe every failed or deleted operation. The
host may therefore join it with durable extension records using stable Game Run,
Agent Session, entry, invocation, operation, request, turn, and branch IDs. An
invocation ID identifies exactly one provider call. Live diagnostics and persisted
records for that same call share the ID and may be deduplicated; separate retries,
including engine-level retries of the same turn, must never share it. One
logical record may have a snapshot projection and a durable host projection, but
both must originate from the same typed record construction path.

## Usage Accounting

Durable results expose `usage_status`: `complete` means all provider-neutral input,
output, cache-creation, cache-read, and total fields are canonical for that call;
`unavailable` means accounting could not be observed without affecting gameplay;
`incomplete` is reserved for known partial accounting; and missing/`unknown` denotes
historical records written before the signal existed. Newly emitted records must
always choose an explicit status. In particular, a configured skip that makes no
provider request records `unavailable` with null usage rather than fabricating zero
token accounting. Cumulative transport usage is canonical for one invocation only
when the measured call-count delta is exactly one and all provider-neutral token
fields are present. A zero-call delta is `unavailable`; a delta above one is
`incomplete` because overlapping or cumulative activity cannot be attributed to one
provider invocation. The observed aggregate tokens remain attached to an incomplete
record for diagnosis, but must not be treated as canonical per-call accounting.
Conversation calls, ordinary one-shots, and setup one-shots use the same classifier.
The recorder also normalizes omitted status from source-compatible legacy completion
overloads: null usage becomes `unavailable`, and old three-field usage becomes
`incomplete`, so the live emission boundary never writes `unknown`.

Setup generators may accept a caller-supplied diagnostic call ID. When an Agent
Journal attempt owns the provider call, the generator passes that invocation ID so
the live start/terminal diagnostics and durable invocation/result are exact mirrors.
Callers that do not own a durable journal attempt continue to receive a generated ID.

## Prompt Provenance Contract

Prompt provenance is captured when an invocation input document is constructed,
not reconstructed later by matching strings. Each annotated range needs:

- document role/kind, start offset, and exclusive end offset;
- canonical source kind, source file or catalog identity, and key path;
- configuration revision or content hash;
- exact runtime/generated classification for non-configured text; and
- an optional logical editor target, resolved through an allowlisted host API.

Legacy `PromptTraceResult` source paths cross an explicit caller-supplied
source-identity resolver before contract construction. The resolver maps a
known source path to a registered opaque identifier; unmapped paths fail, and
the adapter neither serializes the path nor generates a replacement host ID.

The exact compiled document remains the historical truth. If current
configuration no longer matches the recorded revision, the debugger shows drift
instead of pretending the current text produced the historical invocation.

Offsets are UTF-16 code-unit offsets: zero-based start inclusive and end
exclusive, matching .NET string indexing. Ranges within a document are complete,
ordered by start/end, non-overlapping, reject zero-length segments, and cannot
split a UTF-16 surrogate pair. Adjacent ranges are valid. Unannotated text must be serialized as explicit
`runtime_generated` ranges rather than inferred later.

## Ownership and Non-Goals

- Pinder Core owns provider-neutral journal contracts, provenance production,
  Pi composition, snapshot materialization, and tests proving context isolation.
- A host such as Pinder Web owns durable journal storage, authorization,
  retention, redaction, configuration-link resolution, and HTTP projection.
- A viewer owns read-only rendering and custom-renderer registration.
- The Game Run Timeline remains the gameplay/replay source. It is not replaced by
  the Agent Journal Debugger.
- Agent Journals are not an excuse to retain secrets, provider credentials, or
  unrestricted filesystem paths.
- Source identities and editor targets are opaque allowlisted identifiers. Raw
  filesystem paths, arbitrary URLs, provider credentials, bearer tokens, and
  cookies are forbidden contract values.
- Diagnostic custom entries must contribute zero provider-context messages/text.

## Required Verification

Changes in this area must include focused regressions proving snapshot round
trip, custom-entry version handling, unknown-entry preservation, prompt-range
accuracy, and zero diagnostic-entry contribution to provider context. Wiring
changes additionally require integration coverage for retries and disposable
private branches before stale diagnostics are removed downstream.
