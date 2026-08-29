using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly GameRunAgentJournalContext? _agentJournalContext;

        public DateeResponseStage(
            ILlmAdapter llm,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null,
            GameRunAgentJournalContext? agentJournalContext = null)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _onDiagnostic = onDiagnostic;
            _agentJournalContext = agentJournalContext;
        }

        public async Task<DateeResponseStageResult> ExecuteAsync(
            GameSessionState state,
            RollStageResult rollStage,
            DeliveryStageResult deliveryStage,
            CharacterProfile player,
            CharacterProfile datee,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct,
            InterestState? finalInterestAfterState = null,
            CharacterEmotionalStatus? playerEmotionalStatus = null,
            CharacterEmotionalStatus? dateeEmotionalStatus = null,
            DateeResponseReplayState? replayState = null)
        {
            if (replayState != null)
            {
                if (state.TurnNumber != replayState.PostTurnNumber)
                    throw new InvalidOperationException("datee_response_replay.post_turn.identity.mismatch");
                if (!string.Equals(
                    deliveryStage.DeliveredMessage,
                    replayState.DeliveredMessage,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("datee_response_replay.delivered_message.identity.mismatch");
                }
            }

            int finalInterestAfter = state.Interest.Current;
            InterestState resolvedFinalInterestAfterState =
                replayState?.InterestAfterState
                ?? finalInterestAfterState
                ?? new InterestMeter(finalInterestAfter).GetState();
            playerEmotionalStatus ??= CharacterEmotionalStatusResolver.Resolve(player, 0, 0);
            dateeEmotionalStatus ??= CharacterEmotionalStatusResolver.Resolve(datee, 0, 0);
            string deliveredMessage = replayState?.DeliveredMessage ?? deliveryStage.DeliveredMessage;
            AcceptedDateeResponsePlanState? replayPlanState =
                state.TakeDateeResponsePlanForReplay(deliveredMessage);
            int responseTurn = replayPlanState?.OriginatingTurn ?? state.TurnNumber;
            if (replayState != null && responseTurn != replayState.ResponseTurn)
                throw new InvalidOperationException("datee_response_replay.response_turn.identity.mismatch");

            // Compute response delay
            double responseDelayMinutes = replayState?.ResponseDelayMinutes
                ?? datee.Timing.ComputeDelay(finalInterestAfter, rollStage.ResolveDice);

            // Generate datee response
            IReadOnlyList<string> activeTrapNames = replayState?.ActiveTrapIds
                ?? GameSessionHelpers.GetActiveTrapNames(state.Traps);
            string[]? dateeTrapInstructions = replayState != null
                ? (replayState.ActiveTrapInstructions.Count == 0
                    ? null
                    : replayState.ActiveTrapInstructions.ToArray())
                : GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

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

            int interestBefore = replayState?.InterestBefore ?? rollStage.InterestBefore;
            DateeReactionTarget? admittedDateeReactionTarget = state.CurrentDateeReactionTarget;
            if (admittedDateeReactionTarget != null)
            {
                string? manner = admittedDateeReactionTarget.ResolvedTarget.Manner;
                if (manner == "CURATED_BUFFER" || manner == "DEFENSIVE_EVASION" || manner == "INTIMATE_BREAKTHROUGH")
                {
                    bool compatible = (manner == "CURATED_BUFFER" && finalInterestAfter == interestBefore)
                        || (manner == "DEFENSIVE_EVASION" && finalInterestAfter < interestBefore)
                        || (manner == "INTIMATE_BREAKTHROUGH" && finalInterestAfter > interestBefore);
                    if (!compatible)
                    {
                        admittedDateeReactionTarget = null;
                    }
                }
            }

            var dateeContext = new DateeContext(
                dateePrompt: datee.AssembledSystemPrompt,
                conversationHistory: TurnOrchestratorHelpers.BuildHistoryForLlmContext(state),
                dateeLastMessage: GameSessionHelpers.GetLastDateeMessage(state.History, datee.DisplayName),
                activeTraps: activeTrapNames,
                currentInterest: finalInterestAfter,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: interestBefore,
                interestAfter: finalInterestAfter,
                responseDelayMinutes: responseDelayMinutes,
                activeTrapInstructions: dateeTrapInstructions,
                playerName: player.DisplayName,
                dateeName: datee.DisplayName,
                currentTurn: responseTurn,
                shadowThresholds: dateeShadowThresholds,
                deliveryTier: replayState?.DeliveryTier ?? rollStage.RollResult.Tier,
                activeArchetypeDirective: dateeArchetypeDirective,
                // #1123 strict bleed isolation: the datee session sees ONLY the
                // avatar's public dating-app card, never the avatar's full
                // private system prompt.
                playerAvatarCard: GameSessionHelpers.BuildPublicProfileCard(player),
                horninessOverlayApplied: replayState?.HorninessOverlayApplied
                    ?? deliveryStage.HorninessCheckResult.OverlayApplied,
                horninessTier: replayState?.HorninessTier
                    ?? deliveryStage.HorninessCheckResult.Tier,
                resolvedTarget: null,
                cognitiveSubtext: null,
                interestBeforeState: replayState?.InterestBeforeState ?? rollStage.StateBefore,
                interestAfterState: resolvedFinalInterestAfterState,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    replayState?.RollStat ?? rollStage.RollResult.Stat,
                    replayState?.OutcomeIntensity ?? RollOutcomeIntensityContract.FromRollResult(rollStage.RollResult),
                    datee.PsychiatricDiagnosis),
                agentJournalContext: _agentJournalContext,
                dateeTextingStyle: datee.TextingStyleFragment,
                playerHungerForIntimacy: playerEmotionalStatus.HungerForIntimacy,
                playerTerrorOfRejection: playerEmotionalStatus.TerrorOfRejection,
                dateeHungerForIntimacy: dateeEmotionalStatus.HungerForIntimacy,
                dateeTerrorOfRejection: dateeEmotionalStatus.TerrorOfRejection,
                previousAcceptedEmotionalDirections: state.DateeEmotionalDirectionHistory,
                dateeReactionTarget: admittedDateeReactionTarget,
                cognitiveSubtextFact: state.CurrentDateeCognitiveSubtextFact,
                recipientCharacterId: datee.CharacterId,
                onDiagnostic: _onDiagnostic,
                acceptedDateeResponsePlanState: replayPlanState);

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
                        ["turn"] = responseTurn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));

            DateeResponse dateeResponse;
            StatefulDateeResult? sessionResult = null;
            try
            {
                if (_llm is Pinder.Core.Interfaces.ISessionStatefulLlmAdapter sessionLlm
                    && sessionLlm.SupportsConversationSessions)
                {
                    sessionResult = await sessionLlm.GetDateeResponseAsync(
                        dateeContext,
                        new List<ConversationMessage>(state.DateeHistory),
                        new List<ConversationMessage>(state.AvatarHistory),
                        state.DateeSessionSnapshot,
                        state.AvatarSessionSnapshot,
                        ct).ConfigureAwait(false);
                    if (sessionResult == null)
                        throw new InvalidOperationException("LLM adapter returned null session datee result");
                    dateeResponse = sessionResult.Response;
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                    if (sessionResult.DateeSessionSnapshot == null)
                        throw new InvalidOperationException("Session adapter omitted the DATEE session snapshot.");
                    if (sessionResult.AvatarSessionSnapshot == null)
                        throw new InvalidOperationException("Session adapter omitted the avatar session snapshot.");
                }
                else if (_llm is Pinder.Core.Interfaces.IStatefulLlmAdapter statefulLlm)
                {
                    var statefulResult = await statefulLlm.GetDateeResponseAsync(
                        dateeContext,
                        new List<ConversationMessage>(state.DateeHistory),
                        ct).ConfigureAwait(false);
                    if (statefulResult == null)
                        throw new InvalidOperationException("LLM adapter returned null stateful datee result");
                    dateeResponse = statefulResult.Response;
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                }
                else
                {
                    dateeResponse = await _llm.GetDateeResponseAsync(dateeContext, ct).ConfigureAwait(false);
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                }

                OperationalDiagnostics.EmitSucceededTerminal(
                    _onDiagnostic,
                    "DateeResponseStage",
                    "DateeResponseSucceeded",
                    "Datee response operation succeeded.",
                    OperationalDiagnosticOperationKind.DateeResponse,
                    LlmPhase.OpponentResponse,
                    callId,
                    responseTurn);
            }
            catch (OperationCanceledException ex)
            {
                OperationalDiagnostics.EmitCancelledTerminal(
                    _onDiagnostic,
                    "DateeResponseStage",
                    "DateeResponseCancelled",
                    "Datee response operation was cancelled.",
                    ex,
                    OperationalDiagnosticOperationKind.DateeResponse,
                    LlmPhase.OpponentResponse,
                    callId,
                    responseTurn);
                throw;
            }
            catch (Exception ex)
            {
                OperationalDiagnostics.EmitFailedTerminal(
                    _onDiagnostic,
                    "DateeResponseStage",
                    "DateeResponseFailed",
                    "Datee response operation failed.",
                    ex,
                    OperationalDiagnosticOperationKind.DateeResponse,
                    LlmPhase.OpponentResponse,
                    callId,
                    responseTurn);
                throw;
            }

            state.DateeHistory.Add(ConversationMessage.User(deliveredMessage));
            state.DateeHistory.Add(ConversationMessage.Assistant(dateeResponse.MessageText));
            CharacterEmotionalDirection? acceptedDirection = dateeResponse.EmotionalReactionDebug?.Direction;
            if (acceptedDirection != null)
            {
                state.RecordAcceptedDateeEmotionalDirection(
                    replayState?.AcceptedEmotionalDirection
                    ?? CharacterEmotionalDirectionSummary.FromDirection(responseTurn, acceptedDirection));
            }
            DateeResponsePlan? acceptedPlan = dateeResponse.EmotionalReactionDebug?.ResponsePlan;
            if (acceptedPlan != null)
            {
                state.LastAcceptedDateeResponsePlan = acceptedPlan;
            }
            AcceptedDateeResponsePlanState? acceptedPlanState = dateeResponse.EmotionalReactionDebug?.ResponsePlanState;
            if (acceptedPlanState != null)
            {
                state.LastAcceptedDateeResponsePlanState = acceptedPlanState;
            }
            if (sessionResult != null)
            {
                state.AvatarHistory.Add(ConversationMessage.Assistant(deliveredMessage));
                state.AvatarHistory.Add(ConversationMessage.User(dateeResponse.MessageText));
                state.DateeSessionSnapshot = sessionResult.DateeSessionSnapshot;
                state.AvatarSessionSnapshot = sessionResult.AvatarSessionSnapshot;
            }

            string dateeMessage = dateeResponse.MessageText;
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DateeResponseCompleted, dateeMessage));

            if (state.CurrentDateeReactionTarget != null)
            {
                var target = state.CurrentDateeReactionTarget.ResolvedTarget;
                if (target.Registry == EmotionStemSelectionRules.BackstoryRegistry)
                    state.DateeSpentBackstoryIndices.Add(target.Index);
                else if (target.Registry == EmotionStemSelectionRules.StakeRegistry)
                    state.DateeSpentStakeIndices.Add(target.Index);
            }

            return new DateeResponseStageResult(dateeResponse, responseDelayMinutes, dateeMessage);
        }
    }
}
