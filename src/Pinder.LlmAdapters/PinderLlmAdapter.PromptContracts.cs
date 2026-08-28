using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;

namespace Pinder.LlmAdapters
{
    internal sealed class PromptProviderContract
    {
        public PromptProviderContract(PromptContractRoleScope role, IReadOnlyList<AnnotatedInvocationDocument> documents, IReadOnlyList<RoleFactAccessDecision>? facts = null, string? outputSchema = null)
        {
            Role = role;
            Documents = documents ?? throw new ArgumentNullException(nameof(documents));
            Facts = facts;
            OutputSchema = outputSchema;
        }

        public PromptContractRoleScope Role { get; }
        public IReadOnlyList<AnnotatedInvocationDocument> Documents { get; }
        public IReadOnlyList<RoleFactAccessDecision>? Facts { get; }
        public string? OutputSchema { get; }
    }

    public sealed partial class PinderLlmAdapter
    {
        private void ValidatePromptContracts(string phase, PromptContractRoleScope role, params AnnotatedInvocationDocument[] documents)
            => ValidatePromptContracts(phase, new PromptProviderContract(role, documents));

        private void ValidatePromptContracts(string phase, PromptProviderContract contract)
        {
            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            PromptContractRegistry registry = _options.PromptContractRegistry ?? PromptContractRegistry.CreateDefault();
            registry.ValidateCompleteness(catalog);
            PromptContractLinter.Validate(phase, contract.Role, registry, catalog, contract.Documents, contract.Facts, contract.OutputSchema);
        }

        private void ValidateProviderPromptContracts(string phase, string systemPrompt, string userPrompt, PromptProviderContract? contract, string? requestSchema)
        {
            PromptProviderContract effective = contract ?? new PromptProviderContract(
                PromptContractRoleScope.SharedEngine,
                new[] { RuntimeDocument("provider.system", AgentJournalInputRole.System, systemPrompt), RuntimeDocument("provider.user", AgentJournalInputRole.User, userPrompt) },
                outputSchema: requestSchema);
            AnnotatedInvocationDocument? system = effective.Documents.SingleOrDefault(document => document.Role == AgentJournalInputRole.System);
            AnnotatedInvocationDocument? user = effective.Documents.SingleOrDefault(document => document.Role == AgentJournalInputRole.User);
            if (system?.Text != systemPrompt || user?.Text != userPrompt)
                throw new PromptLayerContractException("prompt_contract.payload.mismatch", phase, effective.Role, "provider_payload", system?.Text != systemPrompt ? "system_prompt" : "user_template", null, null, null);
            if (requestSchema != null && effective.OutputSchema != requestSchema)
                throw new PromptLayerContractException("prompt_contract.output.conflict", phase, effective.Role, "provider_payload", "output_contract", null, null, effective.OutputSchema);
            ValidatePromptContracts(phase, effective);
        }

        private static AnnotatedInvocationDocument RuntimeDocument(string id, AgentJournalInputRole role, string text)
            => AnnotatedInvocationDocument.Create(id, role, id, text, string.IsNullOrEmpty(text) ? Array.Empty<AgentJournalProvenanceRange>() : new[] { new AgentJournalProvenanceRange(id, 0, text.Length, AgentJournalRangeKind.RuntimeGenerated, AgentJournalRedactionClass.None, new AgentJournalSourceIdentity(AgentJournalSourceKind.RuntimeGenerated, "runtime", id)) });
    }

    public static class PromptContractLinter
    {
        private static readonly Regex Placeholder = new Regex(@"\{[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant);

        public static void Validate(
            string phase,
            PromptContractRoleScope role,
            PromptContractRegistry registry,
            IReadOnlyList<AnnotatedInvocationDocument> documents)
        {
            Validate(phase, role, registry, PromptTemplates.Catalog, documents);
        }

        public static void Validate(
            string phase,
            PromptContractRoleScope role,
            PromptContractRegistry registry,
            PromptCatalog? catalog,
            IReadOnlyList<AnnotatedInvocationDocument> documents,
            IReadOnlyList<RoleFactAccessDecision>? facts = null,
            string? outputSchema = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            ValidateFacts(phase, role, facts);

            var hard = new Dictionary<PromptContractAuthority, PromptLayerContract>();
            foreach (var document in documents)
            {
                if (Placeholder.IsMatch(document.Text))
                    throw new PromptLayerContractException("prompt_contract.placeholder.unresolved", phase, role, "document:" + document.DocumentId, Field(document.Role), null, null, null);

                foreach (var range in document.Ranges)
                {
                    string key = CatalogKey(range.Source.KeyPath);
                    if (!registry.TryGet(key, out var contract))
                    {
                        if (range.Source.Kind == AgentJournalSourceKind.Configuration || range.Source.Kind == AgentJournalSourceKind.Catalog)
                            throw Failure("prompt_contract.registry.missing", phase, role, key, document, range, catalog, null);
                        continue;
                    }
                    if (contract.Phase != phase && contract.Phase != "synthesis" && contract.Phase != "any")
                        throw Failure("prompt_contract.phase.mismatch", phase, role, key, document, range, catalog, null);
                    if (contract.Knowledge == PromptContractKnowledge.CounterpartPrivate)
                        throw Failure("prompt_contract.knowledge.counterpart_private", phase, role, key, document, range, catalog, null);
                    if (contract.RoleScope != PromptContractRoleScope.RoleNeutral && contract.RoleScope != PromptContractRoleScope.SharedEngine && contract.RoleScope != role)
                        throw Failure("prompt_contract.role.mismatch", phase, role, key, document, range, catalog, null);
                    if (contract.Layer == PromptContractLayer.IdentityPersonality && contract.Authority == PromptContractAuthority.SurfaceStyle)
                        throw Failure("prompt_contract.personality.surface_style", phase, role, key, document, range, catalog, null);
                    if (contract.HardAuthority && hard.TryGetValue(contract.Authority, out var prior) && prior.Key != contract.Key)
                        throw Failure("prompt_contract.authority.conflict", phase, role, key, document, range, catalog, prior.Key);
                    if (contract.HardAuthority)
                        hard[contract.Authority] = contract;
                }
            }
        }

        private static void ValidateFacts(string phase, PromptContractRoleScope role, IReadOnlyList<RoleFactAccessDecision>? facts)
        {
            if (facts == null) return;
            var expected = role == PromptContractRoleScope.Datee ? ConversationParticipantRole.Datee : ConversationParticipantRole.PlayerAvatar;
            foreach (var fact in facts)
            {
                if (!fact.Admitted || fact.RecipientRole != expected || (fact.Visibility == PromptFactVisibility.PrivateToSubject && fact.SubjectCharacterId != fact.RecipientCharacterId))
                    throw new PromptLayerContractException("prompt_contract.fact_access.conflict", phase, role, fact.FactSourceId, "fact_access", null, null, null);
            }
        }

        private static string CatalogKey(string key) =>
            key.EndsWith(".system_prompt", StringComparison.Ordinal)
                ? key.Substring(0, key.Length - 14)
                : key.EndsWith(".user_template", StringComparison.Ordinal)
                    ? key.Substring(0, key.Length - 14)
                    : key;

        private static string Field(AgentJournalInputRole role) =>
            role == AgentJournalInputRole.System ? "system_prompt" : "user_template";

        private static PromptLayerContractException Failure(
            string code,
            string phase,
            PromptContractRoleScope role,
            string key,
            AnnotatedInvocationDocument document,
            AgentJournalProvenanceRange range,
            PromptCatalog? catalog,
            string? conflict)
        {
            var e = catalog?.TryGet(key);
            string span = e?.SourceLine.HasValue == true
                ? "line:" + e.SourceLine.Value + ";utf16:" + range.StartUtf16 + ":" + range.EndUtf16
                : range.StartUtf16 + ":" + range.EndUtf16;
            return new PromptLayerContractException(
                code,
                phase,
                role,
                key,
                Field(document.Role),
                e?.SourceFile ?? range.Source.SourceId,
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
            : base("Prompt contract violation: " + code + "; key=" + key + "; field=" + field + ".")
        {
            ViolationCode = code;
            Phase = phase;
            Role = role;
            PromptKey = key;
            Field = field;
            SourcePath = path;
            SourceSpan = span;
            ConflictingKey = conflict;
        }

        public string ViolationCode { get; }
        public string? Phase { get; }
        public PromptContractRoleScope Role { get; }
        public string PromptKey { get; }
        public string Field { get; }
        public string? SourcePath { get; }
        public string? SourceSpan { get; }
        public string? ConflictingKey { get; }
    }
}
