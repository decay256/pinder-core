using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    internal partial class TurnOrchestrator
    {
        private struct DeliveryStageResult
        {
            public string DeliveredMessage { get; set; }
            public List<TextDiff> TextDiffs { get; set; }
            public SteeringRollResult SteeringResult { get; set; }
            public ShadowCheckResult ShadowCheckResult { get; set; }
            public HorninessCheckResult HorninessCheckResult { get; set; }
            public int HorninessInterestPenalty { get; set; }
            public int HorninessInterestBefore { get; set; }
            public int FinalInterestDelta { get; set; }
        }

        private async Task<DeliveryStageResult> ExecuteDeliveryStageAsync(
            GameSessionState state,
            DialogueOption chosenOption,
            RollResult rollResult,
            CharacterProfile player,
            CharacterProfile opponent,
            System.IProgress<TurnProgressEvent>? progress,
            int interestDelta,
            CancellationToken ct)
        {
            var textDiffs = new List<TextDiff>();

            // 6. Deliver message via LLM
            var deliveryTrapNames = GameSessionHelpers.GetActiveTrapNames(state.Traps);
            var deliveryTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            int beatDcBy = rollResult.IsSuccess ? rollResult.FinalTotal - rollResult.DC : 0;

            // Resolve stat-specific failure instruction when the roll failed (#695)
            string? statFailureInstruction = null;
            if (!rollResult.IsSuccess && _statDeliveryInstructions != null)
            {
                statFailureInstruction = HorninessEngine.GetStatFailureInstruction(
                    _statDeliveryInstructions, chosenOption.Stat, rollResult.Tier);
            }

            // 10b. Steering roll
            string originalIntendedText = chosenOption.IntendedText ?? "";
            progress?.Report(new TurnProgressEvent(TurnProgressStage.SteeringStarted));
            SteeringRollResult steeringResult = await _steeringEngine.AttemptSteeringRollAsync(
                originalIntendedText, player, opponent, _llm, BuildHistoryForLlmContext(state), ct).ConfigureAwait(false);
            progress?.Report(new TurnProgressEvent(
                TurnProgressStage.SteeringCompleted,
                steeringResult.SteeringSucceeded ? steeringResult.SteeringQuestion : null));

            string intendedTextForDelivery = originalIntendedText;
            DialogueOption deliveryOption = chosenOption;
            if (steeringResult.SteeringSucceeded && steeringResult.SteeringQuestion != null)
            {
                intendedTextForDelivery = originalIntendedText.Length == 0
                    ? steeringResult.SteeringQuestion
                    : originalIntendedText.TrimEnd() + " " + steeringResult.SteeringQuestion;

                if (intendedTextForDelivery != originalIntendedText
                    && !string.IsNullOrEmpty(originalIntendedText)
                    && originalIntendedText != "...")
                {
                    var steeringSpans = WordDiff.Compute(originalIntendedText, intendedTextForDelivery);
                    textDiffs.Add(new TextDiff("Steering", steeringSpans, originalIntendedText, intendedTextForDelivery));
                }

                deliveryOption = new DialogueOption(
                    chosenOption.Stat,
                    intendedTextForDelivery,
                    chosenOption.CallbackTurnNumber,
                    chosenOption.ComboName,
                    chosenOption.HasTellBonus,
                    chosenOption.HasWeaknessWindow,
                    chosenOption.IsUnhingedReplacement);
            }

            string playerArchetypeDirectiveForDelivery = player.ActiveArchetype?.Directive;

            var deliveryContext = new DeliveryContext(
                playerPrompt: player.AssembledSystemPrompt,
                opponentPrompt: opponent.AssembledSystemPrompt,
                conversationHistory: BuildHistoryForLlmContext(state),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(state.History, opponent.DisplayName),
                chosenOption: deliveryOption,
                outcome: rollResult.Tier,
                beatDcBy: beatDcBy,
                activeTraps: deliveryTrapNames,
                activeTrapInstructions: deliveryTrapInstructions,
                playerName: player.DisplayName,
                opponentName: opponent.DisplayName,
                currentTurn: state.TurnNumber,
                shadowThresholds: state.CurrentShadowThresholds,
                isNat20: rollResult.IsNatTwenty,
                statFailureInstruction: statFailureInstruction,
                activeArchetypeDirective: playerArchetypeDirectiveForDelivery);

            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryStarted));
            string deliveredMessage = await _llm.DeliverMessageAsync(deliveryContext, ct).ConfigureAwait(false);
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryCompleted, deliveredMessage));

            // #902: Meta-prefix strip immediately after delivery LLM call.
            {
                string rawDelivered = deliveredMessage;
                deliveredMessage = MetaPrefixStripper.Strip(rawDelivered);
                if (deliveredMessage != rawDelivered)
                {
                    var stripSpans = WordDiff.Compute(rawDelivered, deliveredMessage);
                    textDiffs.Add(new TextDiff(
                        MetaPrefixStripper.LayerName, stripSpans,
                        rawDelivered, deliveredMessage));
                }
            }

            // Tier modifier diff
            if (deliveredMessage != intendedTextForDelivery
                && !string.IsNullOrEmpty(intendedTextForDelivery)
                && intendedTextForDelivery != "...")
            {
                string layerLabel = rollResult.IsNatTwenty ? "Nat 20" :
                                    rollResult.IsNatOne    ? "Nat 1"  :
                                    rollResult.Tier == Rolls.FailureTier.Success ? "Strong success" :
                                    rollResult.Tier.ToString();
                var tierSpans = WordDiff.Compute(intendedTextForDelivery, deliveredMessage);
                textDiffs.Add(new TextDiff(layerLabel, tierSpans, intendedTextForDelivery, deliveredMessage));
            }

            // ---- Speculative Overlay Dispatcher ----
            bool runTrap = state.Traps.HasActive && rollResult.ActivatedTrap == null;
            string trapInstruction = "";
            string trapDisplayName = "";
            string opponentCtxForTrap = "";

            if (runTrap)
            {
                var activeTrap = state.Traps.Active!;
                trapInstruction = activeTrap.Definition.LlmInstruction;
                trapDisplayName = activeTrap.Definition.DisplayName;
                opponentCtxForTrap = BuildOpponentContext(opponent);

                if (string.IsNullOrWhiteSpace(trapInstruction)
                    || string.IsNullOrEmpty(deliveredMessage)
                    || deliveredMessage == "...")
                {
                    runTrap = false;
                }
            }

            // #755: Shadow check
            ShadowStatType? pairedShadow = GetPairedShadow(chosenOption.Stat);
            ShadowCheckResult shadowCheckResult = ShadowCheckResult.NotPerformed;

            bool runShadow = false;
            string corruptionInstruction = "";
            int shadowRoll = 0;
            int shadowDC = 0;
            bool shadowMiss = false;
            FailureTier shadowTier = FailureTier.Success;
            RollCheckResult? rawShadowCheck = null;

            if (pairedShadow.HasValue && state.PlayerShadows != null)
            {
                int shadowValue = state.PlayerShadows.GetEffectiveShadow(pairedShadow.Value);
                if (shadowValue > 0)
                {
                    var rawShadowResult = _shadowCheckEngine.Check(pairedShadow.Value, shadowValue);
                    shadowRoll = rawShadowResult.Roll;
                    shadowDC   = rawShadowResult.DC;
                    shadowMiss = rawShadowResult.IsMiss;
                    rawShadowCheck = rawShadowResult.Check;

                    if (shadowMiss)
                    {
                        shadowTier = rawShadowResult.Tier;
                        string? instruction = HorninessEngine.GetShadowCorruptionInstruction(
                            _statDeliveryInstructions, pairedShadow.Value, shadowTier);

                        if (instruction != null)
                        {
                            runShadow = true;
                            corruptionInstruction = instruction;
                        }
                    }
                }
            }

            // Dispatch speculative LLM calls in parallel
            var dispatchResult = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                _llm,
                deliveredMessage,
                runTrap,
                trapInstruction,
                trapDisplayName,
                opponentCtxForTrap,
                runShadow,
                corruptionInstruction,
                pairedShadow ?? ShadowStatType.Dread,
                playerArchetypeDirectiveForDelivery,
                textDiffs,
                _onTextLayerNoop,
                state.TurnNumber,
                progress,
                ct).ConfigureAwait(false);

            deliveredMessage = dispatchResult.FinalMessage;
            bool shadowOverlayApplied = dispatchResult.ShadowOverlayApplied;

            if (pairedShadow.HasValue && state.PlayerShadows != null)
            {
                int shadowValue = state.PlayerShadows.GetEffectiveShadow(pairedShadow.Value);
                if (shadowValue > 0)
                {
                    if (shadowMiss)
                    {
                        if (shadowOverlayApplied && rollResult.IsSuccess)
                        {
                            var forcedFailResult = CreateForcedFailResult(rollResult, shadowTier);
                            int shadowFailDelta = ResolveFailureInterestDelta(forcedFailResult, _rules);
                            int correction = shadowFailDelta - interestDelta;
                            state.Interest.Apply(correction);
                            interestDelta = shadowFailDelta;

                            rollResult.Check.ApplyFinalOverride(
                                Pinder.Core.Rolls.RollVerdict.Miss,
                                shadowTier);
                        }

                        shadowCheckResult = new ShadowCheckResult(
                            true, pairedShadow.Value, shadowRoll, shadowDC, true, shadowTier, shadowOverlayApplied,
                            rawShadowCheck);
                    }
                    else
                    {
                        shadowCheckResult = new ShadowCheckResult(
                            true, pairedShadow.Value, shadowRoll, shadowDC, false, FailureTier.Success, false,
                            rawShadowCheck);
                    }
                }
            }

            string? horninessOverlayInstruction;
            HorninessCheckResult horninessCheckResult;
            (horninessCheckResult, horninessOverlayInstruction) = _horninessEngine.PeekAsync(
                state.SessionHorniness,
                state.PlayerShadows,
                _statDeliveryInstructions,
                ct);

            int horninessInterestPenalty = 0;
            int horninessInterestBefore = 0;

            // #899: Horniness TEXT OVERLAY
            if (horninessOverlayInstruction != null)
            {
                string beforeHorniness = deliveredMessage;
                string opponentCtx = BuildOpponentContext(opponent);
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayStarted));
                string rawHorninessOutput = await _llm.ApplyHorninessOverlayAsync(deliveredMessage, horninessOverlayInstruction, opponentCtx, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayCompleted, rawHorninessOutput));

                string sanitizedHorninessOutput = MetaPrefixStripper.Strip(rawHorninessOutput);
                if (sanitizedHorninessOutput != rawHorninessOutput)
                {
                    var stripSpans = WordDiff.Compute(rawHorninessOutput, sanitizedHorninessOutput);
                    textDiffs.Add(new TextDiff(
                        MetaPrefixStripper.LayerName, stripSpans,
                        rawHorninessOutput, sanitizedHorninessOutput));
                }

                deliveredMessage = sanitizedHorninessOutput;

                if (deliveredMessage != beforeHorniness)
                {
                    var horninessSpans = WordDiff.Compute(beforeHorniness, deliveredMessage);
                    textDiffs.Add(new TextDiff("Horniness", horninessSpans, beforeHorniness, deliveredMessage));
                }
                else
                {
                    EmitTextLayerNoop(_onTextLayerNoop, state.TurnNumber, "Horniness", beforeHorniness, deliveredMessage);
                }
            }

            if (horninessCheckResult.OverlayApplied && interestDelta > 0)
            {
                horninessInterestBefore = state.Interest.Current;
                int halvedDelta = (int)Math.Floor(interestDelta / 2.0);
                int penalty = halvedDelta - interestDelta;
                state.Interest.Apply(penalty);
                horninessInterestPenalty = penalty;
                interestDelta += penalty;
            }

            // Issue #339: same-turn callback-phrase strip
            {
                string beforeCallbackStrip = deliveredMessage;
                string strippedMessage = CallbackStripper.Strip(beforeCallbackStrip);
                if (!ReferenceEquals(strippedMessage, beforeCallbackStrip)
                    && strippedMessage != beforeCallbackStrip)
                {
                    deliveredMessage = strippedMessage;
                    var stripSpans = WordDiff.Compute(beforeCallbackStrip, deliveredMessage);
                    textDiffs.Add(new TextDiff(
                        CallbackStripper.LayerName, stripSpans,
                        beforeCallbackStrip, deliveredMessage));
                }
            }

            return new DeliveryStageResult
            {
                DeliveredMessage = deliveredMessage,
                TextDiffs = textDiffs,
                SteeringResult = steeringResult,
                ShadowCheckResult = shadowCheckResult,
                HorninessCheckResult = horninessCheckResult,
                HorninessInterestPenalty = horninessInterestPenalty,
                HorninessInterestBefore = horninessInterestBefore,
                FinalInterestDelta = interestDelta
            };
        }
    }
}
