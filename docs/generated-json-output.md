# Generated JSON Output

Generated JSON handling separates transport mechanics from game contracts:

- Extraction: `Pinder.LlmAdapters.GeneratedJsonObjectExtractor` finds the
  first complete, syntactically valid JSON object in generated text. It owns
  bounded scanning, fenced/prose tolerance, escaped string braces, root-array
  rejection, and explicit failure codes. It does not retry, repair, validate
  domain fields, or fabricate fallback data.
- Request metadata: `StructuredLlmRequest.SchemaName` and
  `StructuredLlmRequest.SchemaVersion` identify the contract requested from a
  provider-neutral transport. Transports may map those values to native tools,
  JSON-schema mode, or local-validation metadata, but transports do not decide
  whether generated game content is valid.
- Validation: the call site owns the schema/contract check after extraction or
  native structured transport. For example, therapist diagnosis generation
  deserializes the extracted object and then validates the required
  cognitive-subtext fields. Named structured-output contracts such as dialogue
  options and emotional director validate their root object in their contract
  parser.
- Retry and terminal policy: `SemanticOutputRecoveryExecutor` owns retry
  attempts around semantic rejection. The domain caller maps the final
  rejection to the appropriate exception and diagnostic text.

Use the extractor when a model may return prose or Markdown around a JSON
object. If a provider already guarantees a typed object, prefer that provider
contract and keep this helper out of the provider transport layer.

## Version Ownership Policy

For named generated JSON contracts that create a `StructuredLlmRequest`, the
model-authored payload must self-identify with a root string field named
`schema_version`. That in-payload value is the authoritative version for
accepting or rejecting the generated object. `StructuredLlmRequest.SchemaVersion`
is still required request metadata and must match the contract constant, but it
is not sufficient to accept a response because it describes what was requested,
not what the model actually produced.

The parser and JSON schema for each named generated JSON contract must follow
the same rules:

- The root object requires `schema_version` plus the contract's domain fields.
- `schema_version` must be a string equal to the contract's `SchemaVersion`
  constant, for example `dialogue_options.v1` or `emotional_director.v1`.
- Missing, non-string, or mismatched `schema_version` is a semantic contract
  rejection with the stable reason `invalid_schema_version`.
- Native structured output and local JSON fallback converge on the same domain
  parser after the native/extraction boundary. Native paths may require a
  complete root object; fallback paths may first use
  `GeneratedJsonObjectExtractor`, but version validation is identical after
  that point.
- Prompt YAML for a named generated JSON contract must instruct the model to
  include the exact `schema_version` field. This instruction belongs in the
  existing prompt catalog, not in production C# prose.

This policy avoids a second versioning system: request metadata, provider tool
names, and diagnostics record the requested contract; the generated JSON object
records the produced contract.

## Current Contract Inventory

`DialogueOptionsStructuredContract` is the compliant reference implementation.
It sets `SchemaName = "dialogue_options"` and
`SchemaVersion = "dialogue_options.v1"`, requires root
`schema_version`, emits a JSON Schema `const` for that field, validates the
same payload under Anthropic tool, OpenAI JSON-schema, structured non-native,
and plain-text local JSON fallback paths, and has regression coverage in
`Issue1159_1160_StructuredDialogueOptionsTests`.

`CharacterEmotionalDirectionContract` sets `SchemaName = "emotional_director"` and
`SchemaVersion = "emotional_director.v1"` on `StructuredLlmRequest`. Its
payload schema and parser require root `schema_version:
"emotional_director.v1"` plus the seven director fields, and reject missing,
non-string, or mismatched payload versions with `invalid_schema_version` before
the per-field semantic checks run.

Therapist diagnosis generation is adjacent but not the same contract family. It
uses `GeneratedJsonObjectExtractor` and validates the resulting flat map through
`TherapistDiagnosisContract`; it does not currently create a
`StructuredLlmRequest` or publish a named provider-native JSON schema. Keep it
out of the #1352 migration unless it is intentionally promoted to a named
structured-output contract in a separate ticket.

See `docs/specs/issue-1352-structured-output-versioning.md` for the verified
source inventory, implementation deltas, dependency map, risks, and test plan.
