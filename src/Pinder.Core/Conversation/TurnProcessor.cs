using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal static partial class TurnProcessor
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
    }
}
