# Issue 1352: Structured Output Version Ownership

## Decision

For named generated JSON contracts that use `StructuredLlmRequest`, the
authoritative version is the root `schema_version` field inside the
model-authored JSON object. `StructuredLlmRequest.SchemaVersion` remains
required request metadata and must be copied into provider-native tool or JSON
schema requests, but it describes the requested contract. The parser accepts
only the version actually produced in the payload.

This keeps one repo-wide versioning model:

- request metadata records what the adapter asked the transport/provider to
  produce;
- in-payload `schema_version` records what the model produced;
- the contract parser compares the payload value with the same contract
  `SchemaVersion` constant.

No transport gets provider-specific game semantics, and no fallback path gets a
different version rule.

## Verified Pre-Implementation State

This section records the baseline inspected before #1352 was implemented. The
implemented deltas below resolve the emotional-director drift it describes.

`src/Pinder.Core/Interfaces/StructuredLlmContracts.cs` defines the
provider-neutral `StructuredLlmRequest`, `StructuredLlmResponse`, and optional
`IStructuredLlmTransport` boundary. The request carries `SchemaName`,
`SchemaVersion`, `JsonSchema`, prompts, sampling, phase, and metadata. The
response carries provider/model metadata, `UsedNativeStructuredOutput`, raw
JSON text, provider request JSON, and validation reporting. These types do not
validate game fields.

`src/Pinder.LlmAdapters/Anthropic/AnthropicTransport.cs` maps
`StructuredLlmRequest.JsonSchema` into an Anthropic tool `input_schema`, names
the tool from `SchemaName`, and uses `SchemaVersion` only in the tool
description. It returns tool input as `UsedNativeStructuredOutput = true` when
present, otherwise falls back to text.

`src/Pinder.LlmAdapters/OpenAi/OpenAiTransport.cs` maps
`StructuredLlmRequest.JsonSchema` into OpenAI-compatible `response_format:
json_schema` only when native structured output is enabled. Otherwise it sends
ordinary messages and returns `UsedNativeStructuredOutput = false` for local
validation. The transport does not inspect domain fields in either mode.

`src/Pinder.LlmAdapters/DialogueOptionsStructuredContract.cs` is already the
reference shape. It sets `SchemaVersion = "dialogue_options.v1"`, requires
root `schema_version` in the JSON schema, verifies that payload field exactly,
and rejects missing/mismatched versions with `invalid_schema_version`. The
local fallback in `PinderLlmAdapter.ParseDialogueOptionsFromTextOrJson` reuses
the same strict parser when the response starts with a JSON object.

`data/prompts/templates.yaml` tells providers that support JSON output to
return `schema_version "dialogue_options.v1"` and shows the versioned object
shape. `tests/Pinder.LlmAdapters.Tests/Issue1159_1160_StructuredDialogueOptionsTests.cs`
asserts request metadata, schema contents, native parsing, local JSON fallback,
and rejection of unexpected fields.

Before #1352, `src/Pinder.LlmAdapters/EmotionalDirectorContract.cs` was
partially aligned but not compliant with this policy. It set
`SchemaVersion = "emotional_director.v1"` on the request and built a JSON
schema for the seven director fields, but the schema omitted `schema_version`,
the parser allowed only the seven fields, and an otherwise valid payload with
`schema_version` was rejected as `unexpected_field`.

`src/Pinder.LlmAdapters/PinderLlmAdapter.EmotionalDirector.cs` uses the same
semantic recovery executor for native structured and fallback paths. For
`UsedNativeStructuredOutput = true`, it requires a complete JSON object. For
plain transport or structured non-native fallback, it uses
`GeneratedJsonObjectExtractor` before calling the same
`EmotionalDirectorContract.TryParse`. This is the right convergence point for
adding in-payload version validation once, provider-neutrally.

Before #1352, `data/prompts/emotional-reactions.yaml` instructed the director
to return exactly seven string fields and did not mention `schema_version`.
`tests/Pinder.LlmAdapters.Tests/Issue1341_EmotionalDirectorContractTests.cs`
and `Issue1342_1343_EmotionalDirectorPerformanceTests.cs` build valid
emotional director fixtures without `schema_version`, so those tests document
the baseline drift corrected by #1352.

`src/Pinder.SessionSetup/Synthesis/LlmTherapistDiagnosisGenerator.cs` is an
adjacent generated JSON user, but it is not a named structured-output transport
contract. It extracts the first JSON object and validates a normalized flat map
through `TherapistDiagnosisContract`. Do not fold it into #1352 unless a
separate issue intentionally promotes diagnosis generation to
`StructuredLlmRequest`.

## Implemented Deltas

Tests were changed first. The initial red run proved that
`EmotionalDirectorContract` rejects a missing or wrong payload version with
`invalid_schema_version` and accepts only `schema_version:
"emotional_director.v1"` in both native and fallback paths.

Change `src/Pinder.LlmAdapters/EmotionalDirectorContract.cs` narrowly:

- add `schema_version` to the required root schema as a `const` equal to
  `SchemaVersion`;
- allow `schema_version` plus the seven existing director fields at the root;
- read and validate `schema_version` before per-field semantic validation;
- return stable reason `invalid_schema_version` for missing, non-string, or
  mismatched versions;
- leave the existing semantic field validators, length limits, duplicate
  property rejection, native complete-object handling, fallback extraction, and
  sanitized exception mapping intact.

Change `data/prompts/emotional-reactions.yaml` narrowly:

- update `emotional-reaction-director.system_prompt` so the field list says
  the JSON object must contain exactly `schema_version`, `primary_emotion`,
  `intensity`, `underlying_feeling`, `interpretation`, `impulse`, `restraint`,
  and `response_posture`;
- state that `schema_version` must be exactly `emotional_director.v1`;
- keep the instruction in YAML and do not add prompt prose in C#.

Change tests narrowly:

- update the emotional director valid JSON helpers in
  `Issue1341_EmotionalDirectorContractTests.cs` and
  `Issue1342_1343_EmotionalDirectorPerformanceTests.cs` to emit
  `schema_version: "emotional_director.v1"`;
- assert `transport.LastStructuredRequest.JsonSchema` contains
  `"schema_version"` and `"emotional_director.v1"`;
- add parser rejection cases for missing version, wrong version, non-string
  version, and duplicate `schema_version`;
- assert native structured and structured non-native/plain fallback both use
  the same `invalid_schema_version` reason;
- update any adjacent fixtures such as `ParameterDriftFixTests` only if they
  embed emotional director JSON directly.

Do not change `StructuredLlmRequest`, `StructuredLlmResponse`,
`IStructuredLlmTransport`, Anthropic transport, OpenAI transport, or
`GeneratedJsonObjectExtractor` for #1352. Those boundaries are already shaped
correctly for this policy.

## Migration and Compatibility

#1342/#1343 already run the emotional director before every production DATEE
performance call, but the director output is operation-local. It is not stored
as canonical history, not exposed to players, and not persisted as character
memory. Therefore the #1352 versioning change does not require a data migration
for existing sessions or saved transcripts.

The compatibility risk is deployment/config skew. After the parser requires
`schema_version`, an older `emotional-reaction-director` prompt that still asks
for only seven fields will cause plain-text or non-native structured routes to
retry and then fail closed. Native schema-capable providers will usually be
forced by the new JSON schema, but the repo must not rely on provider-specific
coercion. Ship the parser, YAML prompt, and tests atomically in the same Core
submodule bump and restart any host that has already loaded the prompt catalog.

Do not add a grace path that accepts missing `schema_version`; that would keep
two live versioning systems. If rollback compatibility is needed, roll back the
whole Core submodule/prompt catalog together.

## Dependency Map

`StructuredLlmRequest` is owned by `Pinder.Core.Interfaces` and is consumed by
provider transports.

`AnthropicTransport` and `OpenAiTransport` consume the request schema but do
not own game validation.

`PinderLlmAdapter.GenerateEmotionalDirectionAsync` owns operation sequencing,
semantic recovery, diagnostics, and the native/fallback convergence point.

`EmotionalDirectorContract` owns the generated JSON schema, parser, version
check, domain field checks, and stable rejection reasons for this contract.

`data/prompts/emotional-reactions.yaml` owns model-facing prompt prose and must
stay the only place where director prompt instructions are edited.

`Issue1341_EmotionalDirectorContractTests` owns contract and retry regressions.
`Issue1342_1343_EmotionalDirectorPerformanceTests` owns production DATEE
orchestration regressions that embed valid director JSON.

## NFRs and Assumptions

Reliability: invalid or unversioned emotional director JSON is a semantic
contract violation and follows the existing retry/exhaustion path. There is no
silent fallback for the required director.

Provider neutrality: the same payload version parser runs after Anthropic
tool input, OpenAI JSON-schema output, structured non-native fallback, and
plain text extraction. Transports remain schema carriers only.

Security and privacy: diagnostics continue to emit sanitized reasons such as
`invalid_schema_version`, not raw director payloads or transcript content.

Latency and cost: #1352 adds no LLM call. It may add retries only when a model
or stale prompt fails to include the required version.

Observability: existing `StructuredLlmResponse.ReportValidation` and
`LlmContractViolation` callbacks should surface `invalid_schema_version`
without adding a new telemetry subsystem.

Configuration: prompt text remains loaded through `PromptCatalog` from
`data/prompts/*.yaml`; no prompt prose moves into C#.

## Risks and Required Reviews

Risk: local JSON extraction returns the first valid object. Keep the director
prompt concise and avoid adding a large example object before the actual answer
unless regression tests prove echoed examples cannot be selected.

Risk: adding `schema_version` to the root allowed-property set without an
explicit equality check would weaken the contract. Review should verify both
schema and parser contain the const/equality rule.

Risk: the existing meta-language filter rejects the word `schema` inside
director field values. That should remain a field-value rule only; it must not
reject the root `schema_version` key before version validation.

Required reviews: backend/code review for parser/schema/test parity; QA review
for native and fallback structured paths; security review only if diagnostics
or leak-guard behavior changes, which #1352 should not require.
