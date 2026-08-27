# Role Fact Access Policy

`Pinder.Core.Conversation` owns the V1 authorization contract for private prompt facts. Prompt adapters must construct `OwnedPromptFactV1`, submit a `RoleFactAccessRequest` to `RoleFactAccessPolicy`, and use only admitted facts when assembling role-specific LLM context.

The policy uses canonical character UUIDs plus `ConversationParticipantRole`. Display names, slugs, registry names, or semantic similarity are not proof of ownership.

`RoleFactAccessDecision` is safe diagnostic material: it carries the decision code, subject and recipient identities, visibility, and source id. It intentionally does not carry the fact text, so rejected private backstory, psychological stake, diagnosis, or cognitive subtext is not copied into logs.

Source ids must stay content-free and stable. Use the `PromptFactSourceIds` builders for backstory, stake, diagnosis, cognitive subtext, visible messages, and engine/authored transition targets.

Runtime prompt wiring belongs outside this contract layer. Adapter code must not create parallel ad hoc visibility filters; it should record the policy decision/provenance and fail closed when a required fact is rejected.

## Runtime and restore diagnostics

Role-specific prompt contexts accept only typed targets/facts. When any typed fact is present, the caller must also supply the canonical recipient character UUID. Raw `resolvedTarget` or `cognitiveSubtext` compatibility values are rejected before prompt assembly.

Operators can use these stable codes without inspecting private text:

| Code | Meaning | Operator action |
|---|---|---|
| `prompt_fact.raw_fallback_forbidden` | A caller attempted role-neutral prompt input. | Update the caller to pass the appropriate typed role target/fact and recipient UUID. |
| `prompt_fact.recipient_character_id.required` | Typed prompt input had no canonical recipient identity. | Propagate `CharacterProfile.CharacterId` for the receiving role. |
| `restore.role_target.legacy_ambiguous_active_target` | Direct restore contained an active role-neutral target. | Regenerate the restore payload with avatar/datee target fields; do not infer ownership. |
| `snapshot.role_target.legacy_active_target_forbidden` | A session-runner snapshot contained a standalone or mixed active legacy target. | Reject or regenerate the snapshot; mixed data is not automatically patched. |
| `snapshot.role_fact.source_kind_mismatch` | Persisted `source_kind` disagreed with the canonical `source_id`. | Treat the snapshot as corrupt and regenerate it from a validated source. |
| `restore.schema_version.required` / `snapshot.schema_version.required` | A restore or envelope omitted its explicit schema version. | Reject the payload; regenerate it with the current schema. |
| `restore.schema_version.unsupported` / `snapshot.schema_version.unsupported` | A restore or envelope uses an unknown schema version. | Upgrade through a supported migration path. |
| `restore.schema_version.legacy_active_target_forbidden` / `snapshot.schema_version.legacy_active_target_forbidden` | Identity-backed legacy data contains any active target. | Regenerate with current role-explicit target fields. Legacy restore is permitted only for identity-backed no-target data. |
| `target.registry.invalid`, `target.index.invalid`, `target.field.invalid` | Persisted target coordinates are not canonical. | Treat the payload as tampered or corrupt. |
| `target.source_id.category_mismatch`, `target.source_id.index_mismatch`, `target.source_id.field_mismatch`, `target.stem_text_mismatch` | Target coordinates, source metadata, or stem text disagree. | Reject rather than repairing or guessing the intended target. |

Current `ResimulateData`, initial snapshots, and turn snapshots use schema version 2. Version 1 is the identity-backed legacy no-target form. Version 0/missing and unknown versions fail closed.

Each admitted provider invocation may carry `role_fact_access_decisions`. These records include schema version, admission result, reason code, source ID and `PromptFactSourceKind`, visibility, and subject/recipient UUID+role. They deliberately contain no fact text. A rejected fact throws `RoleFactAccessDeniedException` with code `prompt_fact.access_denied`, emits terminal `AgentJournalRoleFactAccessRejected` / phase `role_fact_access`, and stops before provider invocation, retry, RNG reservation, history/spent mutation, or snapshot advancement.

A journaling-enabled `GameRunAgentJournalContext` supplies the durable host sink.
Each denied private fact is written synchronously as
`pinder.role-fact-policy-decision.v1` before the typed exception escapes. The
record is text-free and provider-independent: it contains no invocation ID.
Malformed raw fallback and missing-recipient contract failures use the shared
`AgentJournalRoleFactContractRejected` diagnostic helper; typed failures include
source metadata but never source text.
