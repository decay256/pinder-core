using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.AgentJournals;

namespace Pinder.LlmAdapters
{
    public sealed partial class PinderLlmAdapter
    {
        internal Task<CharacterEmotionalDirection> GenerateEmotionalDirectionAsync(
            DateeContext context,
            CancellationToken cancellationToken = default)
        {
            var promptCompiler = new EmotionalPromptCompiler(
                PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog));
            return GenerateEmotionalDirectionAsync(context, promptCompiler, cancellationToken);
        }

        internal Task<CharacterEmotionalDirection> GenerateEmotionalDirectionAsync(
            DateeContext context,
            EmotionalPromptCompiler promptCompiler,
            CancellationToken cancellationToken = default)
            => GenerateEmotionalDirectionAsync(
                context,
                promptCompiler,
                priorMessages: null,
                dateeSystemPrompt: null,
                privateBranch: null,
                cancellationToken);

        private Task<CharacterEmotionalDirection> GenerateEmotionalDirectionAsync(
            DateeContext context,
            EmotionalPromptCompiler promptCompiler,
            IReadOnlyList<ConversationMessage>? priorMessages,
            string? dateeSystemPrompt,
            PiConversationBranch? privateBranch,
            CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (promptCompiler == null) throw new ArgumentNullException(nameof(promptCompiler));

            PromptCatalog catalog = PromptCatalog.ResolveCatalogOrThrow(_options.PromptCatalog);
            CompiledEmotionalDirectorPrompt prompt = promptCompiler.CompileDirector(
                context,
                includeConversationHistory: priorMessages == null,
                dateeSystemPrompt: dateeSystemPrompt);

            return ExecuteCharacterEmotionalDirectorAsync(
                new CharacterEmotionalDirectorInvocation
                {
                    Phase = LlmPhase.EmotionalDirector,
                    JournalOperation = GameRunConversationJournalInventory.EmotionalDirector,
                    PrivatePhase = DateePrivatePhaseDirector,
                    BranchKind = "datee-private-analysis",
                    Turn = context.CurrentTurn,
                    SystemPrompt = prompt.SystemPrompt,
                    UserPrompt = prompt.UserPrompt,
                    Temperature = prompt.Temperature ?? LlmPhaseTemperatures.EmotionalDirector,
                    MaxTokens = prompt.MaxTokens ?? _options.MaxTokens,
                    AllowedEmotions = CharacterEmotionCatalog.Load(catalog),
                    PriorMessages = priorMessages,
                    PrivateBranch = privateBranch,
                    JournalContext = context.AgentJournalContext,
                    BuildMetadata = attemptPrompt => BuildEmotionalDirectorMetadata(
                        prompt.Metadata,
                        attemptPrompt),
                    CompileRetrySystemPrompt = reason => promptCompiler.CompileDirectorRetrySystemPrompt(
                        prompt.SystemPrompt,
                        reason),
                    OnExhausted = (rejection, attempts) => EmitEmotionalDirectorExhaustedDiagnostic(
                        rejection,
                        attempts,
                        context.CurrentTurn),
                },
                cancellationToken);
        }

        private void EmitEmotionalDirectorExhaustedDiagnostic(
            LlmContractException finalRejection,
            int attemptCount,
            int turn)
        {
            OperationalDiagnostics.Emit(
                GetDiagnosticSink(),
                new OperationalDiagnosticEvent(
                    source: "PinderLlmAdapter",
                    eventName: "EmotionalDirectorContractExhausted",
                    severity: OperationalDiagnosticSeverity.Error,
                    message: "Private character emotional director contract recovery was exhausted.",
                    exception: null,
                    operationKind: OperationalDiagnosticOperationKind.DateeEmotionalDirector,
                    phaseCode: LlmPhase.EmotionalDirector,
                    lifecycle: OperationalDiagnosticLifecycle.Terminal,
                    outcome: OperationalDiagnosticOutcome.Failed,
                    failureClassification: OperationalDiagnosticFailureClassification.Permanent,
                    callId: OperationalDiagnostics.CreateCallId(),
                    correlationHints: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["phase"] = LlmPhase.EmotionalDirector,
                        ["datee_private_phase"] = DateePrivatePhaseDirector,
                        ["exception_type"] = finalRejection.GetType().Name,
                        ["failure_kind"] = "contract_violation",
                        ["reason"] = finalRejection.Reason,
                        ["attempt_count"] = attemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));
        }
    }
}
