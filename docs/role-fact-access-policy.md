# Role Fact Access Policy

`Pinder.Core.Conversation` owns the V1 authorization contract for private prompt facts. Prompt adapters must construct `OwnedPromptFactV1`, submit a `RoleFactAccessRequest` to `RoleFactAccessPolicy`, and use only admitted facts when assembling role-specific LLM context.

The policy uses canonical character UUIDs plus `ConversationParticipantRole`. Display names, slugs, registry names, or semantic similarity are not proof of ownership.

`RoleFactAccessDecision` is safe diagnostic material: it carries the decision code, subject and recipient identities, visibility, and source id. It intentionally does not carry the fact text, so rejected private backstory, psychological stake, diagnosis, or cognitive subtext is not copied into logs.

Source ids must stay content-free and stable. Use the `PromptFactSourceIds` builders for backstory, stake, diagnosis, cognitive subtext, visible messages, and engine/authored transition targets.

Runtime prompt wiring belongs outside this contract layer. Adapter code must not create parallel ad hoc visibility filters; it should record the policy decision/provenance and fail closed when a required fact is rejected.
