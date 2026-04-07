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

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Orchestrates a single Pinder conversation from match to outcome.
    /// Owns all mutable game state: interest, traps, momentum, history, turn count.
    /// Sequences calls to RollEngine (stateless), ILlmAdapter, and interest/trap tracking.
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
        private readonly string? _previousOpener;

        // Combo tracking (#46)
        private readonly ComboTracker _comboTracker;

        // Callback tracking (#47)
        private readonly List<CallbackOpportunity> _topics;

        // Shadow growth tracking fields (#44)
        private readonly List<StatType> _statsUsedPerTurn;
        private readonly List<bool> _highestPctOptionPicked;
        private int _honestySuccessCount;
        private int _tropeTrapCount;
        private bool _tropeTrapMadnessTriggered;
        private int _saUsageCount;
        private bool _saOverthinkingTriggered;
        private string? _sessionOpener;

        private int _momentumStreak;
        private int _pendingMomentumBonus;
        private int _turnNumber;
        private bool _ended;
        private GameOutcome? _outcome;

        // XP tracking (#48)
        private readonly XpLedger _xpLedger;

        // Rule resolver for data-driven game constants (#463)
        private readonly IRuleResolver? _rules;

        // Weakness window from opponent's last response (#49)
        private WeaknessWindow? _activeWeakness;

        // Tell from opponent's last response (#50)
        private Tell? _activeTell;

        // Horniness session roll (#45)
        private int _sessionHorniness;

        // Nat 20 crit advantage (#271) — §4: previous crit grants advantage for 1 roll
        private bool _pendingCritAdvantage;

        // Shadow threshold tracking (#45)
        private StatType? _lastStatUsed;
        private HashSet<StatType>? _shadowDisadvantagedStats;
        private Dictionary<ShadowStatType, int>? _currentShadowThresholds;

        // Stored between StartTurnAsync and ResolveTurnAsync
        private DialogueOption[]? _currentOptions;
        private bool _currentHasAdvantage;
        private bool _currentHasDisadvantage;

        public GameSession(
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry)
            : this(player, opponent, llm, dice, trapRegistry, null)
        {
        }

        /// <summary>
        /// Creates a new GameSession with optional configuration.
        /// When config is null, behavior is identical to the 5-parameter constructor.
        /// </summary>
        public GameSession(
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            GameSessionConfig? config)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _opponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _dice = dice ?? throw new ArgumentNullException(nameof(dice));
            _trapRegistry = trapRegistry ?? throw new ArgumentNullException(nameof(trapRegistry));

            // Store optional config fields early (needed by ResolveThresholdLevel below)
            _clock = config?.Clock;
            _playerShadows = config?.PlayerShadows;
            _opponentShadows = config?.OpponentShadows;
            _previousOpener = config?.PreviousOpener;
            _rules = config?.Rules;

            // Determine starting interest: explicit config > Dread T3 > default
            if (config?.StartingInterest.HasValue == true)
            {
                _interest = new InterestMeter(config.StartingInterest.Value);
            }
            else if (config?.PlayerShadows != null
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

            // Roll session Horniness (1d10) every session + time-of-day modifier when clock available
            {
                int horninessRoll = _dice.Roll(10);
                int todModifier = _clock?.GetHorninessModifier() ?? 0;
                _sessionHorniness = Math.Max(0, horninessRoll + todModifier);
            }

            // XP tracking (#48)
            _xpLedger = new XpLedger();

            // Combo tracking (#46)
            _comboTracker = new ComboTracker();

            // Callback tracking (#47)
            _topics = new List<CallbackOpportunity>();

            // Shadow growth tracking (#44)
            _statsUsedPerTurn = new List<StatType>();
            _highestPctOptionPicked = new List<bool>();
            _honestySuccessCount = 0;
            _tropeTrapCount = 0;
            _tropeTrapMadnessTriggered = false;
            _saUsageCount = 0;
            _saOverthinkingTriggered = false;
            _sessionOpener = null;

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

        /// <summary>The full XP ledger for this session.</summary>
        public XpLedger XpLedger => _xpLedger;

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
                    // Store raw shadow value (not tier) so LLM prompt builder
                    // can check fine-grained thresholds (e.g. >5 for T1 taint).
                    shadowThresholds[shadow] = effectiveVal;
                    int tier = ResolveThresholdLevel(effectiveVal);

                    // T2+: paired positive stat gets disadvantage
                    if (tier >= 2)
                    {
                        // Reverse lookup: find which StatType is paired with this shadow
                        foreach (var kvp in StatBlock.ShadowPairs)
                        {
                            if (kvp.Value == shadow)
                            {
                                _shadowDisadvantagedStats.Add(kvp.Key);
                                break;
                            }
                        }
                    }
                }
            }

            // Store player shadow thresholds for use in ResolveTurnAsync (#308)
            _currentShadowThresholds = shadowThresholds;

            // Get trap names and LLM instructions for context
            var activeTrapNames = GetActiveTrapNames();
            var activeTrapInstructions = GetActiveTrapInstructions();

            // Build dialogue context — pass callback topics (#47) and shadow thresholds (#45)
            var context = new DialogueContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GetLastOpponentMessage(),
                activeTraps: activeTrapNames,
                currentInterest: _interest.Current,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                callbackOpportunities: _topics.Count > 0 ? new List<CallbackOpportunity>(_topics) : null,
                horninessLevel: _sessionHorniness,
                requiresRizzOption: _sessionHorniness >= 12,
                currentTurn: _turnNumber,
                playerTextingStyle: _player.TextingStyleFragment,
                activeTell: _activeTell);

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
                // Fixation T3: force all options to use the same stat as last turn
                if (shadowThresholds.TryGetValue(ShadowStatType.Fixation, out int fixRaw)
                    && fixRaw >= 18 && _lastStatUsed.HasValue)
                {
                    var forcedStat = _lastStatUsed.Value;
                    for (int i = 0; i < options.Length; i++)
                    {
                        var o = options[i];
                        options[i] = new DialogueOption(
                            forcedStat, o.IntendedText, o.CallbackTurnNumber,
                            o.ComboName, o.HasTellBonus, o.HasWeaknessWindow);
                    }
                }

                // Denial T3: remove Honesty options
                if (shadowThresholds.TryGetValue(ShadowStatType.Denial, out int denRaw)
                    && denRaw >= 18)
                {
                    var filtered = options.Where(o => o.Stat != StatType.Honesty).ToArray();
                    if (filtered.Length == 0)
                    {
                        // Fallback: prefer Chaos, else first option
                        var chaos = options.FirstOrDefault(o => o.Stat == StatType.Chaos);
                        filtered = new[] { chaos ?? options[0] };
                    }
                    options = filtered;
                }

                // Madness T3: replace one random option with unhinged replacement marker
                if (shadowThresholds.TryGetValue(ShadowStatType.Madness, out int madRaw)
                    && madRaw >= 18
                    && options.Length > 0)
                {
                    int unhingedIdx = _dice.Roll(options.Length) - 1;
                    var o = options[unhingedIdx];
                    options[unhingedIdx] = new DialogueOption(
                        o.Stat, o.IntendedText, o.CallbackTurnNumber,
                        o.ComboName, o.HasTellBonus, o.HasWeaknessWindow,
                        isUnhingedReplacement: true);
                }
            }

            // Horniness T3 (#45): all options become Rizz
            if (_sessionHorniness >= 18)
            {
                for (int i = 0; i < options.Length; i++)
                {
                    var o = options[i];
                    options[i] = new DialogueOption(StatType.Rizz, o.IntendedText, o.CallbackTurnNumber,
                        o.ComboName, o.HasTellBonus, o.HasWeaknessWindow, o.IsUnhingedReplacement);
                }
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
            int tellBonus = (_activeTell != null && chosenOption.Stat == _activeTell.Stat) ? 2 : 0;

            // Compute external bonus: tell + callback + Triple combo + momentum (#46, #47, #50, #268)
            int externalBonus = tellBonus + callbackBonus + _pendingMomentumBonus;
            if (_comboTracker.HasTripleBonus)
            {
                externalBonus += 1;
                _comboTracker.ConsumeTripleBonus(); // Consume after applying (#46 edge case 7)
            }

            // Compute DC adjustment from weakness window (#49)
            int dcAdjustment = 0;
            if (_activeWeakness != null
                && StatBlock.DefenceTable[chosenOption.Stat] == _activeWeakness.DefendingStat)
            {
                dcAdjustment = _activeWeakness.DcReduction;
            }

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
            int interestDelta;
            if (rollResult.IsSuccess)
            {
                interestDelta = ResolveSuccessInterestDelta(rollResult);
                interestDelta += RiskTierBonus.GetInterestBonus(rollResult);
            }
            else
            {
                interestDelta = ResolveFailureInterestDelta(rollResult);
            }

            // 3. Update momentum streak (bonus was already applied as externalBonus in the roll, #268)
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
            }

            // 3c. Track last stat used for Fixation T3 (#45)
            _lastStatUsed = chosenOption.Stat;

            // 3d. Combo detection (#46)
            _comboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess);
            var combo = _comboTracker.CheckCombo();
            string? comboTriggered = null;
            if (combo != null)
            {
                interestDelta += combo.InterestBonus;
                comboTriggered = combo.Name;
            }

            // 3d. Record roll XP (#48)
            RecordRollXp(rollResult);

            // 4. Record interest before applying delta
            int interestBefore = _interest.Current;
            InterestState stateBefore = ResolveInterestState();

            // 5. Apply interest delta
            _interest.Apply(interestDelta);

            int interestAfter = _interest.Current;
            InterestState stateAfter = ResolveInterestState();

            // ---- Shadow growth evaluation (#44) ----
            EvaluatePerTurnShadowGrowth(chosenOption, optionIndex, rollResult, interestAfter);

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

            // Check end conditions for end-of-game triggers
            bool isGameOver = false;
            GameOutcome? outcome = null;

            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
                isGameOver = true;
                outcome = GameOutcome.Unmatched;
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
                EvaluateEndOfGameShadowGrowth(outcome!.Value);
                RecordEndOfGameXp(outcome!.Value);
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

            // 6. Advance trap timers
            _traps.AdvanceTurn();

            // 7. Deliver message via LLM
            var deliveryTrapNames = GetActiveTrapNames();
            var deliveryTrapInstructions = GetActiveTrapInstructions();

            int beatDcBy = rollResult.IsSuccess ? rollResult.FinalTotal - rollResult.DC : 0;

            var deliveryContext = new DeliveryContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GetLastOpponentMessage(),
                chosenOption: chosenOption,
                outcome: rollResult.Tier,
                beatDcBy: beatDcBy,
                activeTraps: deliveryTrapNames,
                activeTrapInstructions: deliveryTrapInstructions,
                playerName: _player.DisplayName,
                opponentName: _opponent.DisplayName,
                currentTurn: _turnNumber,
                shadowThresholds: _currentShadowThresholds,
                isNat20: rollResult.IsNatTwenty);

            string deliveredMessage = await _llm.DeliverMessageAsync(deliveryContext).ConfigureAwait(false);

            // 8. Append player message to history
            _history.Add((_player.DisplayName, deliveredMessage));

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (stateBefore != stateAfter)
            {
                narrativeBeat = $"*** Interest state changed to {stateAfter} ***";
            }

            // 10. Compute response delay
            double responseDelayMinutes = _opponent.Timing.ComputeDelay(_interest.Current, _dice);

            // 11. Generate opponent response
            var opponentTrapInstructions = GetActiveTrapInstructions();

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

            var opponentContext = new OpponentContext(
                playerPrompt: _player.AssembledSystemPrompt,
                opponentPrompt: _opponent.AssembledSystemPrompt,
                conversationHistory: _history.AsReadOnly(),
                opponentLastMessage: GetLastOpponentMessage(),
                activeTraps: GetActiveTrapNames(),
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
                deliveryTier: rollResult.Tier);

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
                detectedWindow: opponentResponse.WeaknessWindow);
        }

        /// <summary>
        /// Records XP for a roll result following §10 precedence rules:
        /// Nat 20 → 25 XP (overrides DC-tier), Nat 1 → 10 XP (overrides failure),
        /// success → 5/10/15 by DC tier, failure → 2.
        /// </summary>
        private void RecordRollXp(RollResult rollResult)
        {
            if (rollResult.IsNatTwenty)
            {
                _xpLedger.Record("Nat20", 25);
            }
            else if (rollResult.IsNatOne)
            {
                _xpLedger.Record("Nat1", 10);
            }
            else if (rollResult.IsSuccess)
            {
                int baseXp;
                if (rollResult.DC <= 13)
                    baseXp = 5;
                else if (rollResult.DC <= 17)
                    baseXp = 10;
                else
                    baseXp = 15;

                int xp = ApplyRiskTierMultiplier(baseXp, rollResult.RiskTier);

                string label = rollResult.DC <= 13 ? "Success_DC_Low"
                    : rollResult.DC <= 17 ? "Success_DC_Mid"
                    : "Success_DC_High";
                _xpLedger.Record(label, xp);
            }
            else
            {
                _xpLedger.Record("Failure", 2);
            }
        }

        /// <summary>
        /// Applies the risk-tier XP multiplier per risk-reward doc:
        /// Safe=1x, Medium=1.5x, Hard=2x, Bold=3x.
        /// </summary>
        private int ApplyRiskTierMultiplier(int baseXp, RiskTier riskTier)
        {
            if (_rules != null)
            {
                var resolved = _rules.GetRiskTierXpMultiplier(riskTier);
                if (resolved.HasValue)
                    return (int)Math.Round(baseXp * resolved.Value);
            }

            double multiplier;
            if (riskTier == RiskTier.Bold)
                multiplier = 3.0;
            else if (riskTier == RiskTier.Hard)
                multiplier = 2.0;
            else if (riskTier == RiskTier.Medium)
                multiplier = 1.5;
            else
                multiplier = 1.0;

            return (int)Math.Round(baseXp * multiplier);
        }

        /// <summary>
        /// Records end-of-game XP based on the game outcome.
        /// DateSecured → 50, Unmatched/Ghosted → 5.
        /// </summary>
        private void RecordEndOfGameXp(GameOutcome outcome)
        {
            if (outcome == GameOutcome.DateSecured)
                _xpLedger.Record("DateSecured", 50);
            else if (outcome == GameOutcome.Unmatched || outcome == GameOutcome.Ghosted)
                _xpLedger.Record("ConversationComplete", 5);
        }

        /// <summary>
        /// Evaluates per-turn shadow growth triggers after a Speak action resolves.
        /// Applies growth to _playerShadows when available.
        /// </summary>
        private void EvaluatePerTurnShadowGrowth(
            DialogueOption chosenOption,
            int optionIndex,
            RollResult rollResult,
            int interestAfter)
        {
            if (_playerShadows == null)
                return;

            // Trigger 1: Nat 1 → +1 to paired shadow
            if (rollResult.IsNatOne)
            {
                var pairedShadow = StatBlock.ShadowPairs[chosenOption.Stat];
                _playerShadows.ApplyGrowth(pairedShadow, 1,
                    $"Nat 1 on {chosenOption.Stat}");
            }

            // Trigger 2: Catastrophic Wit failure → +1 Dread
            if (chosenOption.Stat == StatType.Wit
                && !rollResult.IsSuccess
                && rollResult.Tier == FailureTier.Catastrophe)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Dread, 1,
                    "Catastrophic Wit failure (miss by 10+)");
            }

            // Trigger 3: TropeTrap count → +1 Madness at 3
            if (!rollResult.IsSuccess && rollResult.Tier >= FailureTier.TropeTrap
                && rollResult.Tier != FailureTier.Legendary) // Legendary is Nat 1, separate tier
            {
                _tropeTrapCount++;
                if (_tropeTrapCount == 3 && !_tropeTrapMadnessTriggered)
                {
                    _tropeTrapMadnessTriggered = true;
                    _playerShadows.ApplyGrowth(ShadowStatType.Madness, 1,
                        "3+ trope traps in one conversation");
                }
            }
            // Legendary (Nat 1) also counts as a trope-trap-tier failure per spec (tier >= TropeTrap)
            if (!rollResult.IsSuccess && rollResult.Tier == FailureTier.Legendary)
            {
                _tropeTrapCount++;
                if (_tropeTrapCount == 3 && !_tropeTrapMadnessTriggered)
                {
                    _tropeTrapMadnessTriggered = true;
                    _playerShadows.ApplyGrowth(ShadowStatType.Madness, 1,
                        "3+ trope traps in one conversation");
                }
            }

            // Trigger 4: Same stat 3 turns in a row → +1 Fixation
            _statsUsedPerTurn.Add(chosenOption.Stat);
            if (_statsUsedPerTurn.Count >= 3)
            {
                int tail = _statsUsedPerTurn.Count;
                if (_statsUsedPerTurn[tail - 1] == _statsUsedPerTurn[tail - 2]
                    && _statsUsedPerTurn[tail - 2] == _statsUsedPerTurn[tail - 3])
                {
                    // Count consecutive same-stat at tail
                    int consecutiveCount = 1;
                    for (int i = tail - 2; i >= 0; i--)
                    {
                        if (_statsUsedPerTurn[i] == _statsUsedPerTurn[tail - 1])
                            consecutiveCount++;
                        else
                            break;
                    }
                    // Trigger every 3 consecutive (at 3, 6, 9, ...)
                    if (consecutiveCount >= 3 && consecutiveCount % 3 == 0)
                    {
                        _playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1,
                            $"Same stat ({chosenOption.Stat}) used 3 turns in a row");
                    }
                }
            }

            // Trigger 5: Highest-% option picked 3 turns in a row → +1 Fixation
            _highestPctOptionPicked.Add(IsHighestProbabilityOption(chosenOption, _currentOptions!));
            if (_highestPctOptionPicked.Count >= 3)
            {
                int tail = _highestPctOptionPicked.Count;
                if (_highestPctOptionPicked[tail - 1]
                    && _highestPctOptionPicked[tail - 2]
                    && _highestPctOptionPicked[tail - 3])
                {
                    // Count consecutive trues at tail
                    int consecutiveCount = 0;
                    for (int i = tail - 1; i >= 0; i--)
                    {
                        if (_highestPctOptionPicked[i])
                            consecutiveCount++;
                        else
                            break;
                    }
                    if (consecutiveCount >= 3 && consecutiveCount % 3 == 0)
                    {
                        _playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1,
                            "Highest-% option picked 3 turns in a row");
                    }
                }
            }

            // Trigger 6: Honesty success tracking + Denial reduction at high interest
            if (chosenOption.Stat == StatType.Honesty && rollResult.IsSuccess)
            {
                _honestySuccessCount++;

                // Shadow reduction: Honesty success at Interest ≥15 → Denial −1
                if (interestAfter >= 15)
                {
                    _playerShadows.ApplyOffset(ShadowStatType.Denial, -1,
                        "Honesty success at high interest");
                }
            }

            // Trigger 7: Interest hits 0 → +2 Dread
            if (interestAfter == 0)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Dread, 2,
                    "Interest hit 0 (unmatch)");
            }

            // Trigger 9: SA used 3+ times → +1 Overthinking (once)
            if (chosenOption.Stat == StatType.SelfAwareness)
            {
                _saUsageCount++;
                if (_saUsageCount == 3 && !_saOverthinkingTriggered)
                {
                    _saOverthinkingTriggered = true;
                    _playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1,
                        "SA used 3+ times in one conversation");
                }
            }

            // Trigger 14: Same opener twice in a row → +1 Madness
            if (_turnNumber == 0) // first turn
            {
                _sessionOpener = chosenOption.IntendedText;
                if (_previousOpener != null
                    && string.Equals(
                        _sessionOpener.Trim(),
                        _previousOpener.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _playerShadows.ApplyGrowth(ShadowStatType.Madness, 1,
                        "Same opener twice in a row");
                }
            }
        }

        /// <summary>
        /// Evaluates end-of-game shadow growth triggers.
        /// </summary>
        private void EvaluateEndOfGameShadowGrowth(GameOutcome outcome)
        {
            if (_playerShadows == null)
                return;

            // Shadow reduction: Date secured → Dread −1
            if (outcome == GameOutcome.DateSecured)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Dread, -1,
                    "Date secured");
            }

            // Trigger 11: Date secured without Honesty success → +1 Denial
            if (outcome == GameOutcome.DateSecured && _honestySuccessCount == 0)
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Denial, 1,
                    "Date secured without any Honesty successes");
            }

            // Trigger 12: Never picked Chaos → +1 Fixation
            if (!_statsUsedPerTurn.Contains(StatType.Chaos))
            {
                _playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1,
                    "Never picked Chaos in whole conversation");
            }

            // Trigger 13: 4+ different stats used → −1 Fixation (offset)
            int distinctStats = _statsUsedPerTurn.Distinct().Count();
            if (distinctStats >= 4)
            {
                _playerShadows.ApplyOffset(ShadowStatType.Fixation, -1,
                    "4+ different stats used in conversation");
            }
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
        /// Falls back to FailureScale.GetInterestDelta().
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
        /// Falls back to SuccessScale.GetInterestDelta().
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
        /// Falls back to InterestMeter.GetState().
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
        /// Falls back to ShadowThresholdEvaluator.GetThresholdLevel().
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
            var trapNames = _traps.AllActive
                .Select(t => t.Definition.Id)
                .ToArray();

            return new GameStateSnapshot(
                interest: _interest.Current,
                state: ResolveInterestState(),
                momentumStreak: _momentumStreak,
                activeTrapNames: trapNames,
                turnNumber: _turnNumber,
                tripleBonusActive: _comboTracker.HasTripleBonus);
        }

        private string GetLastOpponentMessage()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].Sender == _opponent.DisplayName)
                    return _history[i].Text;
            }
            return string.Empty;
        }

        private List<string> GetActiveTrapNames()
        {
            return _traps.AllActive
                .Select(t => t.Definition.Id)
                .ToList();
        }

        /// <summary>
        /// Read action: SA vs DC 12. Success reveals interest. Failure: −1 interest + Overthinking +1.
        /// Self-contained turn action — does NOT require StartTurnAsync() first.
        /// Clears _currentOptions if set. Checks end conditions independently.
        /// </summary>
        /// <exception cref="GameEndedException">If the game has already ended or ghost trigger fires.</exception>
        public Task<ReadResult> ReadAsync()
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

            // 4b. Capture and consume triple bonus if active (#312 — apply to Read roll)
            bool hadTripleBonus = _comboTracker.HasTripleBonus;
            _comboTracker.ConsumeTripleBonus();

            // 5. Determine advantage/disadvantage from interest state + shadow thresholds (#260)
            bool hasAdvantage = _interest.GrantsAdvantage;
            bool hasDisadvantage = _interest.GrantsDisadvantage;

            // Nat 20 crit advantage (#271) — previous crit grants advantage for 1 roll
            if (_pendingCritAdvantage)
            {
                hasAdvantage = true;
                _pendingCritAdvantage = false;
            }

            // Shadow-based SA disadvantage: Overthinking T2+ → SA gets disadvantage
            if (_playerShadows != null)
            {
                int overthinkingVal = _playerShadows.GetEffectiveShadow(ShadowStatType.Overthinking);
                if (ResolveThresholdLevel(overthinkingVal) >= 2)
                {
                    hasDisadvantage = true;
                }
            }

            // 6. Roll SA vs DC 12 (with triple bonus if active, #312)
            int tripleBonus = hadTripleBonus ? 1 : 0;
            var roll = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness,
                _player.Stats,
                12,
                _traps,
                _player.Level,
                _trapRegistry,
                _dice,
                hasAdvantage,
                hasDisadvantage,
                externalBonus: tripleBonus);

            // 6b. Nat 20 crit advantage (#271) — set for next roll
            if (roll.IsNatTwenty)
            {
                _pendingCritAdvantage = true;
            }

            // 7. Resolve outcome
            var shadowEvents = new List<string>();
            int? interestValue;

            if (roll.IsSuccess)
            {
                interestValue = _interest.Current;
            }
            else
            {
                interestValue = null;
                _interest.Apply(-1);

                // Overthinking +1 via SessionShadowTracker if available (#44 trigger 10)
                if (_playerShadows != null)
                {
                    string evt = _playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Read failed");
                    shadowEvents.Add(evt);
                }
            }

            // 7b. Read does not grant XP per §10 (#48)
            int xp = 0;

            // 8. Advance trap timers
            _traps.AdvanceTurn();

            // 9. Increment turn
            _turnNumber++;

            // 10. Check end conditions after interest change
            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
            }

            // 11. Build snapshot and return
            var snapshot = CreateSnapshot();
            return Task.FromResult(new ReadResult(roll.IsSuccess, interestValue, roll, snapshot, xp, shadowEvents));
        }

        /// <summary>
        /// Recover action: SA vs DC 12. Success clears one active trap. Failure: −1 interest + Overthinking +1.
        /// Throws InvalidOperationException if no traps active (TrapState.HasActive == false).
        /// Self-contained turn action — does NOT require StartTurnAsync() first.
        /// </summary>
        /// <exception cref="GameEndedException">If the game has already ended or ghost trigger fires.</exception>
        /// <exception cref="InvalidOperationException">If no traps are active.</exception>
        public Task<RecoverResult> RecoverAsync()
        {
            // 1. Check if game already ended
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            // 2. Check HasActive before anything else
            if (!_traps.HasActive)
                throw new InvalidOperationException("Cannot recover: no active trap.");

            // 3. Check interest end conditions
            CheckInterestEndConditions();

            // 4. Ghost trigger check
            CheckGhostTrigger();

            // 5. Clear pending Speak options and weakness window (#49)
            _currentOptions = null;
            _activeWeakness = null;
            _activeTell = null;

            // 5b. Capture and consume triple bonus if active (#312 — apply to Recover roll)
            bool hadTripleBonus = _comboTracker.HasTripleBonus;
            _comboTracker.ConsumeTripleBonus();

            // 6. Determine advantage/disadvantage from interest state + shadow thresholds (#260)
            bool hasAdvantage = _interest.GrantsAdvantage;
            bool hasDisadvantage = _interest.GrantsDisadvantage;

            // Nat 20 crit advantage (#271) — previous crit grants advantage for 1 roll
            if (_pendingCritAdvantage)
            {
                hasAdvantage = true;
                _pendingCritAdvantage = false;
            }

            // Shadow-based SA disadvantage: Overthinking T2+ → SA gets disadvantage
            if (_playerShadows != null)
            {
                int overthinkingVal = _playerShadows.GetEffectiveShadow(ShadowStatType.Overthinking);
                if (ResolveThresholdLevel(overthinkingVal) >= 2)
                {
                    hasDisadvantage = true;
                }
            }

            // 7. Roll SA vs DC 12 (with triple bonus if active, #312)
            int tripleBonus = hadTripleBonus ? 1 : 0;
            var roll = RollEngine.ResolveFixedDC(
                StatType.SelfAwareness,
                _player.Stats,
                12,
                _traps,
                _player.Level,
                _trapRegistry,
                _dice,
                hasAdvantage,
                hasDisadvantage,
                externalBonus: tripleBonus);

            // 7b. Nat 20 crit advantage (#271) — set for next roll
            if (roll.IsNatTwenty)
            {
                _pendingCritAdvantage = true;
            }

            // 8. Resolve outcome
            string? clearedTrapName = null;

            if (roll.IsSuccess)
            {
                // Clear first active trap
                var firstTrap = _traps.AllActive.First();
                clearedTrapName = firstTrap.Definition.Id;
                _traps.Clear(firstTrap.Definition.Stat);

                // Record trap recovery XP (#48 AC-5)
                _xpLedger.Record("TrapRecovery", 15);

                // Shadow reduction: Recovering from trope trap → Madness −1
                if (_playerShadows != null)
                {
                    _playerShadows.ApplyOffset(ShadowStatType.Madness, -1,
                        "Recovered from trope trap");
                }
            }
            else
            {
                _interest.Apply(-1);

                // Overthinking +1 on Recover failure (#44 trigger 10)
                if (_playerShadows != null)
                {
                    _playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Recover failed");
                }
            }

            // 8b. Drain XP events for this action (#48)
            var recoverXpEvents = _xpLedger.DrainTurnEvents();
            int xp = 0;
            for (int i = 0; i < recoverXpEvents.Count; i++)
                xp += recoverXpEvents[i].Amount;

            // 9. Advance trap timers
            _traps.AdvanceTurn();

            // 10. Increment turn
            _turnNumber++;

            // 11. Check end conditions after interest change
            if (_interest.IsZero)
            {
                _ended = true;
                _outcome = GameOutcome.Unmatched;
            }

            // 12. Build snapshot and return
            var snapshot = CreateSnapshot();
            return Task.FromResult(new RecoverResult(roll.IsSuccess, clearedTrapName, roll, snapshot, xp));
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
        /// Includes shadow growth for ghost Dread +1.
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
        }

        /// <summary>
        /// Collects the LLM instruction text from all currently active traps.
        /// Returns null if no traps are active (avoids empty array allocation).
        /// </summary>
        private string[]? GetActiveTrapInstructions()
        {
            var instructions = _traps.AllActive
                .Select(t => t.Definition.LlmInstruction)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            return instructions.Length > 0 ? instructions : null;
        }

        /// <summary>
        /// Determines whether the chosen option has the highest (or tied-for-highest)
        /// success probability among all available options.
        /// Probability is based on attacker stat modifier + level bonus vs defender DC.
        /// </summary>
        private bool IsHighestProbabilityOption(DialogueOption chosen, DialogueOption[] options)
        {
            int levelBonus = LevelTable.GetBonus(_player.Level);

            // Compute "roll margin" for chosen option: higher = easier to succeed
            // margin = statMod + levelBonus - DC (more positive = higher probability)
            int chosenMargin = _player.Stats.GetEffective(chosen.Stat) + levelBonus
                               - _opponent.Stats.GetDefenceDC(chosen.Stat);

            for (int i = 0; i < options.Length; i++)
            {
                int margin = _player.Stats.GetEffective(options[i].Stat) + levelBonus
                             - _opponent.Stats.GetDefenceDC(options[i].Stat);
                if (margin > chosenMargin)
                    return false; // Another option has strictly higher probability
            }

            return true; // Chosen is highest or tied for highest
        }
    }
}
