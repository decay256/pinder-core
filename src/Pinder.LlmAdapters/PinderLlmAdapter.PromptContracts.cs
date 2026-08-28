using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    internal enum PromptProviderOperation
    {
        DialogueOptionsStructured,
        DialogueOptionsUnstructured,
        DateePerformance,
        DateeResponsePlanReconciliation,
        EmotionalDirectorStructured,
        EmotionalDirectorUnstructured,
        InterestChangeBeat,
        HorninessOverlay,
        TrapOverlay,
        FailureCorruption,
        ShadowCorruption,
        SuccessImprovement,
        SteeringQuestion,
        HorninessQuestion,
    }

    internal sealed class PromptProviderOperationContract
    {
        public PromptProviderOperationContract(
            PromptProviderOperation operation,
            PromptContractRoleScope? role,
            string? outputSchema,
            params string[] phases)
        {
            Operation = operation;
            Role = role;
            OutputSchema = outputSchema;
            Phases = Array.AsReadOnly(phases ?? throw new ArgumentNullException(nameof(phases)));
        }

        public PromptProviderOperation Operation { get; }
        public PromptContractRoleScope? Role { get; }
        public string? OutputSchema { get; }
        public IReadOnlyList<string> Phases { get; }
    }

    internal static class PromptProviderOperationContracts
    {
        private static readonly IReadOnlyDictionary<PromptProviderOperation, PromptProviderOperationContract> Contracts =
            new Dictionary<PromptProviderOperation, PromptProviderOperationContract>
            {
                [PromptProviderOperation.DialogueOptionsStructured] = Contract(
                    PromptProviderOperation.DialogueOptionsStructured,
                    PromptContractRoleScope.PlayerAvatar,
                    DialogueOptionsStructuredContract.SchemaName + ":" + DialogueOptionsStructuredContract.SchemaVersion,
                    LlmPhase.DialogueOptions),
                [PromptProviderOperation.DialogueOptionsUnstructured] = Contract(
                    PromptProviderOperation.DialogueOptionsUnstructured,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.DialogueOptions),
                [PromptProviderOperation.DateePerformance] = Contract(
                    PromptProviderOperation.DateePerformance,
                    PromptContractRoleScope.Datee,
                    DateePerformanceStructuredContract.SchemaName + ":" + DateePerformanceStructuredContract.SchemaVersion,
                    LlmPhase.OpponentResponse),
                [PromptProviderOperation.DateeResponsePlanReconciliation] = Contract(
                    PromptProviderOperation.DateeResponsePlanReconciliation,
                    PromptContractRoleScope.Datee,
                    DateeResponsePlanStructuredContract.SchemaName + ":" + DateeResponsePlanStructuredContract.SchemaVersion,
                    LlmPhase.OpponentResponse),
                [PromptProviderOperation.EmotionalDirectorStructured] = Contract(
                    PromptProviderOperation.EmotionalDirectorStructured,
                    null,
                    CharacterEmotionalDirectionContract.SchemaName + ":" + CharacterEmotionalDirectionContract.SchemaVersion,
                    LlmPhase.EmotionalDirector,
                    LlmPhase.AvatarEmotionalDirector),
                [PromptProviderOperation.EmotionalDirectorUnstructured] = Contract(
                    PromptProviderOperation.EmotionalDirectorUnstructured,
                    null,
                    null,
                    LlmPhase.EmotionalDirector,
                    LlmPhase.AvatarEmotionalDirector),
                [PromptProviderOperation.InterestChangeBeat] = Contract(
                    PromptProviderOperation.InterestChangeBeat,
                    PromptContractRoleScope.Datee,
                    null,
                    LlmPhase.InterestChangeBeat),
                [PromptProviderOperation.HorninessOverlay] = Contract(
                    PromptProviderOperation.HorninessOverlay,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.HorninessOverlay),
                [PromptProviderOperation.TrapOverlay] = Contract(
                    PromptProviderOperation.TrapOverlay,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.TrapOverlay),
                [PromptProviderOperation.FailureCorruption] = Contract(
                    PromptProviderOperation.FailureCorruption,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.Delivery),
                [PromptProviderOperation.ShadowCorruption] = Contract(
                    PromptProviderOperation.ShadowCorruption,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.ShadowCorruption),
                [PromptProviderOperation.SuccessImprovement] = Contract(
                    PromptProviderOperation.SuccessImprovement,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.Delivery),
                [PromptProviderOperation.SteeringQuestion] = Contract(
                    PromptProviderOperation.SteeringQuestion,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.Steering),
                [PromptProviderOperation.HorninessQuestion] = Contract(
                    PromptProviderOperation.HorninessQuestion,
                    PromptContractRoleScope.PlayerAvatar,
                    null,
                    LlmPhase.HorninessOverlay),
            };

        public static PromptProviderOperationContract Get(PromptProviderOperation operation)
            => Contracts.TryGetValue(operation, out PromptProviderOperationContract? contract)
                ? contract
                : throw new InvalidOperationException("Missing provider operation contract for '" + operation + "'.");

        private static PromptProviderOperationContract Contract(
            PromptProviderOperation operation,
            PromptContractRoleScope? role,
            string? outputSchema,
            params string[] phases)
            => new PromptProviderOperationContract(operation, role, outputSchema, phases);
    }

    internal sealed class PromptProviderContract
    {
        public PromptProviderContract(
            PromptProviderOperation operation,
            PromptContractRoleScope role,
            IReadOnlyList<AnnotatedInvocationDocument> documents,
            IReadOnlyList<RoleFactAccessDecision>? facts = null)
        {
            Operation = operation;
            Role = role;
            Documents = documents ?? throw new ArgumentNullException(nameof(documents));
            Facts = facts;
        }

        public PromptProviderOperation Operation { get; }
        public PromptContractRoleScope Role { get; }
        public IReadOnlyList<AnnotatedInvocationDocument> Documents { get; }
        public IReadOnlyList<RoleFactAccessDecision>? Facts { get; }
    }

    internal static class PromptProviderContractValidator
    {
        public static void Validate(
            string phase,
            string systemPrompt,
            string userPrompt,
            PromptProviderContract? contract,
            string? actualOutputSchema,
            PromptCatalog catalog,
            PromptContractRegistry registry)
        {
            if (contract == null)
            {
                throw new PromptLayerContractException(
                    "prompt_contract.provider_contract.missing",
                    phase,
                    PromptContractRoleScope.RoleNeutral,
                    "provider_contract",
                    "operation",
                    null,
                    null,
                    null);
            }

            PromptProviderOperationContract expected = PromptProviderOperationContracts.Get(contract.Operation);
            if (!expected.Phases.Contains(phase, StringComparer.Ordinal))
            {
                throw new PromptLayerContractException(
                    "prompt_contract.phase.mismatch",
                    phase,
                    contract.Role,
                    contract.Operation.ToString(),
                    "operation",
                    null,
                    null,
                    string.Join(",", expected.Phases));
            }
            if (expected.Role.HasValue && expected.Role.Value != contract.Role)
            {
                throw new PromptLayerContractException(
                    "prompt_contract.role.mismatch",
                    phase,
                    contract.Role,
                    contract.Operation.ToString(),
                    "operation",
                    null,
                    null,
                    expected.Role.Value.ToString());
            }
            if (!string.Equals(expected.OutputSchema, actualOutputSchema, StringComparison.Ordinal))
            {
                throw new PromptLayerContractException(
                    "prompt_contract.output.conflict",
                    phase,
                    contract.Role,
                    contract.Operation.ToString(),
                    "output_contract",
                    null,
                    null,
                    expected.OutputSchema);
            }

            if (contract.Documents.Count != 2)
            {
                throw PayloadMismatch(phase, contract.Role, "documents");
            }
            AnnotatedInvocationDocument? system = contract.Documents.SingleOrDefault(
                document => document.Role == AgentJournalInputRole.System);
            AnnotatedInvocationDocument? user = contract.Documents.SingleOrDefault(
                document => document.Role == AgentJournalInputRole.User);
            if (system == null || user == null)
            {
                throw PayloadMismatch(phase, contract.Role, "roles");
            }
            if (!string.Equals(system.Text, systemPrompt, StringComparison.Ordinal))
            {
                throw PayloadMismatch(phase, contract.Role, "system_prompt");
            }
            if (!string.Equals(user.Text, userPrompt, StringComparison.Ordinal))
            {
                throw PayloadMismatch(phase, contract.Role, "user_template");
            }

            registry.ValidateCompleteness(catalog);
            PromptContractLinter.Validate(
                phase,
                contract.Role,
                registry,
                catalog,
                contract.Documents,
                contract.Facts);
        }

        private static PromptLayerContractException PayloadMismatch(
            string phase,
            PromptContractRoleScope role,
            string field)
            => new PromptLayerContractException(
                "prompt_contract.payload.mismatch",
                phase,
                role,
                "provider_payload",
                field,
                null,
                null,
                null);
    }

    public sealed partial class PinderLlmAdapter
    {
        private void ValidatePromptContracts(
            string phase,
            PromptContractRoleScope role,
            params AnnotatedInvocationDocument[] documents)
            => ValidatePromptContracts(
                phase,
                role,
                documents,
                facts: null);

        private void ValidatePromptContracts(
            string phase,
            PromptContractRoleScope role,
            IReadOnlyList<AnnotatedInvocationDocument> documents,
            IReadOnlyList<RoleFactAccessDecision>? facts)
        {
            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            PromptContractRegistry registry = _options.PromptContractRegistry ?? PromptContractRegistry.CreateDefault();
            registry.ValidateCompleteness(catalog);
            PromptContractLinter.Validate(phase, role, registry, catalog, documents, facts);
        }

        private void ValidateProviderPromptContracts(
            string phase,
            string systemPrompt,
            string userPrompt,
            PromptProviderContract? contract,
            string? requestSchema)
        {
            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            PromptContractRegistry registry = _options.PromptContractRegistry ?? PromptContractRegistry.CreateDefault();
            PromptProviderContractValidator.Validate(
                phase,
                systemPrompt,
                userPrompt,
                contract,
                requestSchema,
                catalog,
                registry);
        }
    }

    public static class PromptContractLinter
    {
        private static readonly Regex Placeholder = new Regex(
            @"\{[A-Za-z_][A-Za-z0-9_]*\}",
            RegexOptions.CultureInvariant);

        public static void Validate(
            string phase,
            PromptContractRoleScope role,
            PromptContractRegistry registry,
            PromptCatalog catalog,
            IReadOnlyList<AnnotatedInvocationDocument> documents,
            IReadOnlyList<RoleFactAccessDecision>? facts = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            ValidateFacts(phase, role, facts);

            var hard = new Dictionary<PromptContractAuthority, PromptLayerContract>();
            foreach (AnnotatedInvocationDocument document in documents)
            {
                if (Placeholder.IsMatch(document.Text))
                {
                    throw new PromptLayerContractException(
                        "prompt_contract.placeholder.unresolved",
                        phase,
                        role,
                        "document:" + document.DocumentId,
                        Field(document.Role),
                        null,
                        null,
                        null);
                }

                foreach (AgentJournalProvenanceRange range in document.Ranges)
                {
                    string key = CatalogKey(range.Source.KeyPath);
                    if (!registry.TryGet(key, out PromptLayerContract contract))
                    {
                        if (range.Source.Kind == AgentJournalSourceKind.Configuration)
                        {
                            throw Failure(
                                "prompt_contract.registry.missing",
                                phase,
                                role,
                                key,
                                document,
                                range,
                                catalog,
                                null);
                        }
                        continue;
                    }
                    if (contract.Phase != phase && contract.Phase != "any")
                    {
                        throw Failure(
                            "prompt_contract.phase.mismatch",
                            phase,
                            role,
                            key,
                            document,
                            range,
                            catalog,
                            contract.Phase);
                    }
                    if (contract.Knowledge == PromptContractKnowledge.CounterpartPrivate)
                    {
                        throw Failure(
                            "prompt_contract.knowledge.counterpart_private",
                            phase,
                            role,
                            key,
                            document,
                            range,
                            catalog,
                            null);
                    }
                    if (contract.RoleScope != PromptContractRoleScope.RoleNeutral
                        && contract.RoleScope != PromptContractRoleScope.SharedEngine
                        && contract.RoleScope != role)
                    {
                        throw Failure(
                            "prompt_contract.role.mismatch",
                            phase,
                            role,
                            key,
                            document,
                            range,
                            catalog,
                            null);
                    }
                    if (contract.Layer == PromptContractLayer.IdentityPersonality
                        && contract.Authority == PromptContractAuthority.SurfaceStyle)
                    {
                        throw Failure(
                            "prompt_contract.personality.surface_style",
                            phase,
                            role,
                            key,
                            document,
                            range,
                            catalog,
                            null);
                    }
                    if (contract.HardAuthority
                        && hard.TryGetValue(contract.Authority, out PromptLayerContract? prior)
                        && prior.Key != contract.Key)
                    {
                        throw Failure(
                            "prompt_contract.authority.conflict",
                            phase,
                            role,
                            key,
                            document,
                            range,
                            catalog,
                            prior.Key);
                    }
                    if (contract.HardAuthority)
                    {
                        hard[contract.Authority] = contract;
                    }
                }
            }
        }

        private static void ValidateFacts(
            string phase,
            PromptContractRoleScope role,
            IReadOnlyList<RoleFactAccessDecision>? facts)
        {
            if (facts == null) return;
            ConversationParticipantRole expected = role == PromptContractRoleScope.Datee
                ? ConversationParticipantRole.Datee
                : ConversationParticipantRole.PlayerAvatar;
            foreach (RoleFactAccessDecision fact in facts)
            {
                if (!fact.Admitted
                    || fact.RecipientRole != expected
                    || (fact.Visibility == PromptFactVisibility.PrivateToSubject
                        && fact.SubjectCharacterId != fact.RecipientCharacterId))
                {
                    throw new PromptLayerContractException(
                        "prompt_contract.fact_access.conflict",
                        phase,
                        role,
                        fact.FactSourceId,
                        "fact_access",
                        null,
                        null,
                        null);
                }
            }
        }

        private static string CatalogKey(string key)
            => key.EndsWith(".system_prompt", StringComparison.Ordinal)
                ? key.Substring(0, key.Length - 14)
                : key.EndsWith(".user_template", StringComparison.Ordinal)
                    ? key.Substring(0, key.Length - 14)
                    : key;

        private static string Field(AgentJournalInputRole role)
            => role == AgentJournalInputRole.System ? "system_prompt" : "user_template";

        private static PromptLayerContractException Failure(
            string code,
            string phase,
            PromptContractRoleScope role,
            string key,
            AnnotatedInvocationDocument document,
            AgentJournalProvenanceRange range,
            PromptCatalog catalog,
            string? conflict)
        {
            PromptEntry? entry = catalog.TryGet(key);
            string span = entry?.SourceLine.HasValue == true
                ? "line:" + entry.SourceLine.Value + ";utf16:" + range.StartUtf16 + ":" + range.EndUtf16
                : "utf16:" + range.StartUtf16 + ":" + range.EndUtf16;
            return new PromptLayerContractException(
                code,
                phase,
                role,
                key,
                Field(document.Role),
                entry?.SourceFile ?? range.Source.SourceId,
                span,
                conflict);
        }
    }

    public sealed class PromptLayerContractException : InvalidOperationException
    {
        public PromptLayerContractException(
            string code,
            string? phase,
            PromptContractRoleScope role,
            string key,
            string field,
            string? path,
            string? span,
            string? conflict)
            : base(BuildMessage(code, key, field))
        {
            ViolationCode = code;
            Phase = phase;
            Role = role;
            PromptKey = key;
            Field = field;
            SourcePath = path;
            SourceSpan = span;
            ConflictingKey = conflict;
            RemediationSummary = RemediationFor(code);
        }

        public string ViolationCode { get; }
        public string? Phase { get; }
        public PromptContractRoleScope Role { get; }
        public string PromptKey { get; }
        public string Field { get; }
        public string? SourcePath { get; }
        public string? SourceSpan { get; }
        public string? ConflictingKey { get; }
        public string RemediationSummary { get; }

        private static string BuildMessage(string code, string key, string field)
            => "Prompt contract violation: " + code + "; key=" + key + "; field=" + field +
                "; remediation=" + RemediationFor(code) + ".";

        private static string RemediationFor(string code)
        {
            if (code.IndexOf("registry.missing", StringComparison.Ordinal) >= 0)
                return "Register the annotated source with its exact phase, role, layer, authority, and knowledge scope";
            if (code.IndexOf("registry.obsolete", StringComparison.Ordinal) >= 0)
                return "Remove the obsolete registry entry or restore an independently inventoried runtime use";
            if (code.IndexOf("phase.mismatch", StringComparison.Ordinal) >= 0)
                return "Align the operation phase with the registered prompt phase";
            if (code.IndexOf("role.mismatch", StringComparison.Ordinal) >= 0)
                return "Align the recipient role with the registered prompt ownership";
            if (code.IndexOf("fact_access", StringComparison.Ordinal) >= 0)
                return "Rebuild the prompt using only admitted typed fact-access decisions";
            if (code.IndexOf("output", StringComparison.Ordinal) >= 0)
                return "Use the output schema independently assigned to this provider operation";
            if (code.IndexOf("provider_contract", StringComparison.Ordinal) >= 0)
                return "Supply the exact annotated system and user documents for this provider operation";
            if (code.IndexOf("personality.surface_style", StringComparison.Ordinal) >= 0)
                return "Remove surface-style mandates from personality behavior and keep them in texting-style authority";
            if (code.IndexOf("placeholder", StringComparison.Ordinal) >= 0)
                return "Resolve every configured placeholder before provider dispatch";
            if (code.IndexOf("authority.conflict", StringComparison.Ordinal) >= 0)
                return "Keep exactly one hard authority for the conflicting prompt concern";
            return "Correct the prompt contract metadata before retrying the provider operation";
        }
    }
}
