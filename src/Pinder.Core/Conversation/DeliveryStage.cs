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
    internal struct DeliveryStageResult
    {
        public string DeliveredMessage { get; set; }
        public List<TextDiff> TextDiffs { get; set; }
        public SteeringRollResult SteeringResult { get; set; }
        public ShadowCheckResult ShadowCheckResult { get; set; }
        public HorninessCheckResult HorninessCheckResult { get; set; }
        public int HorninessInterestPenalty { get; set; }
        public int HorninessInterestBefore { get; set; }
        public int FinalInterestDelta { get; set; }
        public int ShadowCorrection { get; set; }
    }

    internal class DeliveryStage
    {
        private readonly ILlmAdapter _llm;
        private readonly IRuleResolver? _rules;
        private readonly SteeringEngine _steeringEngine;
        private readonly HorninessEngine _horninessEngine;
        private readonly ShadowCheckEngine _shadowCheckEngine;
        private readonly IStatDeliveryInstructionProvider? _statDeliveryInstructions;
        private readonly Action<TextLayerNoopEvent>? _onTextLayerNoop;
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;
        private readonly int _maxDeliveryWords;

        public DeliveryStage(
            ILlmAdapter llm,
            IRuleResolver? rules,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            IStatDeliveryInstructionProvider? statDeliveryInstructions,
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            Action<OperationalDiagnosticEvent>? onDiagnostic,
            int maxDeliveryWords)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _rules = rules;
            _steeringEngine = steeringEngine ?? throw new ArgumentNullException(nameof(steeringEngine));
            _horninessEngine = horninessEngine ?? throw new ArgumentNullException(nameof(horninessEngine));
            _shadowCheckEngine = shadowCheckEngine ?? throw new ArgumentNullException(nameof(shadowCheckEngine));
            _statDeliveryInstructions = statDeliveryInstructions;
            _onTextLayerNoop = onTextLayerNoop;
            _onDiagnostic = onDiagnostic;
            _maxDeliveryWords = maxDeliveryWords;
        }

        public async Task<DeliveryStageResult> ExecuteAsync(
            GameSessionState state,
            DialogueOption chosenOption,
            RollResult rollResult,
            CharacterProfile player,
            CharacterProfile datee,
            System.IProgress<TurnProgressEvent>? progress,
            int interestDelta,
            CancellationToken ct)
        {
            var textDiffs = new List<TextDiff>();
            string deliveryCallId = OperationalDiagnostics.CreateCallId();
            OperationalDiagnostics.Emit(
                _onDiagnostic,
                new OperationalDiagnosticEvent(
                    "DeliveryStage",
                    "DeliveryStarted",
                    OperationalDiagnosticSeverity.Info,
                    "Delivery operation started.",
                    operationKind: OperationalDiagnosticOperationKind.Delivery,
                    phaseCode: OperationalDiagnosticPhaseCode.Start,
                    lifecycle: OperationalDiagnosticLifecycle.Start,
                    callId: deliveryCallId,
                    correlationHints: new Dictionary<string, string>
                    {
                        ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));

            try
            {
            // 10b. Steering roll
            string originalIntendedText = chosenOption.IntendedText ?? "";
            progress?.Report(new TurnProgressEvent(TurnProgressStage.SteeringStarted));
            SteeringRollResult steeringResult = await _steeringEngine.AttemptSteeringRollAsync(
                originalIntendedText, player, datee, _llm, TurnOrchestratorHelpers.BuildHistoryForLlmContext(state), ct).ConfigureAwait(false);
            progress?.Report(new TurnProgressEvent(
                TurnProgressStage.SteeringCompleted,
                steeringResult.SteeringSucceeded ? steeringResult.SteeringQuestion : null));

            string playerArchetypeDirectiveForDelivery = player.ActiveArchetype?.Directive;
            // #1125: the stat-specific failure instruction and trap/beatDcBy
            // inputs that previously fed the creative delivery PROMPT are gone —
            // there is no delivery LLM call. Failure flavour now reaches the
            // datee via DateeContext.DeliveryTier (set in DateeResponseStage),
            // and the actual text degradation is deterministic (DeliveryOverlay)
            // below. Trap/shadow taint still fire as their own overlays further
            // down (LlmDispatcher), unchanged.

            // #1125 — "DELIVERY" is now a NON-LLM commit/overlay step. Instead of
            // a creative LLM call that expanded a gist and degraded it per the
            // roll, the picked full line is committed verbatim on success and
            // degraded deterministically on a failure tier (parity with the old
            // delivery-LLM degradation, but pure and reproducible). No
            // `delivery`/`DeliverMessageAsync` LLM call fires in the turn, and
            // the avatar session history is NOT written here — option-generation
            // and this commit overlay are ephemeral; only the committed line is
            // persisted (by TurnOrchestrator), preserving the clean-history rule.
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryStarted));
            string? deliveredMessage = null;

            if (!rollResult.IsSuccess)
            {
                string? failureInstruction = HorninessEngine.GetStatFailureInstruction(_statDeliveryInstructions, chosenOption.Stat, rollResult.Tier);
                if (!string.IsNullOrWhiteSpace(failureInstruction) && _llm != null)
                {
                    try
                    {
                        deliveredMessage = await _llm.ApplyFailureCorruptionAsync(
                            originalIntendedText,
                            failureInstruction,
                            chosenOption.Stat,
                            rollResult.Tier,
                            playerArchetypeDirectiveForDelivery,
                            ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!IsRetryableException(ex))
                        {
                            throw;
                        }

                        OperationalDiagnostics.Emit(
                            _onDiagnostic,
                            new OperationalDiagnosticEvent(
                                "DeliveryStage",
                                "FailureCorruptionTransientFailure",
                                OperationalDiagnosticSeverity.Warning,
                                $"Primary LLM failure corruption failed with transient error: {ex.Message}",
                                ex));

                        // Fall back to deterministic DeliveryOverlay
                        deliveredMessage = null;
                    }
                }
            }

            if (deliveredMessage == null || deliveredMessage == originalIntendedText)
            {
                deliveredMessage = DeliveryOverlay.Apply(originalIntendedText, rollResult.Tier, rollResult.MissMargin, chosenOption.Stat);
            }

            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryCompleted, deliveredMessage));

            // #902 / SANITIZATION-INVARIANTS-MUST-RUN-AFTER-EACH-STAGE: meta-prefix
            // strip still runs after the (now deterministic) delivery stage, so a
            // labelling artifact carried in the picked option line is stripped
            // before downstream overlays. Emits a TextDiff layer when it fires.
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, MetaPrefixStripper.LayerName, textDiffs);

            // Tier modifier diff: the deterministic degrade vs the original pre-steering base.
            if (deliveredMessage != originalIntendedText
                && !string.IsNullOrEmpty(originalIntendedText)
                && originalIntendedText != "...")
            {
                string layerLabel = rollResult.IsNatTwenty ? "Nat 20" :
                                    rollResult.IsNatOne    ? "Nat 1"  :
                                    rollResult.Tier == Rolls.FailureTier.Success ? "Strong success" :
                                    rollResult.Tier.ToString();
                var tierSpans = WordDiff.Compute(originalIntendedText, deliveredMessage);
                textDiffs.Add(new TextDiff(layerLabel, tierSpans, originalIntendedText, deliveredMessage));
            }

            // Success Improvement (Strong/Legendary/Nat20)
            bool isSuccess = rollResult.IsSuccess;
            bool isNat20 = rollResult.IsNatTwenty;
            if (isSuccess && !string.IsNullOrWhiteSpace(deliveredMessage) && deliveredMessage.Trim() != "..." && _llm is IStatefulLlmAdapter statefulAdapter)
            {
                int beatDcBy = Math.Max(0, rollResult.FinalTotal - rollResult.DC);
                string tierKey = isNat20 ? "nat20" :
                                 beatDcBy >= 15 ? "exceptional" :
                                 beatDcBy >= 10 ? "critical" :
                                 beatDcBy >= 5  ? "strong" : "clean";

                if (isNat20 || beatDcBy >= 5) // nat20, strong, critical, exceptional
                {
                    string beforeImprovement = deliveredMessage;
                    string improved = null;
                    try
                    {
                        var context = new SuccessImprovementContext(
                            player.AssembledSystemPrompt,
                            datee.DisplayName,
                            player.DisplayName,
                            beforeImprovement,
                            chosenOption.Stat,
                            tierKey,
                            TurnOrchestratorHelpers.BuildHistoryForLlmContext(state));
                        
                        improved = await statefulAdapter.GetSuccessImprovementAsync(context, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (!IsRetryableException(ex))
                        {
                            throw;
                        }

                        OperationalDiagnostics.Emit(
                            _onDiagnostic,
                            new OperationalDiagnosticEvent(
                                "DeliveryStage",
                                "SuccessImprovementTransientFailure",
                                OperationalDiagnosticSeverity.Warning,
                                $"Success improvement failed with transient error: {ex.Message}",
                                ex));

                        // Ignore and fallback
                    }

                    if (improved != null)
                    {
                        improved = improved.Trim();
                        if (SuccessImprovementValidator.IsRejected(improved))
                        {
                            TextLayerNoopDiagnostics.Emit(
                                _onTextLayerNoop,
                                state.TurnNumber,
                                "Success improvement",
                                beforeImprovement,
                                beforeImprovement);
                        }
                        else if (!string.IsNullOrWhiteSpace(improved) && improved != "...")
                        {
                            deliveredMessage = improved;
                        }
                    }

                    if (deliveredMessage != beforeImprovement)
                    {
                        string layerName = isNat20 ? "Nat 20" : (tierKey == "exceptional" ? "Legendary success" : "Strong success");
                        var spans = WordDiff.Compute(beforeImprovement, deliveredMessage);
                        textDiffs.Add(new TextDiff(layerName, spans, beforeImprovement, deliveredMessage));
                    }
                }
            }

            // Steering, when it fires, appends its question to the degraded and sanitized delivery base as its own later layer
            if (steeringResult.SteeringSucceeded && steeringResult.SteeringQuestion != null)
            {
                string beforeSteering = deliveredMessage;
                deliveredMessage = beforeSteering.Length == 0
                    ? steeringResult.SteeringQuestion
                    : beforeSteering.TrimEnd() + " " + steeringResult.SteeringQuestion;

                if (deliveredMessage != beforeSteering
                    && !string.IsNullOrEmpty(beforeSteering)
                    && beforeSteering != "...")
                {
                    var steeringSpans = WordDiff.Compute(beforeSteering, deliveredMessage);
                    textDiffs.Add(new TextDiff("Steering", steeringSpans, beforeSteering, deliveredMessage));
                }
            }

            // ---- Speculative Overlay Dispatcher ----
            bool runTrap = state.Traps.HasActive && rollResult.ActivatedTrap == null;
            string trapInstruction = "";
            string trapDisplayName = "";
            string dateeCtxForTrap = "";

            if (runTrap)
            {
                var activeTrap = state.Traps.Active!;
                trapInstruction = activeTrap.Definition.LlmInstruction;
                trapDisplayName = activeTrap.Definition.DisplayName;
                dateeCtxForTrap = TurnOrchestratorHelpers.BuildDateeContext(datee);

                if (string.IsNullOrWhiteSpace(trapInstruction)
                    || string.IsNullOrEmpty(deliveredMessage)
                    || deliveredMessage == "...")
                {
                    runTrap = false;
                }
            }

            // #755: Shadow check
            ShadowStatType? pairedShadow = TurnOrchestratorHelpers.GetPairedShadow(chosenOption.Stat);
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
                dateeCtxForTrap,
                runShadow,
                corruptionInstruction,
                pairedShadow ?? ShadowStatType.Dread,
                playerArchetypeDirectiveForDelivery,
                textDiffs,
                _onTextLayerNoop,
                state.TurnNumber,
                progress,
                ct,
                state.SpeculativeWasteTracker,
                _onDiagnostic).ConfigureAwait(false);

            deliveredMessage = dispatchResult.FinalMessage;
            bool shadowOverlayApplied = dispatchResult.ShadowOverlayApplied;

            int shadowCorrection = 0;
            if (pairedShadow.HasValue && state.PlayerShadows != null)
            {
                int shadowValue = state.PlayerShadows.GetEffectiveShadow(pairedShadow.Value);
                if (shadowValue > 0)
                {
                    if (shadowMiss)
                    {
                        if (shadowOverlayApplied && rollResult.IsSuccess)
                        {
                            // #1095: A shadow trap (success roll + paired-shadow MISS with overlay)
                            // no longer demotes the turn to a forced FAILURE. Instead it TRUNCATES
                            // the positive interest delta to a maximum of 1 ("tainted, capped").
                            // The roll verdict stays SUCCESS (no ApplyFinalOverride to Miss in
                            // TurnOrchestrator), momentum keeps incrementing, and success-gated
                            // downstream effects stay on the success path. The DATEE reacts
                            // neutrally / slightly negatively, not with a hard-rejection failure beat.
                            // shadowCorrection stays the signed adjustment so the central
                            // state.Interest.Apply(ShadowCorrection) remains correct.
                            int truncated = Math.Min(interestDelta, 1);
                            shadowCorrection = truncated - interestDelta;
                            interestDelta = truncated;
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

            // #1209: Horniness append
            if (horninessCheckResult.IsMiss && _llm is IStatefulLlmAdapter horninessStateful && !string.IsNullOrWhiteSpace(deliveredMessage) && deliveredMessage.Trim() != "...")
            {
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayStarted));
                string beforeHorniness = deliveredMessage;
                string question = null;
                try
                {
                    question = await horninessStateful.GetHorninessQuestionAsync(new HorninessQuestionContext(player.AssembledSystemPrompt, datee.DisplayName, player.DisplayName, beforeHorniness, TurnOrchestratorHelpers.BuildHistoryForLlmContext(state)), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    OperationalDiagnostics.Emit(
                        _onDiagnostic,
                        new OperationalDiagnosticEvent(
                            "DeliveryStage",
                            "HorninessQuestionFailure",
                            OperationalDiagnosticSeverity.Warning,
                            $"Horniness question generation failed on turn {state.TurnNumber}: {ex.Message}",
                            ex));

                    // Preserve existing gameplay fallback: continue the turn without an appended question.
                    question = null;
                }
                
                if (!string.IsNullOrWhiteSpace(question))
                {
                    deliveredMessage = beforeHorniness.Length == 0 ? question : beforeHorniness.TrimEnd() + " " + question;
                    if (deliveredMessage != beforeHorniness)
                    {
                        var sp = WordDiff.Compute(beforeHorniness, deliveredMessage);
                        textDiffs.Add(new TextDiff("Horniness", sp, beforeHorniness, deliveredMessage));
                    }
                }
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayCompleted, deliveredMessage));
            }

            if (horninessCheckResult.OverlayApplied && interestDelta > 0)
            {
                horninessInterestBefore = state.Interest.Current + shadowCorrection;
                int halvedDelta = (int)Math.Floor(interestDelta / 2.0);
                int penalty = halvedDelta - interestDelta;
                horninessInterestPenalty = penalty;
                interestDelta += penalty;
            }

            // Issue #339: same-turn callback-phrase strip
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, CallbackStripper.LayerName, textDiffs);

            // #1041 (Tier C): markdown-stripping pass for surfaces that expect plain prose.
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, MarkdownSanitizer.LayerName, textDiffs);

            OperationalDiagnostics.Emit(
                _onDiagnostic,
                new OperationalDiagnosticEvent(
                    "DeliveryStage",
                    "DeliverySucceeded",
                    OperationalDiagnosticSeverity.Info,
                    "Delivery operation succeeded.",
                    operationKind: OperationalDiagnosticOperationKind.Delivery,
                    phaseCode: OperationalDiagnosticPhaseCode.Completed,
                    lifecycle: OperationalDiagnosticLifecycle.Terminal,
                    outcome: OperationalDiagnosticOutcome.Succeeded,
                    callId: deliveryCallId,
                    correlationHints: new Dictionary<string, string>
                    {
                        ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));

            return new DeliveryStageResult
            {
                DeliveredMessage = deliveredMessage,
                TextDiffs = textDiffs,
                SteeringResult = steeringResult,
                ShadowCheckResult = shadowCheckResult,
                HorninessCheckResult = horninessCheckResult,
                HorninessInterestPenalty = horninessInterestPenalty,
                HorninessInterestBefore = horninessInterestBefore,
                FinalInterestDelta = interestDelta,
                ShadowCorrection = shadowCorrection
            };
            }
            catch (OperationCanceledException ex)
            {
                OperationalDiagnostics.Emit(
                    _onDiagnostic,
                    new OperationalDiagnosticEvent(
                        "DeliveryStage",
                        "DeliveryCancelled",
                        OperationalDiagnosticSeverity.Warning,
                        "Delivery operation was cancelled.",
                        ex,
                        OperationalDiagnosticOperationKind.Delivery,
                        OperationalDiagnosticPhaseCode.Completed,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Cancelled,
                        OperationalDiagnosticFailureClassification.Cancelled,
                        callId: deliveryCallId,
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
                        "DeliveryStage",
                        "DeliveryFailed",
                        OperationalDiagnosticSeverity.Error,
                        "Delivery operation failed.",
                        ex,
                        OperationalDiagnosticOperationKind.Delivery,
                        OperationalDiagnosticPhaseCode.Completed,
                        OperationalDiagnosticLifecycle.Terminal,
                        OperationalDiagnosticOutcome.Failed,
                        OperationalDiagnostics.ClassifyException(ex),
                        callId: deliveryCallId,
                        correlationHints: new Dictionary<string, string>
                        {
                            ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        }));
                throw;
            }
        }

        private static bool IsRetryableException(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                return false;
            }
            if (ex is TimeoutException)
            {
                return true;
            }
            if (ex is System.Net.Http.HttpRequestException)
            {
                return true;
            }
            if (ex is LlmTransportException transportEx)
            {
                return transportEx.FailureKind == LlmFailureKind.RateLimited ||
                       transportEx.FailureKind == LlmFailureKind.Network;
            }

            string msg = ex.Message ?? "";
            if (msg.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("503", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("thrott", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("service unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("temporary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("transient", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("LLM failure", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

    }
}
