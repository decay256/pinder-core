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
using Pinder.Core.I18n;

namespace Pinder.Core.Conversation
{
    internal partial class TurnOrchestrator
    {
        private readonly ILlmAdapter _llm;
        private readonly IDiceRoller _dice;
        private readonly IRuleResolver? _rules;
        private readonly Random? _statDrawRng;

        private readonly RollResolutionStage _rollResolutionStage;
        private readonly DeliveryStage _deliveryStage;
        private readonly OpponentResponseStage _opponentResponseStage;
        private readonly int _maxDialogueOptions;

        public TurnOrchestrator(
            ILlmAdapter llm,
            IDiceRoller dice,
            IRuleResolver? rules,
            Random? statDrawRng,
            RollResolutionStage rollResolutionStage,
            DeliveryStage deliveryStage,
            OpponentResponseStage opponentResponseStage,
            int maxDialogueOptions)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _dice = dice ?? throw new ArgumentNullException(nameof(dice));
            _rules = rules;
            _statDrawRng = statDrawRng;

            _rollResolutionStage = rollResolutionStage ?? throw new ArgumentNullException(nameof(rollResolutionStage));
            _deliveryStage = deliveryStage ?? throw new ArgumentNullException(nameof(deliveryStage));
            _opponentResponseStage = opponentResponseStage ?? throw new ArgumentNullException(nameof(opponentResponseStage));
            _maxDialogueOptions = maxDialogueOptions;
        }

        internal async Task<TurnStart> StartTurnAsync(
            GameSessionState state,
            CharacterProfile player,
            CharacterProfile opponent,
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
            if (TurnOrchestratorHelpers.ResolveInterestState(state, _rules) == InterestState.Bored)
            {
                int ghostRoll = _dice.Roll(4);
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
                    int tier = TurnOrchestratorHelpers.ResolveThresholdLevel(effectiveVal, _rules);
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

            // Draw N random stats for this turn's options
            var allStats = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var availableStats = OptionFilterEngine.DrawRandomStats(allStats, _maxDialogueOptions, shadowThresholds, _statDrawRng);

            var context = new DialogueContext(
                playerPrompt: player.AssembledSystemPrompt,
                opponentPrompt: GameSessionHelpers
                    .BuildOpponentVisibleProfile(opponent, state.OpponentOutfitDescription)
                    .Render(),
                // #333: scene entries are excluded from the LLM context view.
                conversationHistory: TurnOrchestratorHelpers.BuildHistoryForLlmContext(state),
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
                availableStats: availableStats,
                activeArchetypeDirective: playerArchetypeDirective,
                stakeLines: null,
                stakeLinesReferenced: null,
                maxDialogueOptions: _maxDialogueOptions);

            // Get dialogue options from LLM
            var rawOptions = await _llm.GetDialogueOptionsAsync(context, ct).ConfigureAwait(false);

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
                options = OptionFilterEngine.ApplyT3Filters(options, shadowThresholds, state.LastStatUsed, _dice);
            }

            state.CurrentOptions = options;

            // Compute pending momentum bonus for the upcoming roll (#268)
            state.PendingMomentumBonus = TurnOrchestratorHelpers.GetMomentumBonus(state.MomentumStreak, _rules);

            state.CurrentDicePools = new Pinder.Core.Rolls.PerOptionDicePool[options.Length];
            for (int i = 0; i < options.Length; i++)
                state.CurrentDicePools[i] = new Pinder.Core.Rolls.PerOptionDicePool(i);

            var snapshot = TurnOrchestratorHelpers.CreateSnapshot(state, _rules);

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
