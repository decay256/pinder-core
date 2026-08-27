using System;
using Pinder.Core.Diagnostics.AgentJournals;

namespace Pinder.Core.Conversation
{
    internal static class RoleFactAccessGuard
    {
        internal static RoleFactAccessDecision? RequireAdmitted(
            OwnedPromptFactV1? fact,
            Guid recipientCharacterId,
            ConversationParticipantRole recipientRole,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            GameRunAgentJournalContext? agentJournalContext,
            int turn,
            string operationKind)
        {
            if (fact == null) return null;
            RoleFactAccessDecision decision = RoleFactAccessPolicy.Decide(
                new RoleFactAccessRequest(recipientCharacterId, recipientRole, fact));
            if (decision.Admitted) return decision;

            var exception = new RoleFactAccessDeniedException(decision);
            PersistRejection(decision, agentJournalContext, operationKind, turn, onDiagnostic);
            OperationalDiagnostics.Emit(
                onDiagnostic,
                AgentJournalOperationalDiagnostics.RoleFactAccessRejected(
                    exception,
                    agentJournalContext,
                    operationKind,
                    turn));
            throw exception;
        }

        private static void PersistRejection(
            RoleFactAccessDecision decision,
            GameRunAgentJournalContext? context,
            string operationKind,
            int turn,
            Action<OperationalDiagnosticEvent>? onDiagnostic)
        {
            if (context?.HostSink == null) return;
            var policyRecord = AgentJournalRoleFactPolicyDecisionRecord.Rejected(
                decision,
                context,
                operationKind,
                turn);
            AgentJournalValidationResult validation = AgentJournalValidator.Validate(policyRecord);
            if (!validation.IsValid)
                throw new InvalidOperationException("Rejected role-fact policy record is invalid.");
            AgentJournalSinkRecord sinkRecord = AgentJournalSinkRecord.RoleFactPolicyDecision(policyRecord);
            try
            {
                context.HostSink.PersistAsync(sinkRecord, System.Threading.CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception sinkError)
            {
                OperationalDiagnostics.Emit(
                    onDiagnostic,
                    AgentJournalOperationalDiagnostics.SinkPersistenceFailed(
                        sinkRecord,
                        AgentJournalSinkFailureMode.FailClosed,
                        sinkError));
            }
        }
    }

    internal static class RoleFactPreProviderFailureGuard
    {
        internal static void RejectRawPromptFallbacks(
            ResolvedRevelationTarget? resolvedTarget,
            string? cognitiveSubtext,
            OwnedPromptFactV1? sourceFact,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            GameRunAgentJournalContext? journalContext,
            int turn,
            string operationKind)
        {
            if (resolvedTarget == null && cognitiveSubtext == null) return;
            Throw(
                new RoleFactContractException(
                    "prompt_fact.raw_fallback_forbidden",
                    "Prompt compilation requires typed role facts and a recipient character id."),
                sourceFact,
                onDiagnostic,
                journalContext,
                turn,
                operationKind);
        }

        internal static Guid? RequireRecipientIdentity(
            Guid? recipientCharacterId,
            OwnedPromptFactV1? sourceFact,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            GameRunAgentJournalContext? journalContext,
            int turn,
            string operationKind)
        {
            if (sourceFact == null) return recipientCharacterId;
            if (recipientCharacterId.HasValue && recipientCharacterId.Value != Guid.Empty)
                return recipientCharacterId;
            Throw(
                new RoleFactContractException(
                    "prompt_fact.recipient_character_id.required",
                    "Typed prompt facts require a non-empty recipient character UUID."),
                sourceFact,
                onDiagnostic,
                journalContext,
                turn,
                operationKind);
            return null;
        }

        private static void Throw(
            RoleFactContractException exception,
            OwnedPromptFactV1? sourceFact,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            GameRunAgentJournalContext? journalContext,
            int turn,
            string operationKind)
        {
            OperationalDiagnostics.Emit(
                onDiagnostic,
                AgentJournalOperationalDiagnostics.RoleFactContractRejected(
                    exception,
                    sourceFact,
                    journalContext,
                    operationKind,
                    turn));
            throw exception;
        }
    }
}
