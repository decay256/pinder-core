using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal struct DateeResponseStageResult
    {
        public DateeResponse DateeResponse { get; }
        public double ResponseDelayMinutes { get; }
        public string DateeMessage { get; }

        public DateeResponseStageResult(DateeResponse dateeResponse, double responseDelayMinutes, string dateeMessage)
        {
            DateeResponse = dateeResponse ?? throw new ArgumentNullException(nameof(dateeResponse));
            ResponseDelayMinutes = responseDelayMinutes;
            DateeMessage = dateeMessage ?? throw new ArgumentNullException(nameof(dateeMessage));
        }
    }

    internal class DateeResponseStage
    {
        private readonly ILlmAdapter _llm;
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;

        public DateeResponseStage(
            ILlmAdapter llm,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _onDiagnostic = onDiagnostic;
        }

        public async Task<DateeResponseStageResult> ExecuteAsync(
            GameSessionState state,
            RollStageResult rollStage,
            DeliveryStageResult deliveryStage,
            CharacterProfile player,
            CharacterProfile datee,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct)
        {
            // Compute response delay
            double responseDelayMinutes = datee.Timing.ComputeDelay(state.Interest.Current, rollStage.ResolveDice);

            // Generate datee response
            var dateeTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            Dictionary<ShadowStatType, int>? dateeShadowThresholds = null;
            if (state.DateeShadows != null)
            {
                dateeShadowThresholds = new Dictionary<ShadowStatType, int>();
                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    dateeShadowThresholds[shadow] = state.DateeShadows.GetEffectiveShadow(shadow);
                }
            }

            string dateeArchetypeDirective = datee.ActiveArchetype?.Directive;

            var dateeContext = new DateeContext(
                dateePrompt: datee.AssembledSystemPrompt,
                conversationHistory: TurnOrchestratorHelpers.BuildHistoryForLlmContext(state),
                dateeLastMessage: GameSessionHelpers.GetLastDateeMessage(state.History, datee.DisplayName),
                activeTraps: GameSessionHelpers.GetActiveTrapNames(state.Traps),
                currentInterest: state.Interest.Current,
                playerDeliveredMessage: deliveryStage.DeliveredMessage,
                interestBefore: rollStage.InterestBefore,
                interestAfter: rollStage.InterestAfter,
                responseDelayMinutes: responseDelayMinutes,
                activeTrapInstructions: dateeTrapInstructions,
                playerName: player.DisplayName,
                dateeName: datee.DisplayName,
                currentTurn: state.TurnNumber,
                shadowThresholds: dateeShadowThresholds,
                deliveryTier: rollStage.RollResult.Tier,
                activeArchetypeDirective: dateeArchetypeDirective,
                // #1123 strict bleed isolation: the datee session sees ONLY the
                // avatar's public dating-app card, never the avatar's full
                // private system prompt.
                playerAvatarCard: GameSessionHelpers.BuildPublicProfileCard(player),
                horninessOverlayApplied: deliveryStage.HorninessCheckResult.OverlayApplied,
                horninessTier: deliveryStage.HorninessCheckResult.Tier,
                resolvedTarget: state.CurrentResolvedTarget,
                cognitiveSubtext: state.CurrentCognitiveSubtext);

            progress?.Report(new TurnProgressEvent(TurnProgressStage.DateeResponseStarted));

            string callId = OperationalDiagnostics.CreateCallId();
            OperationalDiagnostics.Emit(
                _onDiagnostic,
                new OperationalDiagnosticEvent(
                    "DateeResponseStage",
                    "DateeResponseStarted",
                    OperationalDiagnosticSeverity.Info,
                    "Datee response operation started.",
                    operationKind: OperationalDiagnosticOperationKind.DateeResponse,
                    phaseCode: LlmPhase.OpponentResponse,
                    lifecycle: OperationalDiagnosticLifecycle.Start,
                    callId: callId,
                    correlationHints: new Dictionary<string, string>
                    {
                        ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));

            DateeResponse dateeResponse;
            try
            {
                if (_llm is Pinder.Core.Interfaces.IStatefulLlmAdapter statefulLlm)
                {
                    var statefulResult = await statefulLlm.GetDateeResponseAsync(
                        dateeContext,
                        state.DateeHistory,
                        ct).ConfigureAwait(false);
                    if (statefulResult == null)
                        throw new InvalidOperationException("LLM adapter returned null stateful datee result");
                    dateeResponse = statefulResult.Response;
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                    if (statefulResult.NewHistoryEntries != null)
                    {
                        foreach (var entry in statefulResult.NewHistoryEntries)
                        {
                            if (entry != null)
                                state.DateeHistory.Add(entry);
                        }
                    }
                }
                else
                {
                    dateeResponse = await _llm.GetDateeResponseAsync(dateeContext, ct).ConfigureAwait(false);
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                }

                OperationalDiagnostics.Emit(
                    _onDiagnostic,
                    new OperationalDiagnosticEvent(
                        "DateeResponseStage",
                        "DateeResponseSucceeded",
                        OperationalDiagnosticSeverity.Info,
                        "Datee response operation succeeded.",
                        operationKind: OperationalDiagnosticOperationKind.DateeResponse,
                        phaseCode: LlmPhase.OpponentResponse,
                        lifecycle: OperationalDiagnosticLifecycle.Terminal,
                        outcome: OperationalDiagnosticOutcome.Succeeded,
                        callId: callId,
                        correlationHints: new Dictionary<string, string>
                        {
                            ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }));
            }
            catch (OperationCanceledException ex)
            {
                OperationalDiagnostics.Emit(
                    _onDiagnostic,
                    new OperationalDiagnosticEvent(
                        "DateeResponseStage",
                        "DateeResponseCancelled",
                        OperationalDiagnosticSeverity.Warning,
                        "Datee response operation was cancelled.",
                        ex,
                        OperationalDiagnosticOperationKind.DateeResponse,
                        LlmPhase.OpponentResponse,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Cancelled,
                        OperationalDiagnosticFailureClassification.Cancelled,
                        callId: callId,
                        correlationHints: new Dictionary<string, string>
                        {
                            ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }));
                throw;
            }
            catch (Exception ex)
            {
                OperationalDiagnostics.Emit(
                    _onDiagnostic,
                    new OperationalDiagnosticEvent(
                        "DateeResponseStage",
                        "DateeResponseFailed",
                        OperationalDiagnosticSeverity.Error,
                        "Datee response operation failed.",
                        ex,
                        OperationalDiagnosticOperationKind.DateeResponse,
                        LlmPhase.OpponentResponse,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Failed,
                        OperationalDiagnostics.ClassifyException(ex),
                        callId: callId,
                        correlationHints: new Dictionary<string, string>
                        {
                            ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }));
                throw;
            }

            string dateeMessage = dateeResponse.MessageText;
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DateeResponseCompleted, dateeMessage));

            if (state.CurrentResolvedTarget.HasValue)
            {
                var target = state.CurrentResolvedTarget.Value;
                if (target.Registry == "BACKSTORY")
                {
                    state.SpentBackstoryIndices.Add(target.Index);
                }
                else if (target.Registry == "STAKE")
                {
                    state.SpentStakeIndices.Add(target.Index);
                }
            }

            return new DateeResponseStageResult(dateeResponse, responseDelayMinutes, dateeMessage);
        }
    }
}
