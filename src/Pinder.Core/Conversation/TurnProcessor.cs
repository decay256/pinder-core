using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.I18n;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    internal static class TurnProcessor
    {
        internal static async Task<TurnStart> StartTurnAsync(
            GameSessionState state,
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IGameClock? clock,
            IRuleResolver? rules,
            object? statDeliveryInstructions,
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            Random? statDrawRng,
            int globalDcBias,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // Check if game already ended
            if (state.Ended)
                throw new GameEndedException(state.Outcome!.Value);

            // Check end conditions: interest at 0 or 25
            if (state.Interest.IsZero)
            {
                if (state.PlayerShadows != null)
                {
                    var dreadEvents = new[] { $"{ShadowStatType.Dread} +1 (Conversation ended without date)" };
                    var dreadEffects = new[] { new ShadowGrowthEffect(ShadowStatType.Dread, 1, "Conversation ended without date") };
                    throw new GameEndedException(GameOutcome.Unmatched, dreadEvents, dreadEffects);
                }
                throw new GameEndedException(GameOutcome.Unmatched);
            }

            if (state.Interest.IsMaxed)
            {
                throw new GameEndedException(GameOutcome.DateSecured);
            }

            // Ghost trigger: if Bored state, 25% chance per turn
            if (ResolveInterestState(state, rules) == InterestState.Bored)
            {
                int ghostRoll = dice.Roll(4);
                if (ghostRoll == 1)
                {
                    if (state.PlayerShadows != null)
                    {
                        var events = new[] { $"{ShadowStatType.Dread} +1 (Ghosted)" };
                        var effects = new[] { new ShadowGrowthEffect(ShadowStatType.Dread, 1, "Ghosted") };
                        throw new GameEndedException(GameOutcome.Ghosted, events, effects);
                    }

                    throw new GameEndedException(GameOutcome.Ghosted);
                }
            }

            // Determine advantage/disadvantage from interest state + traps
            bool hasAdvantage = state.Interest.GrantsAdvantage;
            bool hasDisadvantage = state.Interest.GrantsDisadvantage;

            // Nat 20 crit advantage (#271) — previous crit grants advantage for 1 roll
            if (state.PendingCritAdvantage)
            {
                hasAdvantage = true;
                state.PendingCritAdvantage = false;
            }

            // Store for ResolveTurnAsync
            state.CurrentHasAdvantage = hasAdvantage;
            state.CurrentHasDisadvantage = hasDisadvantage;

            // Shadow threshold evaluation (#45)
            Dictionary<ShadowStatType, int>? shadowThresholds = null;
            state.ShadowDisadvantagedStats = null;

            if (state.PlayerShadows != null)
            {
                shadowThresholds = new Dictionary<ShadowStatType, int>();
                state.ShadowDisadvantagedStats = new HashSet<StatType>();

                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    int effectiveVal = state.PlayerShadows.GetEffectiveShadow(shadow);
                    shadowThresholds[shadow] = effectiveVal;
                    int tier = ResolveThresholdLevel(effectiveVal, rules);
                    // T2+ disadvantage for paired stats is removed: shadow check IS the disadvantage (#755)
                    _ = tier; // suppress unused warning
                }
            }

            // Store player shadow thresholds for use in ResolveTurnAsync (#308)
            state.CurrentShadowThresholds = shadowThresholds;

            // Get trap names and LLM instructions for context
            var activeTrapNames = GameSessionHelpers.GetActiveTrapNames(state.Traps);
            var activeTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            // Build dialogue context — pass callback topics (#47) and shadow thresholds (#45)
            string playerArchetypeDirective = player.ActiveArchetype?.Directive;

            // Draw 3 random stats for this turn's options
            var allStats = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var availableStats = OptionFilterEngine.DrawRandomStats(allStats, 3, shadowThresholds, statDrawRng);

            var context = new DialogueContext(
                playerPrompt: player.AssembledSystemPrompt,
                opponentPrompt: GameSessionHelpers
                    .BuildOpponentVisibleProfile(opponent, state.OpponentOutfitDescription)
                    .Render(),
                // #333: scene entries are excluded from the LLM context view.
                conversationHistory: BuildHistoryForLlmContext(state),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(state.History, opponent.DisplayName),
                activeTraps: activeTrapNames,
                currentInterest: state.Interest.Current,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                callbackOpportunities: state.Topics.Count > 0 ? new List<CallbackOpportunity>(state.Topics) : null,
                horninessLevel: state.SessionHorniness,
                requiresRizzOption: false,
                currentTurn: state.TurnNumber,
                playerTextingStyle: player.TextingStyleFragment,
                activeTell: state.ActiveTell,
                activeArchetypeDirective: playerArchetypeDirective,
                availableStats: availableStats);

            // Get dialogue options from LLM
            var rawOptions = await llm.GetDialogueOptionsAsync(context, ct).ConfigureAwait(false);

            // Peek combos for each option (#46), enrich with weakness window (#49) and tell bonus (#50)
            var options = new DialogueOption[rawOptions.Length];
            for (int i = 0; i < rawOptions.Length; i++)
            {
                var opt = rawOptions[i];
                string? comboName = state.ComboTracker.PeekCombo(opt.Stat);
                bool hasWeaknessWindow = state.ActiveWeakness != null
                    && StatBlock.DefenceTable[opt.Stat] == state.ActiveWeakness.DefendingStat;
                bool hasTellBonus = state.ActiveTell != null && opt.Stat == state.ActiveTell.Stat;
                options[i] = new DialogueOption(
                    opt.Stat,
                    opt.IntendedText,
                    opt.CallbackTurnNumber,
                    comboName,
                    hasTellBonus,
                    hasWeaknessWindow);
            }

            // T3 option filtering (#45)
            if (state.PlayerShadows != null && shadowThresholds != null)
            {
                options = OptionFilterEngine.ApplyT3Filters(options, shadowThresholds, state.LastStatUsed, dice);
            }

            state.CurrentOptions = options;

            // Compute pending momentum bonus for the upcoming roll (#268)
            state.PendingMomentumBonus = GetMomentumBonus(state.MomentumStreak, rules);

            state.CurrentDicePools = new Pinder.Core.Rolls.PerOptionDicePool[options.Length];
            for (int i = 0; i < options.Length; i++)
                state.CurrentDicePools[i] = new Pinder.Core.Rolls.PerOptionDicePool(i);

            var snapshot = CreateSnapshot(state, rules);

            // #903 — build opponent defense snapshot (6 entries, one per StatType).
            var defenseEntries = new System.Collections.Generic.Dictionary<Pinder.Core.Stats.StatType, OpponentDefenseEntry>();
            foreach (Pinder.Core.Stats.StatType attackerStat in System.Enum.GetValues(typeof(Pinder.Core.Stats.StatType)))
            {
                var defenderStat = Pinder.Core.Stats.StatBlock.DefenceTable[attackerStat];
                int baseModifier = opponent.Stats.GetBase(defenderStat);
                int effectiveModifier = opponent.Stats.GetEffective(defenderStat);

                // Include any active OpponentDCIncrease trap bonus for this attacker stat.
                var activeTrap = state.Traps.GetActive(attackerStat);
                if (activeTrap != null && activeTrap.Definition.Effect == Pinder.Core.Traps.TrapEffect.OpponentDCIncrease)
                    effectiveModifier += activeTrap.Definition.EffectValue;

                defenseEntries[attackerStat] = new OpponentDefenseEntry(defenderStat, effectiveModifier, baseModifier);
            }
            var defenseSnapshot = new OpponentDefenseSnapshot(
                new System.Collections.ObjectModel.ReadOnlyDictionary<Pinder.Core.Stats.StatType, OpponentDefenseEntry>(defenseEntries));

            // #593: expose the active weakness window's DC reduction so the frontend
            // can render the magnitude on its FoldableHintBanner.
            int? weaknessDcReduction = state.ActiveWeakness?.DcReduction;

            return new TurnStart(options, snapshot, state.CurrentDicePools, defenseSnapshot, weaknessDcReduction);
        }

        internal static async Task<TurnResult> ResolveTurnAsync(
            GameSessionState state,
            int optionIndex,
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IRuleResolver? rules,
            IConsequenceCatalog? consequenceCatalog,
            ShadowGrowthEvaluator? shadowGrowthEvaluator,
            SessionXpRecorder xpRecorder,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct)
        {
            return await ResolveTurnAsync(
                state,
                optionIndex,
                player,
                opponent,
                llm,
                dice,
                trapRegistry,
                rules,
                consequenceCatalog,
                shadowGrowthEvaluator,
                xpRecorder,
                steeringEngine,
                horninessEngine,
                shadowCheckEngine,
                progress,
                statDeliveryInstructions: null,
                onTextLayerNoop: null,
                globalDcBias: 0,
                ct).ConfigureAwait(false);
        }

        internal static async Task<TurnResult> ResolveTurnAsync(
            GameSessionState state,
            int optionIndex,
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IRuleResolver? rules,
            IConsequenceCatalog? consequenceCatalog,
            ShadowGrowthEvaluator? shadowGrowthEvaluator,
            SessionXpRecorder xpRecorder,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            System.IProgress<TurnProgressEvent>? progress,
            object? statDeliveryInstructions,
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            int globalDcBias,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (state.Ended)
                throw new GameEndedException(state.Outcome!.Value);

            if (state.CurrentOptions == null)
                throw new InvalidOperationException("Must call StartTurnAsync before ResolveTurnAsync.");

            if (optionIndex < 0 || optionIndex >= state.CurrentOptions.Length)
                throw new ArgumentOutOfRangeException(nameof(optionIndex),
                    $"Option index {optionIndex} is out of range. Valid range: 0–{state.CurrentOptions.Length - 1}.");

            var chosenOption = state.CurrentOptions[optionIndex];

            // ---- Trap SA-disarm (issue #371) ----
            string? trapClearedDisplayName = null;
            if (chosenOption.Stat == StatType.SelfAwareness && state.Traps.HasActive)
            {
                trapClearedDisplayName = state.Traps.Active!.Definition.DisplayName;
                state.Traps.Clear();
            }

            // Denial +1 when Honesty was available but player chose a different stat (#272 — §7)
            if (state.PlayerShadows != null
                && chosenOption.Stat != StatType.Honesty
                && state.CurrentOptions.Any(o => o.Stat == StatType.Honesty))
            {
                state.PlayerShadows.ApplyGrowth(ShadowStatType.Denial, 1,
                    "Skipped Honesty option");
            }

            // Compute callback bonus (#47)
            int callbackBonus = 0;
            if (chosenOption.CallbackTurnNumber.HasValue)
            {
                callbackBonus = CallbackBonus.Compute(state.TurnNumber, chosenOption.CallbackTurnNumber.Value);
            }

            // Compute tell bonus (#50)
            bool hasTellOption = state.ActiveTell != null && chosenOption.Stat == state.ActiveTell.Stat;
            int tellBonus = hasTellOption ? 2 : 0;

            // Compute external bonus: tell + callback + Triple combo + momentum (#46, #47, #50, #268)
            int externalBonus = tellBonus + callbackBonus + state.PendingMomentumBonus;
            int tripleBonusApplied = 0;
            if (state.ComboTracker.HasTripleBonus)
            {
                tripleBonusApplied = 1;
                externalBonus += tripleBonusApplied;
                state.ComboTracker.ConsumeTripleBonus(); // Consume after applying (#46 edge case 7)
            }

            // Compute DC adjustment from weakness window (#49) + global difficulty bias
            int dcAdjustment = 0;
            if (state.ActiveWeakness != null
                && StatBlock.DefenceTable[chosenOption.Stat] == state.ActiveWeakness.DefendingStat)
            {
                dcAdjustment = state.ActiveWeakness.DcReduction;
            }
            if (globalDcBias != 0)
                dcAdjustment -= globalDcBias;

            // Clear weakness window — consumed this turn regardless of match (#49)
            state.ActiveWeakness = null;

            // Clear active tell — consumed this turn regardless of match (#50)
            state.ActiveTell = null;

            // Shadow threshold per-stat disadvantage (#45)
            bool resolveHasDisadvantage = state.CurrentHasDisadvantage;
            if (state.ShadowDisadvantagedStats != null && state.ShadowDisadvantagedStats.Contains(chosenOption.Stat))
            {
                resolveHasDisadvantage = true;
            }

            // 1. Roll dice
            IDiceRoller resolveDice;
            RollResult rollResult;
            (rollResult, resolveDice) = TurnDiceEvaluator.EvaluateRolls(
                state,
                optionIndex,
                chosenOption,
                player,
                opponent,
                dice,
                trapRegistry,
                consequenceCatalog,
                externalBonus,
                dcAdjustment,
                resolveHasDisadvantage);

            // 2. Compute interest delta from roll outcome
            int baseInterestDelta;
            int riskBonusDelta = 0;
            if (rollResult.IsSuccess)
            {
                baseInterestDelta = ResolveSuccessInterestDelta(rollResult, rules);
                riskBonusDelta = RiskTierBonus.GetInterestBonus(rollResult);
            }
            else
            {
                baseInterestDelta = ResolveFailureInterestDelta(rollResult, rules);
            }
            int interestDelta = baseInterestDelta + riskBonusDelta;

            // 3. Update momentum streak
            state.PendingMomentumBonus = 0;
            if (rollResult.IsSuccess)
            {
                state.MomentumStreak++;
            }
            else
            {
                state.MomentumStreak = 0;
            }

            // 3b. Nat 20 crit advantage (#271) — set for next roll
            if (rollResult.IsNatTwenty)
            {
                state.PendingCritAdvantage = true;

                // Nat 20 on CHAOS → Madness −1
                if (chosenOption.Stat == StatType.Chaos)
                {
                    state.PlayerShadows?.ApplyOffset(ShadowStatType.Madness, -1,
                        "Nat 20 on Chaos — chaos mastered, not consumed");
                }

                // Nat 20 (any stat) → Dread −1 (#720)
                state.PlayerShadows?.ApplyOffset(ShadowStatType.Dread, -1,
                    "Nat 20 — existential confidence surge");
            }

            // 3c. Track last stat used for Fixation T3 (#45)
            state.LastStatUsed = chosenOption.Stat;

            // 3d. Combo detection (#46)
            state.ComboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess);
            var combo = state.ComboTracker.CheckCombo();
            string? comboTriggered = null;
            int comboBonusDelta = 0;
            if (combo != null)
            {
                comboBonusDelta = combo.InterestBonus;
                interestDelta += comboBonusDelta;
                comboTriggered = combo.Name;
            }

            // 3d. Record roll XP (#48)
            xpRecorder.RecordRollXp(rollResult);

            // 4. Record interest before applying delta
            int interestBefore = state.Interest.Current;
            InterestState stateBefore = ResolveInterestState(state, rules);

            // 5. Apply interest delta
            state.Interest.Apply(interestDelta);

            int interestAfter = state.Interest.Current;
            InterestState stateAfter = ResolveInterestState(state, rules);

            // ---- Shadow growth evaluation (#44) ----
            shadowGrowthEvaluator?.EvaluatePerTurn(
                chosenOption, optionIndex, rollResult, interestAfter, comboTriggered, hasTellOption,
                state.CurrentOptions,
                (chosen, opts) => GameSessionHelpers.IsHighestProbabilityOption(chosen, opts, player, opponent));

            // Shadow reduction: Winning despite Overthinking disadvantage → Overthinking −1
            if (rollResult.IsSuccess
                && state.PlayerShadows != null
                && state.ShadowDisadvantagedStats != null
                && state.ShadowDisadvantagedStats.Contains(chosenOption.Stat)
                && StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking)
            {
                state.PlayerShadows.ApplyOffset(ShadowStatType.Overthinking, -1,
                    "Succeeded despite Overthinking disadvantage");
            }

            // Shadow reduction: Success at interest ≥20 → Overthinking -1
            if (rollResult.IsSuccess && interestAfter >= 20)
            {
                state.PlayerShadows?.ApplyOffset(ShadowStatType.Overthinking, -1,
                    "Success at high interest \u2014 pressure lifts");
            }

            // Check end conditions for end-of-game triggers
            bool isGameOver = false;
            GameOutcome? outcome = null;

            if (state.Interest.IsZero)
            {
                state.Ended = true;
                state.Outcome = GameOutcome.Unmatched;
                isGameOver = true;
                outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date
                state.PlayerShadows?.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
            }
            else if (state.Interest.IsMaxed)
            {
                state.Ended = true;
                state.Outcome = GameOutcome.DateSecured;
                isGameOver = true;
                outcome = GameOutcome.DateSecured;
            }

            // End-of-game shadow growth checks
            if (isGameOver)
            {
                shadowGrowthEvaluator?.EvaluateEndOfGame(outcome!.Value);
                xpRecorder.RecordEndOfGameXp(outcome!.Value);
            }

            // Drain XP events for this turn (#48)
            var turnXpEvents = state.XpLedger.DrainTurnEvents();
            int turnXpEarned = 0;
            for (int i = 0; i < turnXpEvents.Count; i++)
                turnXpEarned += turnXpEvents[i].Amount;

            // Drain shadow growth events for this turn
            var shadowGrowthEvents = state.PlayerShadows != null
                ? state.PlayerShadows.DrainGrowthEvents()
                : (IReadOnlyList<string>)Array.Empty<string>();

            // 6. Deliver message via LLM
            var deliveryTrapNames = GameSessionHelpers.GetActiveTrapNames(state.Traps);
            var deliveryTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            int beatDcBy = rollResult.IsSuccess ? rollResult.FinalTotal - rollResult.DC : 0;

            // Resolve stat-specific failure instruction when the roll failed (#695)
            string? statFailureInstruction = null;
            if (!rollResult.IsSuccess && statDeliveryInstructions != null)
            {
                statFailureInstruction = HorninessEngine.GetStatFailureInstruction(
                    statDeliveryInstructions, chosenOption.Stat, rollResult.Tier);
            }

            var textDiffs = new List<TextDiff>();

            // 10b. Steering roll
            string originalIntendedText = chosenOption.IntendedText ?? "";
            progress?.Report(new TurnProgressEvent(TurnProgressStage.SteeringStarted));
            SteeringRollResult steeringResult = await steeringEngine.AttemptSteeringRollAsync(
                originalIntendedText, player, opponent, llm, BuildHistoryForLlmContext(state), ct).ConfigureAwait(false);
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
            string deliveredMessage = await llm.DeliverMessageAsync(deliveryContext, ct).ConfigureAwait(false);
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

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (stateBefore != stateAfter)
            {
                narrativeBeat = $"*** Interest state changed to {stateAfter} ***";
            }

            // 10. Compute response delay
            double responseDelayMinutes = opponent.Timing.ComputeDelay(state.Interest.Current, resolveDice);

            string? horninessOverlayInstruction;
            HorninessCheckResult horninessCheckResult;
            (horninessCheckResult, horninessOverlayInstruction) = horninessEngine.PeekAsync(
                state.SessionHorniness,
                state.PlayerShadows,
                statDeliveryInstructions,
                ct);

            int horninessInterestPenalty = 0;
            int horninessInterestBefore = 0;

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
                    var rawShadowResult = shadowCheckEngine.Check(pairedShadow.Value, shadowValue);
                    shadowRoll = rawShadowResult.Roll;
                    shadowDC   = rawShadowResult.DC;
                    shadowMiss = rawShadowResult.IsMiss;
                    rawShadowCheck = rawShadowResult.Check;

                    if (shadowMiss)
                    {
                        shadowTier = rawShadowResult.Tier;
                        string? instruction = HorninessEngine.GetShadowCorruptionInstruction(
                            statDeliveryInstructions, pairedShadow.Value, shadowTier);

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
                llm,
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
                onTextLayerNoop,
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
                            int shadowFailDelta = ResolveFailureInterestDelta(forcedFailResult, rules);
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

            // #899: Horniness TEXT OVERLAY
            if (horninessOverlayInstruction != null)
            {
                string beforeHorniness = deliveredMessage;
                string opponentCtx = BuildOpponentContext(opponent);
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayStarted));
                string rawHorninessOutput = await llm.ApplyHorninessOverlayAsync(deliveredMessage, horninessOverlayInstruction, opponentCtx, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
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
                    EmitTextLayerNoop(onTextLayerNoop, state.TurnNumber, "Horniness", beforeHorniness, deliveredMessage);
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

            state.History.Add((player.DisplayName, deliveredMessage));

            // 11. Generate opponent response
            var opponentTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            Dictionary<ShadowStatType, int>? opponentShadowThresholds = null;
            if (state.OpponentShadows != null)
            {
                opponentShadowThresholds = new Dictionary<ShadowStatType, int>();
                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    opponentShadowThresholds[shadow] = state.OpponentShadows.GetEffectiveShadow(shadow);
                }
            }

            string opponentArchetypeDirective = opponent.ActiveArchetype?.Directive;

            var opponentContext = new OpponentContext(
                playerPrompt: player.AssembledSystemPrompt,
                opponentPrompt: opponent.AssembledSystemPrompt,
                conversationHistory: BuildHistoryForLlmContext(state),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(state.History, opponent.DisplayName),
                activeTraps: GameSessionHelpers.GetActiveTrapNames(state.Traps),
                currentInterest: state.Interest.Current,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                activeTrapInstructions: opponentTrapInstructions,
                playerName: player.DisplayName,
                opponentName: opponent.DisplayName,
                currentTurn: state.TurnNumber,
                shadowThresholds: opponentShadowThresholds,
                deliveryTier: rollResult.Tier,
                activeArchetypeDirective: opponentArchetypeDirective);

            progress?.Report(new TurnProgressEvent(TurnProgressStage.OpponentResponseStarted));

            OpponentResponse opponentResponse;
            if (llm is Pinder.Core.Interfaces.IStatefulLlmAdapter statefulLlm)
            {
                var statefulResult = await statefulLlm.GetOpponentResponseAsync(
                    opponentContext,
                    state.OpponentHistory,
                    ct).ConfigureAwait(false);
                if (statefulResult == null)
                    throw new InvalidOperationException("LLM adapter returned null stateful opponent result");
                opponentResponse = statefulResult.Response;
                if (opponentResponse == null)
                    throw new InvalidOperationException("LLM adapter returned null opponent response");
                if (statefulResult.NewHistoryEntries != null)
                {
                    foreach (var entry in statefulResult.NewHistoryEntries)
                    {
                        if (entry != null)
                            state.OpponentHistory.Add(entry);
                    }
                }
            }
            else
            {
                opponentResponse = await llm.GetOpponentResponseAsync(opponentContext, ct).ConfigureAwait(false);
                if (opponentResponse == null)
                    throw new InvalidOperationException("LLM adapter returned null opponent response");
            }
            string opponentMessage = opponentResponse.MessageText;
            progress?.Report(new TurnProgressEvent(TurnProgressStage.OpponentResponseCompleted, opponentMessage));

            state.ActiveWeakness = opponentResponse.WeaknessWindow;
            state.ActiveTell = opponentResponse.DetectedTell;

            state.History.Add((opponent.DisplayName, opponentMessage));

            state.Traps.AdvanceTurn();

            state.TurnNumber++;

            state.CurrentOptions = null;
            state.CurrentDicePools = null;

            if (rollResult.IsSuccess && baseInterestDelta < 0)
                throw new InvariantViolationException(
                    $"#942 invariant violated on turn {state.TurnNumber}: roll.IsSuccess=true " +
                    $"but baseInterestDelta={baseInterestDelta} (expected ≥0). " +
                    "SuccessScale cannot produce a negative delta for a success roll. " +
                    "This indicates a phantom turn produced from a pre-corrupted session state.");

            var stateSnapshot = CreateSnapshot(state, rules);

            return new TurnResult(
                roll: rollResult,
                deliveredMessage: deliveredMessage,
                opponentMessage: opponentMessage,
                narrativeBeat: narrativeBeat,
                interestDelta: interestDelta,
                stateAfter: stateSnapshot,
                isGameOver: isGameOver,
                outcome: outcome,
                shadowGrowthEvents: shadowGrowthEvents,
                comboTriggered: comboTriggered,
                callbackBonusApplied: callbackBonus,
                tellReadBonus: tellBonus,
                tellReadMessage: tellBonus > 0 ? "📖 You read the moment. +2 bonus." : null,
                xpEarned: turnXpEarned,
                baseInterestDelta: baseInterestDelta,
                riskBonusDelta: riskBonusDelta,
                riskTier: rollResult.RiskTier,
                comboBonusDelta: comboBonusDelta,
                detectedWindow: opponentResponse.WeaknessWindow,
                steering: steeringResult,
                horninessCheck: horninessCheckResult,
                tripleBonusApplied: tripleBonusApplied,
                horninessInterestPenalty: horninessInterestPenalty,
                horninessInterestBefore: horninessInterestBefore,
                textDiffs: textDiffs.Count > 0 ? textDiffs : null,
                shadowCheck: shadowCheckResult,
                trapClearedDisplayName: trapClearedDisplayName);
        }

        private static int GetMomentumBonus(int streak, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetMomentumBonus(streak);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            if (streak >= 5) return 3;
            if (streak >= 3) return 2;
            return 0;
        }

        private static int ResolveFailureInterestDelta(RollResult rollResult, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetFailureInterestDelta(rollResult.MissMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return FailureScale.GetInterestDelta(rollResult);
        }

        private static int ResolveSuccessInterestDelta(RollResult rollResult, IRuleResolver? rules)
        {
            if (rules != null)
            {
                int beatMargin = rollResult.FinalTotal - rollResult.DC;
                var resolved = rules.GetSuccessInterestDelta(beatMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return SuccessScale.GetInterestDelta(rollResult);
        }

        private static InterestState ResolveInterestState(GameSessionState state, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetInterestState(state.Interest.Current);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return state.Interest.GetState();
        }

        private static int ResolveThresholdLevel(int shadowValue, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetShadowThresholdLevel(shadowValue);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
        }

        private static GameStateSnapshot CreateSnapshot(GameSessionState state, IRuleResolver? rules)
        {
            return GameSessionHelpers.CreateSnapshot(
                state.Interest,
                ResolveInterestState(state, rules),
                state.MomentumStreak,
                state.Traps,
                state.TurnNumber,
                state.ComboTracker.HasTripleBonus,
                state.OpponentHistory);
        }

        private static System.Collections.Generic.IReadOnlyList<(string Sender, string Text)> BuildHistoryForLlmContext(GameSessionState state)
        {
            var history = state.History;
            bool anyScene = false;
            for (int i = 0; i < history.Count; i++)
            {
                if (Senders.IsScene(history[i].Sender)) { anyScene = true; break; }
            }
            if (!anyScene) return history.AsReadOnly();

            var view = new List<(string Sender, string Text)>(history.Count);
            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                if (Senders.IsScene(entry.Sender)) continue;
                view.Add(entry);
            }
            return view.AsReadOnly();
        }

        private static ShadowStatType? GetPairedShadow(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:         return ShadowStatType.Madness;
                case StatType.Rizz:          return ShadowStatType.Despair;
                case StatType.Honesty:       return ShadowStatType.Denial;
                case StatType.Chaos:         return ShadowStatType.Fixation;
                case StatType.Wit:           return ShadowStatType.Dread;
                case StatType.SelfAwareness: return ShadowStatType.Overthinking;
                default:                     return null;
            }
        }

        private static RollResult CreateForcedFailResult(RollResult original, FailureTier shadowTier)
        {
            int fakeDie = original.DC > 1 ? original.DC - 1 : 1;
            var check = Pinder.Core.Rolls.RollCheckResult.Synthesise(
                dieRoll:       fakeDie,
                secondDieRoll: null,
                usedDieRoll:   fakeDie,
                statModifier:  0,
                levelBonus:    0,
                dc:            original.DC);
            return new RollResult(
                dieRoll:        fakeDie,
                secondDieRoll:  null,
                usedDieRoll:    fakeDie,
                stat:           original.Stat,
                statModifier:   0,
                levelBonus:     0,
                dc:             original.DC,
                tier:           shadowTier,
                activatedTrap:  null,
                externalBonus:  0,
                check:          check,
                defendingStat:  Pinder.Core.Stats.StatBlock.DefenceTable[original.Stat]);
        }

        private static string BuildOpponentContext(CharacterProfile opponent)
        {
            if (opponent == null) return string.Empty;
            string bio = string.IsNullOrWhiteSpace(opponent.Bio) ? "(no bio)" : opponent.Bio;
            string items = opponent.EquippedItemDisplayNames != null && opponent.EquippedItemDisplayNames.Count > 0
                ? string.Join(", ", opponent.EquippedItemDisplayNames)
                : "(none)";
            return $"Opponent: {opponent.DisplayName} | Bio: \"{bio}\" | Wearing: {items}";
        }

        private static void EmitTextLayerNoop(Action<TextLayerNoopEvent>? onTextLayerNoop, int turnNumber, string layer, string beforeText, string afterText)
        {
            if (onTextLayerNoop == null) return;
            try
            {
                string beforeHash = ComputeStableHash(beforeText);
                string afterHash = ComputeStableHash(afterText);
                onTextLayerNoop(new TextLayerNoopEvent(turnNumber, layer, beforeHash, afterHash));
            }
            catch
            {
                // Diagnostic-only path — swallow
            }
        }

        private static string ComputeStableHash(string? text)
        {
            if (text == null) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < Math.Min(8, bytes.Length); i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
