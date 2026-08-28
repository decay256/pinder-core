using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;

namespace Pinder.SessionSetup
{
    public sealed class LlmPersonalityConsolidator : IPersonalityConsolidator
    {
        private const int MaxAttempts = 3;
        private const string RepairPromptKey = "personality-consolidation-repair-surface-style";

        private readonly ILlmTransport _transport;
        private readonly PromptCatalog _catalog;
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;

        public LlmPersonalityConsolidator(
            ILlmTransport transport,
            PromptCatalog catalog,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _onDiagnostic = onDiagnostic;
            _catalog.RequireCompleteEntry(
                "personality_consolidation",
                "prompt-catalog: missing required key 'personality_consolidation'.");
        }

        public async Task<string> GenerateAsync(
            string characterName,
            string genderIdentity,
            string bio,
            string gameSystemPrompt,
            IReadOnlyList<string> personalityFragments,
            string stats,
            CancellationToken cancellationToken = default)
        {
            var entry = _catalog.Get("personality_consolidation");
            string userPrompt = PromptCatalog.Substitute(entry.UserTemplate!, new Dictionary<string, string>
            {
                { "characterName", characterName },
                { "genderIdentity", genderIdentity },
                { "bio", string.IsNullOrWhiteSpace(bio) ? "(none)" : bio },
                { "game_system_prompt", gameSystemPrompt ?? string.Empty },
                { "personality_fragments", FormatList(personalityFragments) },
                { "stats", stats ?? string.Empty },
            });

            string baseSystemPrompt = entry.SystemPrompt!;
            string attemptSystemPrompt = baseSystemPrompt;
            var recovery = await SemanticOutputRecoveryExecutor.ExecuteAsync<string, PersonalityConsolidationContractException>(
                MaxAttempts,
                async (attempt, attemptCancellationToken) =>
                {
                    string result = await LlmOptionalTextGeneration.SendRequiredAsync(
                        "personality_consolidation", _transport, attemptSystemPrompt, userPrompt,
                        entry.Temperature!.Value, entry.MaxTokens, LlmPhase.Synthesis, _onDiagnostic,
                        attemptCancellationToken).ConfigureAwait(false);
                    result = result.Trim();
                    var validation = ConsolidatedPersonalityValidator.Validate(result);
                    if (validation.IsValid)
                        return SemanticOutputRecoveryAttemptResult<string, PersonalityConsolidationContractException>.Accepted(result);

                    var rejection = new PersonalityConsolidationContractException(validation.ViolationCode!);
                    if (attempt < MaxAttempts)
                        attemptSystemPrompt = AppendRepairPrompt(baseSystemPrompt, rejection.ViolationCode);
                    return SemanticOutputRecoveryAttemptResult<string, PersonalityConsolidationContractException>.Rejected(rejection);
                },
                onRejected: EmitRejectedDiagnostic,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (recovery.IsAccepted)
                return recovery.AcceptedValue;
            throw recovery.Exhaustion.FinalRejection;
        }

        private string AppendRepairPrompt(string baseSystemPrompt, string violationCode)
        {
            var repairEntry = _catalog.TryGet(RepairPromptKey);
            if (repairEntry == null || string.IsNullOrWhiteSpace(repairEntry.SystemPrompt))
                throw new InvalidOperationException("prompt-catalog: missing required key '" + RepairPromptKey + "' with system_prompt.");
            string repairSystemPrompt = repairEntry.SystemPrompt!;
            return baseSystemPrompt + Environment.NewLine + Environment.NewLine + PromptCatalog.Substitute(
                repairSystemPrompt, new Dictionary<string, string> { { "violation_code", violationCode } });
        }

        private void EmitRejectedDiagnostic(SemanticOutputRecoveryRejection<PersonalityConsolidationContractException> rejection)
        {
            OperationalDiagnostics.Emit(_onDiagnostic, new OperationalDiagnosticEvent(
                "LlmPersonalityConsolidator", "PersonalityConsolidationContractRejected",
                rejection.IsFinalAttempt ? OperationalDiagnosticSeverity.Error : OperationalDiagnosticSeverity.Warning,
                "Personality consolidation output violated the behavioral-layer contract.",
                operationKind: OperationalDiagnosticOperationKind.SetupSynthesis,
                phaseCode: LlmPhase.Synthesis,
                lifecycle: rejection.IsFinalAttempt ? OperationalDiagnosticLifecycle.Terminal : OperationalDiagnosticLifecycle.Phase,
                outcome: rejection.IsFinalAttempt ? OperationalDiagnosticOutcome.Failed : OperationalDiagnosticOutcome.Degraded,
                failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                callId: OperationalDiagnostics.CreateCallId(),
                correlationHints: new Dictionary<string, string>
                {
                    { "generator", "personality_consolidation" },
                    { "violation_code", rejection.Rejection.ViolationCode },
                    { "attempt", rejection.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                }));
        }

        private static string FormatList(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
                return "- (none)";

            var lines = new List<string>();
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    lines.Add("- " + value.Trim());
            }

            return lines.Count == 0 ? "- (none)" : string.Join("\n", lines);
        }
    }
}
