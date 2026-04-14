using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Orchestrates a single Pinder conversation from match to outcome.
    /// Owns all mutable game state: interest, traps, momentum, history, turn count.
    /// Sequences calls to RollEngine (stateless), ILlmAdapter, and interest/trap tracking.
    /// Delegates shadow growth, XP recording, steering, horniness, and option filtering
    /// to dedicated single-responsibility modules.
    /// </summary>
    public sealed class GameSession
    {
        private readonly CharacterProfile _player;
        private readonly CharacterProfile _opponent;
        private readonly ILlmAdapter _llm;
        private readonly IDiceRoller _dice;
        private readonly ITrapRegistry _trapRegistry;

        private readonly InterestMeter _interest;
        private readonly TrapState _traps;
        private readonly List<(string Sender, string Text)> _history;

        // Sprint 8 Wave 0: optional config fields
        private readonly IGameClock? _clock;
        private readonly SessionShadowTracker? _playerShadows;
        private readonly SessionShadowTracker? _opponentShadows;

        // Combo tracking (#46)
        private readonly ComboTracker _comboTracker;

        // Callback tracking (#47)
        private readonly List<CallbackOpportunity> _topics;

        // Despair (RIZZ failure shadow) tracking (#708, #717)
        private int _rizzCumulativeFailureCount;

        private int _momentumStreak;
        private int _pendingMomentumBonus;
        private int _turnNumber;
        private bool _ended;
        private GameOutcome? _outcome;

        // XP tracking (#48)
        private readonly XpLedger _xpLedger;

        // Rule resolver for data-driven game constants (#463)
        private readonly IRuleResolver? _rules;
        private readonly int _globalDcBias;

        // Weakness window from opponent's last response (#49)
        private WeaknessWindow? _activeWeakness;

        // Tell from opponent's last response (#50)
        private Tell? _activeTell;

        // Horniness session roll (#45)
        private int _sessionHorniness;
        private int _horninessRoll;
        private int _horninessTimeModifier;

        // Nat 20 crit advantage (#271) — §4: previous crit grants advantage for 1 roll
        private bool _pendingCritAdvantage;

        // Shadow threshold tracking (#45)
        private StatType? _lastStatUsed;
        private HashSet<StatType>? _shadowDisadvantagedStats;
        private Dictionary<ShadowStatType, int>? _currentShadowThresholds;

        // Stat delivery instructions for horniness overlay tier lookups (#709)
        private readonly object? _statDeliveryInstructions;

        // Stored between StartTurnAsync and ResolveTurnAsync
        private DialogueOption[]? _currentOptions;
        private bool _currentHasAdvantage;
        private bool _currentHasDisadvantage;

        // Extracted single-responsibility modules
        private readonly ShadowGrowthEvaluator? _shadowGrowthEvaluator;
        private readonly SessionXpRecorder _xpRecorder;
        private readonly SteeringEngine _steeringEngine;
        private readonly HorninessEngine _horninessEngine;

        /// <summary>
        /// Creates a new GameSession with required configuration.
        /// Config must be non-null — no silent fallbacks.
        /// </summary>
        public GameSession(
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            GameSessionConfig config)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _opponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _dice = dice ?? throw new ArgumentNullException(nameof(dice));
            _trapRegistry = trapRegistry ?? throw new ArgumentNullException(nameof(trapRegistry));

            if (config == null)
                throw new ArgumentNullException(nameof(config), "GameSessionConfig is required — no silent defaults");

            // Store config fields early (needed by ResolveThresholdLevel below)
            _clock = config.Clock;
            _playerShadows = config.PlayerShadows;
            _opponentShadows = config.OpponentShadows;
            _rules = config.Rules;
            _globalDcBias = config.GlobalDcBias;
            var steeringRng = config.SteeringRng ?? new Random();
            _statDeliveryInstructions = config.StatDeliveryInstructions;

            // Determine starting interest: explicit config > Dread T3 > default
            if (config.StartingInterest.HasValue)
            {
                _interest = new InterestMeter(config.StartingInterest.Value);
            }
            else if (config.PlayerShadows != null
                && ResolveThresholdLevel(
                    config.PlayerShadows.GetEffectiveShadow(ShadowStatType.Dread)) >= 3)
            {
                _interest = new InterestMeter(8);
            }
            else
            {
                _interest = new InterestMeter();
            }
            _traps = new TrapState();
            _history = new List<(string Sender, string Text)>();
            _momentumStreak = 0;
            _turnNumber = 0;
            _ended = false;
            _outcome = null;

            // GameClock is required — no fallback
            if (_clock == null)
                throw new InvalidOperationException("GameClock is required — pass clock via GameSessionConfig.Clock");

            // Roll session Horniness (1d10) every session + time-of-day modifier from clock
            {
                _horninessRoll = _dice.Roll(10);
                _horninessTimeModifier = _clock.GetHorninessModifier();
                _sessionHorniness = Math.Max(0, _horninessRoll + _horninessTimeModifier);
            }

            // XP tracking (#48)
            _xpLedger = new XpLedger();

            // Combo tracking (#46)
            _comboTracker = new ComboTracker();

            // Callback tracking (#47)
            _topics = new List<CallbackOpportunity>();

            // Shadow growth tracking (#44)
            _rizzCumulativeFailureCount = 0;

            // Initialize extracted modules
            _shadowGrowthEvaluator = _playerShadows != null
                ? new ShadowGrowthEvaluator(_playerShadows)
                : null;
            _xpRecorder = new SessionXpRecorder(_xpLedger, _rules);
            _steeringEngine = new SteeringEngine(steeringRng);
            _horninessEngine = new HorninessEngine(steeringRng);

            // Stateful conversation session (#536)
            // If the adapter supports stateful mode, start a persistent opponent session.
            if (_llm is Pinder.Core.Interfaces.IStatefulLlmAdapter stateful)
            {
                stateful.StartOpponentSession(_opponent.AssembledSystemPrompt);
            }
        }

        /// <summary>
        /// Register a conversation topic for future callback opportunities.
        /// Called by the host or LLM adapter after each turn to seed topics.
        /// </summary>
        /// <param name="topic">The topic to register. Must not be null.</param>
        /// <exception cref="ArgumentNullException">If topic is null.</exception>
        public void AddTopic(CallbackOpportunity topic)
        {
            if (topic == null)
                throw new ArgumentNullException(nameof(topic));
            _topics.Add(topic);
        }

        /// <summary>Total XP earned during this session.</summary>
        public int TotalXpEarned => _xpLedger.TotalXp;

        /// <summary>Session horniness value (d10 + clock modifier). Used for display.</summary>
        public int SessionHorniness => _sessionHorniness;

        /// <summary>Raw d10 roll used for session horniness.</summary>
        public int HorninessRoll => _horninessRoll;

        /// <summary>Time-of-day modifier applied to the horniness roll.</summary>
        public int HorninessTimeModifier => _horninessTimeModifier;

        /// <summary>The full XP ledger for this session.</summary>
        public XpLedger XpLedger => _xpLedger;

        /// <summary>
        /// Restores all mutable session state from a <see cref="ResimulateData"/> snapshot.
        /// Call this immediately after constructing a GameSession with the correct initial snapshot;
        /// the session must not have had any turns played.
        /// </summary>
        /// <param name="data">State data to restore.</param>
        /// <param name="trapRegistry">Used to look up trap definitions by stat.</param>
        public void RestoreState(ResimulateData data, ITrapRegistry trapRegistry)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (trapRegistry == null) throw new ArgumentNullException(nameof(trapRegistry));

            // Interest: apply delta to reach the target value
            int interestDelta = data.TargetInterest - _interest.Current;
            if (interestDelta != 0)
                _interest.Apply(interestDelta);

            // Shadow tracker: set deltas so effective values match the snapshot
            if (_playerShadows != null && data.ShadowValues != null)
                _playerShadows.RestoreFromSnapshot(data.ShadowValues);

            // Momentum
            _momentumStreak = data.MomentumStreak;

            // Traps: clear and re-activate with original remaining durations
            _traps.ClearAll();
            if (data.ActiveTraps != null)
            {
                foreach (var (statName, turnsRemaining) in data.ActiveTraps)
                {
                    if (Enum.TryParse<StatType>(statName, out var stat))
                    {
                        var definition = trapRegistry.GetTrap(stat);
                        if (definition != null)
                            _traps.Activate(definition, turnsRemaining);
                    }
                }
            }

            // Conversation history
            _history.Clear();
            if (data.ConversationHistory != null)
                _history.AddRange(data.ConversationHistory);

            // Turn number
            _turnNumber = data.TurnNumber;

            // Combo tracker
            _comboTracker.RestoreFromSnapshot(
                data.ComboHistory ?? new List<(string StatName, bool Succeeded)>(),
                data.PendingTripleBonus);

            // Rizz cumulative failure count (drives Despair shadow growth)
            _rizzCumulativeFailureCount = data.RizzCumulativeFailureCount;
        }

        /// <summary>
        /// Start a new turn. Checks end conditions, determines advantage/disadvantage,
        /// and fetches dialogue options from the LLM adapter.
        /// </summary>
        /// <exception cref="GameEndedException">If the game has already ended.</exception>
        public async Task<TurnStart> StartTurnAsync()
        {
            // Check if game already ended
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            // Check end conditions: interest at 0 or 25
            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date
                if (_playerShadows != null)
                {
                    _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
                    var dreadEvents = _playerShadows.DrainGrowthEvents();
                    throw new GameEndedException(GameOutcome.Unmatched, dreadEvents);
                }
                throw new GameEndedException(GameOutcome.Unmatched);
            }

            if (_interest.IsMaxed)
            {
                _ended = true;
                _outcome = GameOutcome.DateSecured;
                throw new GameEndedException(GameOutcome.DateSecured);
            }

            // Ghost trigger: if Bored state, 25% chance per turn
            if (ResolveInterestState() == InterestState.Bored)
            {
                int ghostRoll = _dice.Roll(4);
                if (ghostRoll == 1)
                {
                    _ended = true;
                    _outcome = GameOutcome.Ghosted;

                    // Shadow growth: Ghosted → +1 Dread (#44 trigger 8)
                    if (_playerShadows != null)
                    {
                        _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "Ghosted");
                        var events = _playerShadows.DrainGrowthEvents();
                        throw new GameEndedException(GameOutcome.Ghosted, events);
                    }

                    throw new GameEndedException(GameOutcome.Ghosted);
                }
            }

            // Determine advantage/disadvantage from interest state + traps
            bool hasAdvantage = _interest.GrantsAdvantage;
            bool hasDisadvantage = _interest.GrantsDisadvantage;

            // Nat 20 crit advantage (#271) — previous crit grants advantage for 1 roll
            if (_pendingCritAdvantage)
            {
                hasAdvantage = true;
                _pendingCritAdvantage = false;
            }

            // Store for ResolveTurnAsync
            _currentHasAdvantage = hasAdvantage;
            _currentHasDisadvantage = hasDisadvantage;

            // Shadow threshold evaluation (#45)
            Dictionary<ShadowStatType, int>? shadowThresholds = null;
            _shadowDisadvantagedStats = null;

            if (_playerShadows != null)
            {
                shadowThresholds = new Dictionary<ShadowStatType, int>();
                _shadowDisadvantagedStats = new HashSet<StatType>();

                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    int effectiveVal = _playerShadows.GetEffectiveShadow(shadow);
                    shadowThresholds[shadow] = effectiveVal;
                    int tier = ResolveThresholdLevel(effectiveVal);
                    // T2+ disadvantage for paired stats is removed: shadow check IS the disadvantage (#755)
                    _ = tier; // suppress unused warning
                }
            }

            // Store player shadow thresholds for use in ResolveTurnAsync (#308)
            _currentShadowThresholds = shadowThresholds;

            // Get trap names and LLM instructions for context
            var activeTrapNames = GameSessionHelpers.GetActiveTrapNames(_traps);
            var activeTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(_traps);

            // Build dialogue context — pass callback topics (#47) and shadow thresholds (#45)
            string playerArchetypeDirective = _player.ActiveArchetype?.Directive;

            // Draw 3 random stats for this turn's options
            var allStats = new[] { StatType.Charm, StatType.Rizz, StatType.Honesty, StatType.Chaos, StatType.Wit, StatType.SelfAwareness };
            var availableStats = OptionFilterEngine.DrawRandomStats(allStats, 3, shadowThresholds);

            var context = new DialogueContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: GameSessionHelpers.BuildOpponentVisibleProfile(_opponent),
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(_history, _opponent.DisplayName),
                activeTraps: activeTrapNames,
                currentInterest: _interest.Current,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                callbackOpportunities: _topics.Count > 0 ? new List<CallbackOpportunity>(_topics) : null,
                horninessLevel: _sessionHorniness,
                requiresRizzOption: false,
                currentTurn: _turnNumber,
                playerTextingStyle: _player.TextingStyleFragment,
                activeTell: _activeTell,
                activeArchetypeDirective: playerArchetypeDirective,
                availableStats: availableStats);

            // Get dialogue options from LLM
            var rawOptions = await _llm.GetDialogueOptionsAsync(context).ConfigureAwait(false);

            // Peek combos for each option (#46), enrich with weakness window (#49) and tell bonus (#50)
            var options = new DialogueOption[rawOptions.Length];
            for (int i = 0; i < rawOptions.Length; i++)
            {
                var opt = rawOptions[i];
                string? comboName = _comboTracker.PeekCombo(opt.Stat);
                bool hasWeaknessWindow = _activeWeakness != null
                    && StatBlock.DefenceTable[opt.Stat] == _activeWeakness.DefendingStat;
                bool hasTellBonus = _activeTell != null && opt.Stat == _activeTell.Stat;
                options[i] = new DialogueOption(
                    opt.Stat,
                    opt.IntendedText,
                    opt.CallbackTurnNumber,
                    comboName,
                    hasTellBonus,
                    hasWeaknessWindow);
            }

            // T3 option filtering (#45)
            if (_playerShadows != null && shadowThresholds != null)
            {
                options = OptionFilterEngine.ApplyT3Filters(options, shadowThresholds, _lastStatUsed, _dice);
            }

            _currentOptions = options;

            // Compute pending momentum bonus for the upcoming roll (#268)
            _pendingMomentumBonus = GetMomentumBonus(_momentumStreak);

            var snapshot = CreateSnapshot();
            return new TurnStart(options, snapshot);
        }

        /// <summary>
        /// Resolve a turn after the player selects an option.
        /// Sequences: roll → interest delta → momentum → shadow growth → trap advance → deliver → opponent response.
        /// </summary>
        /// <param name="optionIndex">Index into the options array from StartTurnAsync.</param>
        /// <exception cref="GameEndedException">If the game has already ended.</exception>
        /// <exception cref="InvalidOperationException">If StartTurnAsync was not called first or index is invalid.</exception>
        public async Task<TurnResult> ResolveTurnAsync(int optionIndex)
        {
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            if (_currentOptions == null)
                throw new InvalidOperationException("Must call StartTurnAsync before ResolveTurnAsync.");

            if (optionIndex < 0 || optionIndex >= _currentOptions.Length)
                throw new ArgumentOutOfRangeException(nameof(optionIndex),
                    $"Option index {optionIndex} is out of range. Valid range: 0–{_currentOptions.Length - 1}.");

            // Consume 1 energy from clock if available
            if (_clock != null && !_clock.ConsumeEnergy(1))
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date (energy depleted)
                if (_playerShadows != null)
                {
                    _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
                    var dreadEvents = _playerShadows.DrainGrowthEvents();
                    throw new GameEndedException(GameOutcome.Unmatched, dreadEvents);
                }
                throw new GameEndedException(GameOutcome.Unmatched);
            }

            var chosenOption = _currentOptions[optionIndex];

            // Denial +1 when Honesty was available but player chose a different stat (#272 — §7)
            if (_playerShadows != null
                && chosenOption.Stat != StatType.Honesty
                && _currentOptions.Any(o => o.Stat == StatType.Honesty))
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Denial, 1,
                    "Skipped Honesty option");
            }

            // Compute callback bonus (#47)
            int callbackBonus = 0;
            if (chosenOption.CallbackTurnNumber.HasValue)
            {
                callbackBonus = CallbackBonus.Compute(_turnNumber, chosenOption.CallbackTurnNumber.Value);
            }

            // Compute tell bonus (#50)
            bool hasTellOption = _activeTell != null && chosenOption.Stat == _activeTell.Stat;
            int tellBonus = hasTellOption ? 2 : 0;

            // Compute external bonus: tell + callback + Triple combo + momentum (#46, #47, #50, #268)
            int externalBonus = tellBonus + callbackBonus + _pendingMomentumBonus;
            int tripleBonusApplied = 0;
            if (_comboTracker.HasTripleBonus)
            {
                tripleBonusApplied = 1;
                externalBonus += tripleBonusApplied;
                _comboTracker.ConsumeTripleBonus(); // Consume after applying (#46 edge case 7)
            }

            // Compute DC adjustment from weakness window (#49) + global difficulty bias
            int dcAdjustment = 0;
            if (_activeWeakness != null
                && StatBlock.DefenceTable[chosenOption.Stat] == _activeWeakness.DefendingStat)
            {
                dcAdjustment = _activeWeakness.DcReduction;
            }
            if (_globalDcBias != 0)
                dcAdjustment -= _globalDcBias;

            // Clear weakness window — consumed this turn regardless of match (#49)
            _activeWeakness = null;

            // Clear active tell — consumed this turn regardless of match (#50)
            _activeTell = null;

            // Shadow threshold per-stat disadvantage (#45)
            bool resolveHasDisadvantage = _currentHasDisadvantage;
            if (_shadowDisadvantagedStats != null && _shadowDisadvantagedStats.Contains(chosenOption.Stat))
            {
                resolveHasDisadvantage = true;
            }

            // 1. Roll dice
            var rollResult = RollEngine.Resolve(
                stat: chosenOption.Stat,
                attacker: _player.Stats,
                defender: _opponent.Stats,
                attackerTraps: _traps,
                level: _player.Level,
                trapRegistry: _trapRegistry,
                dice: _dice,
                hasAdvantage: _currentHasAdvantage,
                hasDisadvantage: resolveHasDisadvantage,
                externalBonus: externalBonus,
                dcAdjustment: dcAdjustment);

            // 2. Compute interest delta from roll outcome
            int baseInterestDelta;
            int riskBonusDelta = 0;
            if (rollResult.IsSuccess)
            {
                baseInterestDelta = ResolveSuccessInterestDelta(rollResult);
                riskBonusDelta = RiskTierBonus.GetInterestBonus(rollResult);
            }
            else
            {
                baseInterestDelta = ResolveFailureInterestDelta(rollResult);
            }
            int interestDelta = baseInterestDelta + riskBonusDelta;

            // 3. Update momentum streak
            _pendingMomentumBonus = 0;
            if (rollResult.IsSuccess)
            {
                _momentumStreak++;
            }
            else
            {
                _momentumStreak = 0;
            }

            // 3b. Nat 20 crit advantage (#271) — set for next roll
            if (rollResult.IsNatTwenty)
            {
                _pendingCritAdvantage = true;

                // Nat 20 on CHAOS → Madness −1
                if (chosenOption.Stat == StatType.Chaos)
                {
                    _playerShadows?.ApplyOffset(ShadowStatType.Madness, -1,
                        "Nat 20 on Chaos — chaos mastered, not consumed");
                }

                // Nat 20 (any stat) → Dread −1 (#720)
                _playerShadows?.ApplyOffset(ShadowStatType.Dread, -1,
                    "Nat 20 — existential confidence surge");
            }

            // 3c. Track last stat used for Fixation T3 (#45)
            _lastStatUsed = chosenOption.Stat;

            // 3d. Combo detection (#46)
            _comboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess);
            var combo = _comboTracker.CheckCombo();
            string? comboTriggered = null;
            int comboBonusDelta = 0;
            if (combo != null)
            {
                comboBonusDelta = combo.InterestBonus;
                interestDelta += comboBonusDelta;
                comboTriggered = combo.Name;
            }

            // 3d. Record roll XP (#48)
            _xpRecorder.RecordRollXp(rollResult);

            // 4. Record interest before applying delta
            int interestBefore = _interest.Current;
            InterestState stateBefore = ResolveInterestState();

            // 5. Apply interest delta
            _interest.Apply(interestDelta);

            int interestAfter = _interest.Current;
            InterestState stateAfter = ResolveInterestState();

            // ---- Shadow growth evaluation (#44) ----
            _shadowGrowthEvaluator?.EvaluatePerTurn(
                chosenOption, optionIndex, rollResult, interestAfter, comboTriggered, hasTellOption,
                _currentOptions,
                (chosen, opts) => GameSessionHelpers.IsHighestProbabilityOption(chosen, opts, _player, _opponent));

            // Shadow reduction: Winning despite Overthinking disadvantage → Overthinking −1
            if (rollResult.IsSuccess
                && _playerShadows != null
                && _shadowDisadvantagedStats != null
                && _shadowDisadvantagedStats.Contains(chosenOption.Stat)
                && StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Overthinking, -1,
                    "Succeeded despite Overthinking disadvantage");
            }

            // Shadow reduction: Success at interest ≥20 → Overthinking -1
            if (rollResult.IsSuccess && interestAfter >= 20)
            {
                _playerShadows?.ApplyOffset(ShadowStatType.Overthinking, -1,
                    "Success at high interest \u2014 pressure lifts");
            }

            // Check end conditions for end-of-game triggers
            bool isGameOver = false;
            GameOutcome? outcome = null;

            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                isGameOver = true;
                outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date
                _playerShadows?.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
            }
            else if (_interest.IsMaxed)
            {
                _ended = true;
                _outcome = GameOutcome.DateSecured;
                isGameOver = true;
                outcome = GameOutcome.DateSecured;
            }

            // End-of-game shadow growth checks
            if (isGameOver)
            {
                _shadowGrowthEvaluator?.EvaluateEndOfGame(outcome!.Value);
                _xpRecorder.RecordEndOfGameXp(outcome!.Value);
            }

            // Drain XP events for this turn (#48)
            var turnXpEvents = _xpLedger.DrainTurnEvents();
            int turnXpEarned = 0;
            for (int i = 0; i < turnXpEvents.Count; i++)
                turnXpEarned += turnXpEvents[i].Amount;

            // Drain shadow growth events for this turn
            var shadowGrowthEvents = _playerShadows != null
                ? _playerShadows.DrainGrowthEvents()
                : (IReadOnlyList<string>)Array.Empty<string>();

            // 6. Deliver message via LLM
            var deliveryTrapNames = GameSessionHelpers.GetActiveTrapNames(_traps);
            var deliveryTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(_traps);

            int beatDcBy = rollResult.IsSuccess ? rollResult.FinalTotal - rollResult.DC : 0;

            // Resolve stat-specific failure instruction when the roll failed (#695)
            string? statFailureInstruction = null;
            if (!rollResult.IsSuccess && _statDeliveryInstructions != null)
            {
                statFailureInstruction = HorninessEngine.GetStatFailureInstruction(
                    _statDeliveryInstructions, chosenOption.Stat, rollResult.Tier);
            }

            var deliveryContext = new DeliveryContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(_history, _opponent.DisplayName),
                chosenOption: chosenOption,
                outcome: rollResult.Tier,
                beatDcBy: beatDcBy,
                activeTraps: deliveryTrapNames,
                activeTrapInstructions: deliveryTrapInstructions,
                playerName: _player.DisplayName,
                opponentName: _opponent.DisplayName,
                currentTurn: _turnNumber,
                shadowThresholds: _currentShadowThresholds,
                isNat20: rollResult.IsNatTwenty,
                statFailureInstruction: statFailureInstruction);

            string deliveredMessage = await _llm.DeliverMessageAsync(deliveryContext).ConfigureAwait(false);

            // Collect word-level diffs for each text transform layer
            var textDiffs = new List<TextDiff>();

            // Tier modifier diff: intended text vs delivered-after-LLM rewrite
            string intendedText = chosenOption.IntendedText ?? "";
            if (deliveredMessage != intendedText && !string.IsNullOrEmpty(intendedText) && intendedText != "...")
            {
                string layerLabel = rollResult.IsNatTwenty ? "Nat 20" :
                                    rollResult.IsNatOne    ? "Nat 1"  :
                                    rollResult.Tier == Rolls.FailureTier.None ? "Strong success" :
                                    rollResult.Tier.ToString();
                var tierSpans = WordDiff.Compute(intendedText, deliveredMessage);
                textDiffs.Add(new TextDiff(layerLabel, tierSpans, intendedText, deliveredMessage));
            }

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (stateBefore != stateAfter)
            {
                narrativeBeat = $"*** Interest state changed to {stateAfter} ***";
            }

            // 10. Compute response delay
            double responseDelayMinutes = _opponent.Timing.ComputeDelay(_interest.Current, _dice);

            // 10b. Steering roll — attempt to append a date-steering question
            SteeringRollResult steeringResult = await _steeringEngine.AttemptSteeringRollAsync(
                deliveredMessage, _player, _opponent, _llm, _history.AsReadOnly()).ConfigureAwait(false);
            if (steeringResult.SteeringSucceeded && steeringResult.SteeringQuestion != null)
            {
                string beforeSteering = deliveredMessage;
                deliveredMessage = deliveredMessage.TrimEnd() + " " + steeringResult.SteeringQuestion;
                if (deliveredMessage != beforeSteering)
                {
                    var steeringSpans = WordDiff.Compute(beforeSteering, deliveredMessage);
                    textDiffs.Add(new TextDiff("Steering", steeringSpans, beforeSteering, deliveredMessage));
                }
            }

            // Per-turn Horniness overlay check (#709)
            string deliveredForHorniness = deliveredMessage;
            HorninessCheckResult horninessCheckResult = await _horninessEngine.CheckAsync(
                _sessionHorniness,
                _playerShadows,
                deliveredMessage,
                _llm,
                _statDeliveryInstructions,
                async (instruction) =>
                {
                    string beforeHorniness = deliveredMessage;
                    string opponentCtx = BuildOpponentContext(_opponent);
                    deliveredMessage = await _llm.ApplyHorninessOverlayAsync(deliveredMessage, instruction, opponentCtx).ConfigureAwait(false);
                    if (deliveredMessage != beforeHorniness)
                    {
                        var horninessSpans = WordDiff.Compute(beforeHorniness, deliveredMessage);
                        textDiffs.Add(new TextDiff("Horniness", horninessSpans, beforeHorniness, deliveredMessage));
                    }
                }).ConfigureAwait(false);

            // #743: Horniness penalty — when overlay fires and turn delta is positive, halve the delta
            // e.g. +5 interest gained this turn → horniness fires → floor(5/2) = 2 net gain
            int horninessInterestPenalty = 0;
            int horninessInterestBefore = 0;
            if (horninessCheckResult.OverlayApplied && interestDelta > 0)
            {
                horninessInterestBefore = _interest.Current;
                int halvedDelta = (int)Math.Floor(interestDelta / 2.0);
                int penalty = halvedDelta - interestDelta; // negative: e.g. 2 - 5 = -3
                _interest.Apply(penalty);
                horninessInterestPenalty = penalty;
                interestDelta += penalty; // net delta = halvedDelta
            }

            // #755: Shadow check — fires when using a stat with active paired shadow
            ShadowStatType? pairedShadow = GetPairedShadow(chosenOption.Stat);
            ShadowCheckResult shadowCheckResult = ShadowCheckResult.NotPerformed;

            if (pairedShadow.HasValue && _playerShadows != null)
            {
                int shadowValue = _playerShadows.GetEffectiveShadow(pairedShadow.Value);
                if (shadowValue > 0)
                {
                    int shadowRoll = _steeringEngine.RollD20(); // use steering rng so it doesn't consume game dice
                    int shadowDC = 20 - shadowValue;
                    bool shadowMiss = shadowRoll < shadowDC;

                    if (shadowMiss)
                    {
                        int missMargin = shadowDC - shadowRoll;
                        FailureTier shadowTier = HorninessEngine.DetermineHorninessTier(missMargin);
                        string? corruptionInstruction = HorninessEngine.GetShadowCorruptionInstruction(
                            _statDeliveryInstructions, pairedShadow.Value, shadowTier);

                        bool overlayApplied = false;
                        if (corruptionInstruction != null && rollResult.IsSuccess)
                        {
                            // Rewrite the delivered message with shadow corruption
                            string beforeShadow = deliveredMessage;
                            deliveredMessage = await _llm.ApplyShadowCorruptionAsync(
                                deliveredMessage, corruptionInstruction, pairedShadow.Value).ConfigureAwait(false);
                            if (deliveredMessage != beforeShadow)
                            {
                                var shadowSpans = WordDiff.Compute(beforeShadow, deliveredMessage);
                                textDiffs.Add(new TextDiff($"Shadow ({pairedShadow.Value})", shadowSpans, beforeShadow, deliveredMessage));
                            }

                            // Override: force success to be treated as a failure
                            // Undo the success interest delta, apply failure delta instead
                            var forcedFailResult = CreateForcedFailResult(rollResult, shadowTier);
                            int shadowFailDelta = ResolveFailureInterestDelta(forcedFailResult);
                            int correction = shadowFailDelta - interestDelta; // usually negative
                            _interest.Apply(correction);
                            interestDelta = shadowFailDelta;
                            overlayApplied = true;
                        }

                        shadowCheckResult = new ShadowCheckResult(
                            true, pairedShadow.Value, shadowRoll, shadowDC, true, shadowTier, overlayApplied);
                    }
                    else
                    {
                        shadowCheckResult = new ShadowCheckResult(
                            true, pairedShadow.Value, shadowRoll, shadowDC, false, FailureTier.None, false);
                    }
                }
            }

            _history.Add((_player.DisplayName, deliveredMessage));

            // 11. Generate opponent response
            var opponentTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(_traps);

            // Compute opponent shadow thresholds for opponent prompt taint (#308)
            Dictionary<ShadowStatType, int>? opponentShadowThresholds = null;
            if (_opponentShadows != null)
            {
                opponentShadowThresholds = new Dictionary<ShadowStatType, int>();
                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    opponentShadowThresholds[shadow] = _opponentShadows.GetEffectiveShadow(shadow);
                }
            }

            // Resolve active archetype directive for opponent
            string opponentArchetypeDirective = _opponent.ActiveArchetype?.Directive;

            var opponentContext = new OpponentContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(_history, _opponent.DisplayName),
                activeTraps: GameSessionHelpers.GetActiveTrapNames(_traps),
                currentInterest: _interest.Current,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                activeTrapInstructions: opponentTrapInstructions,
                playerName: _player.DisplayName,
                opponentName: _opponent.DisplayName,
                currentTurn: _turnNumber,
                shadowThresholds: opponentShadowThresholds,
                deliveryTier: rollResult.Tier,
                activeArchetypeDirective: opponentArchetypeDirective);

            var opponentResponse = await _llm.GetOpponentResponseAsync(opponentContext).ConfigureAwait(false);
            if (opponentResponse == null)
                throw new InvalidOperationException("LLM adapter returned null opponent response");
            string opponentMessage = opponentResponse.MessageText;

            // Store weakness window from opponent response for next turn (#49)
            _activeWeakness = opponentResponse.WeaknessWindow;

            // Store tell from opponent response for next turn (#50)
            _activeTell = opponentResponse.DetectedTell;

            // 12. Append opponent message to history
            _history.Add((_opponent.DisplayName, opponentMessage));

            // SA trap clear: SA success vs DC 12 clears oldest active trap (rules §clear)
            if (chosenOption.Stat == StatType.SelfAwareness
                && rollResult.IsSuccess
                && rollResult.FinalTotal >= 12
                && _traps.HasActive)
            {
                _traps.ClearOldest();
            }

            // 12b. Advance trap timers
            _traps.AdvanceTurn();

            // 13. Increment turn number
            _turnNumber++;

            // 14. Clear stored options
            _currentOptions = null;

            // 15. Build result
            var stateSnapshot = CreateSnapshot();

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
                shadowCheck: shadowCheckResult);
        }

        /// <summary>
        /// Get momentum bonus for the current streak length.
        /// Uses rule resolver if available, falls back to hardcoded values.
        /// 3-streak → +2, 4-streak → +2, 5+ → +3.
        /// </summary>
        private int GetMomentumBonus(int streak)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetMomentumBonus(streak);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            if (streak >= 5) return 3;
            if (streak >= 3) return 2;
            return 0;
        }

        /// <summary>
        /// Get failure interest delta, using rule resolver if available.
        /// </summary>
        private int ResolveFailureInterestDelta(RollResult rollResult)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetFailureInterestDelta(rollResult.MissMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return FailureScale.GetInterestDelta(rollResult);
        }

        /// <summary>
        /// Get success interest delta, using rule resolver if available.
        /// </summary>
        private int ResolveSuccessInterestDelta(RollResult rollResult)
        {
            if (_rules != null)
            {
                int beatMargin = rollResult.FinalTotal - rollResult.DC;
                var resolved = _rules.GetSuccessInterestDelta(beatMargin, rollResult.UsedDieRoll);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return SuccessScale.GetInterestDelta(rollResult);
        }

        /// <summary>
        /// Get interest state, using rule resolver if available.
        /// </summary>
        private InterestState ResolveInterestState()
        {
            if (_rules != null)
            {
                var resolved = _rules.GetInterestState(_interest.Current);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return _interest.GetState();
        }

        /// <summary>
        /// Get shadow threshold level, using rule resolver if available.
        /// </summary>
        private int ResolveThresholdLevel(int shadowValue)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetShadowThresholdLevel(shadowValue);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
        }

        private GameStateSnapshot CreateSnapshot()
        {
            return GameSessionHelpers.CreateSnapshot(
                _interest,
                ResolveInterestState(),
                _momentumStreak,
                _traps,
                _turnNumber,
                _comboTracker.HasTripleBonus);
        }

        /// <summary>
        /// Wait action: −1 interest, advance trap timers. No roll.
        /// Synchronous — no LLM calls.
        /// Self-contained turn action — does NOT require StartTurnAsync() first.
        /// </summary>
        /// <exception cref="GameEndedException">If the game has already ended or ghost trigger fires.</exception>
        public void Wait()
        {
            // 1. Check if game already ended
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            // 2. Check interest end conditions
            CheckInterestEndConditions();

            // 3. Ghost trigger check
            CheckGhostTrigger();

            // 4. Clear pending Speak options and weakness window (#49)
            _currentOptions = null;
            _activeWeakness = null;
            _activeTell = null;

            // 4b. Consume triple bonus if active (#46 edge case 7)
            _comboTracker.ConsumeTripleBonus();

            // 5. Apply -1 interest
            _interest.Apply(-1);

            // 6. Advance trap timers
            _traps.AdvanceTurn();

            // 7. Increment turn
            _turnNumber++;

            // 8. Check end conditions after interest change
            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date (Wait)
                _playerShadows?.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
            }
        }

        /// <summary>
        /// Checks interest-based end conditions and throws GameEndedException if triggered.
        /// </summary>
        private void CheckInterestEndConditions()
        {
            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                // End-of-game Dread +1: conversation ended without date
                if (_playerShadows != null)
                {
                    _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");
                    var dreadEvents = _playerShadows.DrainGrowthEvents();
                    throw new GameEndedException(GameOutcome.Unmatched, dreadEvents);
                }
                throw new GameEndedException(GameOutcome.Unmatched);
            }

            if (_interest.IsMaxed)
            {
                _ended = true;
                _outcome = GameOutcome.DateSecured;
                throw new GameEndedException(GameOutcome.DateSecured);
            }
        }

        /// <summary>
        /// Checks ghost trigger: if Bored state, 25% chance (dice.Roll(4)==1) to ghost.
        /// </summary>
        private void CheckGhostTrigger()
        {
            if (ResolveInterestState() == InterestState.Bored)
            {
                int ghostRoll = _dice.Roll(4);
                if (ghostRoll == 1)
                {
                    _ended = true;
                    _outcome = GameOutcome.Ghosted;

                    if (_playerShadows != null)
                    {
                        _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "Ghosted");
                        var events = _playerShadows.DrainGrowthEvents();
                        throw new GameEndedException(GameOutcome.Ghosted, events);
                    }

                    throw new GameEndedException(GameOutcome.Ghosted);
                }
            }
        }

        /// <summary>
        /// Returns the shadow stat paired with the given positive stat, or null if unrecognised.
        /// </summary>
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

        /// <summary>
        /// Creates a synthetic RollResult that represents a forced failure at the given tier.
        /// Used by the shadow check to compute the failure interest delta when overriding a success.
        /// </summary>
        private static RollResult CreateForcedFailResult(RollResult original, FailureTier shadowTier)
        {
            // Build a result that looks like a miss at the given tier.
            // We derive a miss margin that maps to the tier, then compute a die roll that misses DC.
            int fakeDie = original.DC > 1 ? original.DC - 1 : 1; // just below DC
            return new RollResult(
                dieRoll: fakeDie,
                secondDieRoll: null,
                usedDieRoll: fakeDie,
                stat: original.Stat,
                statModifier: 0,
                levelBonus: 0,
                dc: original.DC,
                tier: shadowTier,
                activatedTrap: null,
                externalBonus: 0);
        }

        /// <summary>
        /// Builds a compact opponent context string for the horniness overlay system prompt.
        /// Format: Opponent: [DisplayName] | Bio: "[bio]" | Wearing: [items]
        /// </summary>
        private static string BuildOpponentContext(CharacterProfile opponent)
        {
            if (opponent == null) return string.Empty;
            string bio = string.IsNullOrWhiteSpace(opponent.Bio) ? "(no bio)" : opponent.Bio;
            string items = opponent.EquippedItemDisplayNames != null && opponent.EquippedItemDisplayNames.Count > 0
                ? string.Join(", ", opponent.EquippedItemDisplayNames)
                : "(none)";
            return $"Opponent: {opponent.DisplayName} | Bio: \"{bio}\" | Wearing: {items}";
        }
    }
}
