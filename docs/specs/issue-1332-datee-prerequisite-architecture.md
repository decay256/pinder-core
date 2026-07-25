# Issue 1332: DATEE Prerequisite Architecture Decisions

This record separates verified current behavior from target guarantees for the
DATEE emotional-reaction prerequisite sprint. It is an architecture contract,
not a feature implementation plan. It must not be used to invent a second LLM
session manager, prompt loader, model router, diagnosis DTO hierarchy, trace
retention policy, or transcript store.

## Scope

This document covers ownership and boundaries needed before adding DATEE
emotional direction:

- session and history ownership;
- prompt compilation and config ownership;
- model and transport ownership;
- required and best-effort failure behavior;
- diagnosis and relationship-state ownership;
- the shape of a future director-to-performance call.

Dependency mapping and production code changes belong in follow-up issues, not
in this record.

## Current State After #1334, #1335, #1348, and #1254

The findings below record the prerequisite review as it existed when #1332
was written. Subsequent implementation has resolved three relevant gaps:

- #1334 propagates resolver-first typed relationship states into
  `DialogueContext.CurrentInterestState`, `DateeContext.InterestBeforeState`,
  and the final post-delivery `DateeContext.InterestAfterState`. Shadow and
  horniness mutations no longer leave the DATEE prompt on an intermediate
  roll-stage relationship state.
- #1335 selects DATEE narrative and resistance prose by the canonical typed
  `InterestState` through semantic YAML keys. Interest 15 is `Interested`;
  interest 16 begins `VeryIntoIt`.
- #1348 keeps prior completed visible exchanges in
  `DateeContext.ConversationHistory` and represents the current delivered
  player message only through `PlayerDeliveredMessage`. The visible
  player/DATEE pair is appended transactionally after DATEE generation
  succeeds, so the current message appears once and failed generation commits
  neither side of the pair.

These are the active contracts. Later references in this document to the
pre-#1334 stale intermediate field, the pre-#1335 numeric prompt split, or the
pre-#1348 current-message duplication risk are historical findings.

- #1254 publishes creative configuration as validated immutable generations.
  Sessions and standalone operations capture one generation, and Anthropic
  request construction receives generation-captured stateful headings rather
  than reading mutable compatibility globals during each request.

## Historical #1332 Baseline

The sections below preserve the prerequisite review at the time #1332 was
written. Statements phrased as current or target describe that historical
baseline unless the active-state section above explicitly carries them
forward.

### Session and History

`GameSessionState.History` is the canonical gameplay transcript. It stores the
messages that actually happened in the round and is the source used to build
visible conversation context for later prompt calls.

`GameSessionState.DateeHistory` is a secondary semantic ledger for DATEE LLM
turns. `GameSessionState.AvatarHistory` is the symmetric compatibility ledger
for avatar-side LLM history. Both survive clone, snapshot, and resimulation, but
they are not the canonical gameplay transcript and are not permission
boundaries.

The active `IStatefulLlmAdapter` contract is engine-owned history passed as a
method argument. It is not provider-persistent session state. Implementations
must not retain DATEE-session fields across calls. The current
`PinderLlmAdapter` confirms this: it treats `DateeContext.ConversationHistory`
as authoritative and deliberately does not prepend `DateeHistory` to avoid
nested prompt growth.

### Role-Isolated Prompt Pipelines

"Avatar session" and "DATEE session" mean role-isolated prompt pipelines. The
avatar path builds the player-avatar system prompt and generates options or
avatar-side rewrites. The DATEE path builds the DATEE system prompt and
generates the reply. The phrase does not currently mean a provider-side
conversation object.

The prompt graph also enforces clean history: option generation and unchosen
options stay ephemeral; only the final delivered message is committed before
the DATEE response runs.

### Prompt and Adapter Ownership

`PinderLlmAdapter` owns game-level prompt compilation, parser selection,
semantic validation, semantic recovery, operation diagnostics, and fallback
behavior. `SessionSystemPromptBuilder` builds role-specific system prompts.
`SessionDocumentBuilder` builds operation-local user messages from typed
context objects such as `DialogueContext`, `DateeContext`,
`InterestChangeContext`, and overlay contexts.

`ILlmTransport` is the low-level provider-neutral wire boundary. Optional
`IStructuredLlmTransport` handles native structured output where available.
Transports know provider payload shape and response extraction; they do not own
game semantics.

### Model and Routing Ownership

Core receives injected `ILlmAdapter` and `ILlmTransport` instances. It does not
choose providers or models.

The web host owns production provider/model selection through
`LlmModelRouting` and `LlmProviderFactory`. `LlmModelRouting` maps
provider-qualified model specs to provider kind plus model id.
`LlmProviderFactory` builds provider transports, wraps them with shared
decorators such as thinking stripping, punctuation normalization, rate limiting,
snapshot capture, and usage capture, then creates the session-scoped
`PinderLlmAdapter`.

`LlmPhase` is observability identity and operation classification. It is not a
router.

### Config and Prompt Ownership

Prompt prose lives in `data/prompts/*.yaml` and is loaded through
`PromptCatalog`. `PromptWiring.Wire()` is the existing startup wiring point for
production and tests. New DATEE emotional-reaction prompts must use the same
catalog schema, named-placeholder substitution, source-file attribution, and
startup fail-fast behavior.

Runtime publication of prompt/catalog content is currently sequential and
static after wiring. Atomic publication for live admin reload is a target
guarantee, not a verified current fact.

Delivery and overlay instructions live in `data/delivery-instructions.yaml` and
are loaded as `StatDeliveryInstructions`. New emotional direction must not add
prompt prose in C# code.

### Failure Concepts

Four concepts are distinct and must remain distinct:

- Transport resilience: provider HTTP failures, 429 retry, cancellation,
  circuit breaking, and provider payload extraction.
- Semantic output recovery: invalid or malformed model output retry/recovery at
  the adapter contract level.
- Operation criticality: whether the operation is required and terminal or
  best-effort with an explicit degraded behavior.
- Turn transactionality: whether gameplay state mutations are committed or
  rolled back when a required operation fails or is cancelled.

Current DATEE response failure is required, but the turn is not yet
transactional. `ResolveTurnAsync` commits the delivered player message to
`GameSessionState.History` before `DateeResponseStage.ExecuteAsync()` calls the
DATEE LLM. If the required DATEE call fails after that mutation, the current
shape can leave mutated state behind. Transactionality is therefore a target
guarantee for the prerequisite sprint.

Best-effort operations already exist. For example, interest beats and overlays
may report degraded output and continue according to explicit fallback rules.
A required DATEE emotional director call must not silently degrade into the
performance call without validated direction.

### Diagnosis Ownership

Character diagnosis is currently stored as a generic string map. The active
runtime need is a small required contract, currently represented by
`derived_feeling` and `defense_reaction`, used by cognitive subtext and rendered
into character prompts.

Issue #1337 centralizes the required keys and stable validation violations in
`TherapistDiagnosisContract`. Unknown fields intentionally have different
behavior at each boundary:

| Boundary | Unknown-field behavior |
| --- | --- |
| LLM diagnosis generation | Normalizes key casing and surrounding whitespace, selects the canonical required fields, and discards generated extras. |
| Permissive character loader | Preserves string-valued extras in the flat map so legacy characters can be regenerated; non-string values are rejected. |
| Authored character schema | Remains closed with `additionalProperties: false`; authored files may contain only the declared diagnosis fields. |
| Core runtime validation | Requires nonblank canonical fields and tolerates extras because validation does not own loading or authored-file admission. |

This is one generic storage shape with boundary-specific admission policy, not
four competing diagnosis contracts. Writers of authored character files remain
subject to the closed schema even though the permissive loader and runtime can
carry legacy regeneration metadata.

Future therapist-generated emotional-reaction fields should be generated by the
existing synthesis/prompt-catalog path and forwarded as character context. They
should not introduce an editable therapist DTO hierarchy or a second generation
subsystem.

### Relationship-State Ownership

`InterestMeter` and `InterestState` are the engine-owned relationship state.
The current typed boundaries are:

- `0`: Unmatched
- `1-4`: Bored
- `5-9`: Lukewarm
- `10-15`: Interested
- `16-20`: VeryIntoIt
- `21-24`: AlmostThere
- `25`: DateSecured

Some prompt helper prose still re-derives labels directly from numeric
interest, and older prompt prose has used a different 15 boundary. The target
guarantee is that relationship prose consumes typed engine state and delta
meaning supplied by Core instead of re-deriving numeric bands in prompt paths.

### Latest Message Duplication

The DATEE prompt path currently receives both the conversation history and
`DateeContext.PlayerDeliveredMessage`. Because `ResolveTurnAsync` adds the
delivered player message to `History` before building `DateeContext`, the
latest player message can appear once in the rendered history and again in the
explicit current-message block. Removing that duplication belongs to its own
implementation issue.

## Target Guarantees

### Session Definition

A session is an engine-owned, role-isolated prompt pipeline:

- avatar pipeline: player-avatar system prompt plus avatar operation context;
- DATEE pipeline: DATEE system prompt plus DATEE operation context;
- no provider-persistent session manager is required for DATEE emotional
  direction;
- no private/player-visible message permission model is needed because game
  output is programmatically selected and exposed by the engine/web host.

Native ordered provider messages may be introduced only after comparative
evidence shows they improve correctness, latency, cost, or cache behavior. They
are not a prerequisite for this sprint.

### Emotional Direction Shape

DATEE emotional direction is one validated operation-local call followed by the
existing DATEE performance call through the same injected primary transport.

The director call may produce operation-local structured direction such as:

- what the delivered message means emotionally;
- the DATEE's primary emotional movement;
- intensity and pressure;
- which character-specific reaction formulation applies;
- which parts of the direction are safe to perform in text.

Director output is never canonical history. It is not shown to players, not
stored as transcript, and not treated as character memory unless a separate
engine-owned persistence feature explicitly promotes a derived state.

### Director-to-Performance Example

Suppose the player uses Honesty at high but not secured interest. Before the
turn, the DATEE is already meaningfully engaged and testing whether the player
is real or just fluent. The roll fails catastrophically, and the delivered
message reads less like vulnerability and more like performed confession.

The director prompt should not say `honesty catastrophe, interest 18->15`.
It should spell out the meaning:

```text
The DATEE had been warm enough to keep investing, but not convinced. The last
message was an attempted honest disclosure that landed badly: it feels like the
player is using vulnerable language as a performance instead of taking a real
risk. Treat this as a sharp emotional reversal, not a generic bad text. Use the
DATEE's diagnosis and backstory to decide what suspicion or hurt this activates.
Return concise direction for how the reply should feel and what must not be
said aloud.
```

A therapist-generated character formulation can then be added in prompt style:

```text
You perceive the last message as performed vulnerability. This makes you feel
manipulated and suspicious that the other person is hiding behind emotional
language.
```

The performance prompt receives concise, actionable direction, not symbolic
shorthand:

```text
Write the DATEE reply in her existing texting style. She is still interested
enough not to disappear, but this specific message makes her cooler and more
watchful. Let the reply show suspicion through restraint, a sharper read of the
player's wording, and one guarded opening. Do not mention rolls, diagnosis,
the director, or "interest".
```

### Required and Best-Effort Behavior

The emotional director is required for the emotional-reaction feature. If its
validated output is missing or invalid after the configured semantic recovery
policy, the turn must fail terminally and transactionality must roll back all
state that belongs to the unresolved turn.

Best-effort operations may continue to degrade only when their behavior is
declared at the operation boundary. Fallback must be explicit, observable, and
test-covered.

### Forbidden Divergence

Do not add:

- a provider-persistent session manager for this feature;
- a private/player-visible message permission model;
- director-specific provider routing, retry, trace, retention, or feature-flag
  systems;
- a second prompt loader or admin-edit path;
- a second transcript store;
- a numeric director band table that bypasses `InterestState`;
- a therapist DTO hierarchy that replaces generic diagnosis storage;
- prompt prose embedded in production C#;
- silent fallback for required emotional direction.

## Known Documentation Drift

`docs/ARCHITECTURE.md`, `docs/prompt-graph.md`, and
`docs/modules/llm-adapters.md` have older wording around "stateful GM",
`StartConversation`, and persistent adapter sessions. Those historical notes
are superseded by the current engine-owned history contract recorded here.

The issue body records prior verification evidence: focused adapter/session
tests passed 15/15, while a broader Core sample had 25 passes and 3
stale-fixture/steering expectation failures. This doc-only worker did not rerun
that broad test sample; see the verification notes in the worker output for the
checks run on this change.
