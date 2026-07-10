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
    /// <summary>
    /// Orchestrates a single Pinder conversation from match to outcome.
    /// Owns all mutable game state: interest, traps, momentum, history, turn count.
    /// Sequences calls to RollEngine (stateless), ILlmAdapter, and interest/trap tracking.
    /// Delegates shadow growth, XP recording, steering, horniness, and option filtering
    /// to dedicated single-responsibility modules.
    /// </summary>
    public sealed partial class GameSession
    {
        private readonly CharacterProfile _player;
        private readonly CharacterProfile _datee;
        private readonly ILlmAdapter _llm;
        private readonly IDiceRoller _dice;
        private readonly ITrapRegistry _trapRegistry;

        private readonly GameSessionState _state;

        private InterestMeter _interest { get => _state.Interest; set => _state.Interest = value; }
        private TrapState _traps { get => _state.Traps; set => _state.Traps = value; }
        private List<(string Sender, string Text)> _history { get => _state.History; set => _state.History = value; }

        // #562: outfit / scene description set by SeedSceneEntries so the
        // dialogue-options call site can surface it to the player as part
        // of the datee's visible-profile (Tinder-card-equivalent)
        // payload, replacing the raw equipped-items list. Empty when no
        // describer was wired or the call failed; renderer then falls
        // back to the items list.
        private string _dateeOutfitDescription { get => _state.DateeOutfitDescription; set => _state.DateeOutfitDescription = value; }

        // #788: datee LLM conversation history lives here, not in the adapter.
        // The adapter is pure-stateless across calls; the engine passes this list
        // in on every datee call and appends the new entries returned by the
        // adapter. Survives snapshot/restore via ResimulateData.DateeHistory.
        private List<ConversationMessage> _dateeHistory { get => _state.DateeHistory; set => _state.DateeHistory = value; }

        // #1123: avatar LLM conversation history lives here, symmetric to
        // _dateeHistory. The avatar (delivery) session is now stateful — the
        // engine passes this list in on every avatar call and appends the new
        // entries returned by the adapter. Survives snapshot/restore via
        // ResimulateData.AvatarHistory.
        private List<ConversationMessage> _avatarHistory { get => _state.AvatarHistory; set => _state.AvatarHistory = value; }

        // Sprint 8 Wave 0: optional config fields
        private readonly IGameClock? _clock;
        private SessionShadowTracker? _playerShadows { get => _state.PlayerShadows; set => _state.PlayerShadows = value; }
        private SessionShadowTracker? _dateeShadows { get => _state.DateeShadows; set => _state.DateeShadows = value; }

        // Combo tracking (#46)
        private ComboTracker _comboTracker { get => _state.ComboTracker; set => _state.ComboTracker = value; }

        // Callback tracking (#47)
        private List<CallbackOpportunity> _topics { get => _state.Topics; set => _state.Topics = value; }

        // Despair (RIZZ failure shadow) tracking (#708, #717)
        private int _rizzCumulativeFailureCount { get => _state.RizzCumulativeFailureCount; set => _state.RizzCumulativeFailureCount = value; }

        private int _momentumStreak { get => _state.MomentumStreak; set => _state.MomentumStreak = value; }
        private int _pendingMomentumBonus { get => _state.PendingMomentumBonus; set => _state.PendingMomentumBonus = value; }
        private int _turnNumber { get => _state.TurnNumber; set => _state.TurnNumber = value; }
        private bool _ended { get => _state.Ended; set => _state.Ended = value; }
        private GameOutcome? _outcome { get => _state.Outcome; set => _state.Outcome = value; }

        // XP tracking (#48)
        private XpLedger _xpLedger { get => _state.XpLedger; set => _state.XpLedger = value; }

        // Rule resolver for data-driven game constants (#463)
        private readonly IRuleResolver? _rules;
        private readonly int _globalDcBias;
        private readonly int _shadowDcBias;
        private readonly int _horninessDcBias;

        // Weakness window from datee's last response (#49)
        private WeaknessWindow? _activeWeakness { get => _state.ActiveWeakness; set => _state.ActiveWeakness = value; }

        // Tell from datee's last response (#50)
        private Tell? _activeTell { get => _state.ActiveTell; set => _state.ActiveTell = value; }

        // Horniness session roll (#45)
        private int _sessionHorniness { get => _state.SessionHorniness; set => _state.SessionHorniness = value; }
        private int _horninessRoll { get => _state.HorninessRoll; set => _state.HorninessRoll = value; }
        private int _horninessTimeModifier { get => _state.HorninessTimeModifier; set => _state.HorninessTimeModifier = value; }

        // Nat 20 crit advantage (#271) — §4: previous crit grants advantage for 1 roll
        private bool _pendingCritAdvantage { get => _state.PendingCritAdvantage; set => _state.PendingCritAdvantage = value; }

        // Shadow threshold tracking (#45)
        private StatType? _lastStatUsed { get => _state.LastStatUsed; set => _state.LastStatUsed = value; }
        private HashSet<StatType>? _shadowDisadvantagedStats { get => _state.ShadowDisadvantagedStats; set => _state.ShadowDisadvantagedStats = value; }
        private Dictionary<ShadowStatType, int>? _currentShadowThresholds { get => _state.CurrentShadowThresholds; set => _state.CurrentShadowThresholds = value; }

        // Stat delivery instructions for horniness overlay tier lookups (#709)
        private readonly IStatDeliveryInstructionProvider? _statDeliveryInstructions;

        // #314: optional callback invoked when a text-transform layer (Horniness /
        // Shadow / Trap overlay) ran an LLM call but produced byte-identical
        // output. Lets the host distinguish "layer ran but no-op" from "layer
        // didn't run" in audit logs. Null when not configured — same shape as
        // before the field existed.
        private readonly Action<TextLayerNoopEvent>? _onTextLayerNoop;

        // #1218: optional callback invoked when shadow filtering changes the option/stat pool.
        private readonly Action<ShadowFilterTraceEvent>? _onShadowFilterTrace;

        // #1219: optional callback invoked when a rule resolution occurs.
        private readonly Action<RuleResolutionTraceEvent>? _onRuleResolution;

        // Optional host-controlled diagnostic callback. Null means no-op.
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;

        private readonly double _activeTrapInterestPenalty;

        // Stored between StartTurnAsync and ResolveTurnAsync
        private DialogueOption[]? _currentOptions { get => _state.CurrentOptions; set => _state.CurrentOptions = value; }
        private bool _currentHasAdvantage { get => _state.CurrentHasAdvantage; set => _state.CurrentHasAdvantage = value; }
        private bool _currentHasDisadvantage { get => _state.CurrentHasDisadvantage; set => _state.CurrentHasDisadvantage = value; }
        // #789 Phase 2 — pre-rolled dice pools, one per option index. Set by
        // StartTurnAsync (display-time placeholders), filled lazily by
        // ResolveTurnAsync via PlaybackDiceRoller. Null between turns.
        private Pinder.Core.Rolls.PerOptionDicePool[]? _currentDicePools { get => _state.CurrentDicePools; set => _state.CurrentDicePools = value; }
        // #789 Phase 2 — single-use replay/test injection slot. When non-null,
        // the next ResolveTurnAsync uses this pool verbatim instead of drawing
        // a fresh one from _dice. Cleared after consumption. Set via
        // <see cref="InjectNextDicePool"/>.
        private Pinder.Core.Rolls.PerOptionDicePool? _injectedNextPool { get => _state.InjectedNextPool; set => _state.InjectedNextPool = value; }

        // Extracted single-responsibility modules
        private ShadowGrowthEvaluator? _shadowGrowthEvaluator;
        private SessionXpRecorder _xpRecorder;
        private SteeringEngine _steeringEngine;
        private ShadowCheckEngine _shadowCheckEngine;
        private HorninessEngine _horninessEngine;
        private TurnOrchestrator _turnOrchestrator;
        // Dedicated RNG for OptionFilterEngine.DrawRandomStats. Kept separate from
        // the steering RNG so tests can queue exact steering values without our
        // stat-draw shuffle consuming them (see issue #130).
        private Random? _statDrawRng;
        private readonly IConsequenceCatalog? _consequenceCatalog;
        private readonly int _maxDialogueOptions;
        private readonly int _maxDeliveryWords;
        private readonly int _hungerForIntimacy;
        private readonly int _terrorOfRejection;

        internal GameSessionState State => _state;

        /// <summary>
        /// Creates a new GameSession with required configuration.
        /// Config must be non-null — no silent fallbacks.
        /// </summary>
        public GameSession(
            CharacterProfile player,
            CharacterProfile datee,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            GameSessionConfig config)
        {
            _state = new GameSessionState();

            _player = player ?? throw new ArgumentNullException(nameof(player));
            _datee = datee ?? throw new ArgumentNullException(nameof(datee));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _dice = dice ?? throw new ArgumentNullException(nameof(dice));
            _trapRegistry = trapRegistry ?? throw new ArgumentNullException(nameof(trapRegistry));

            if (config == null)
                throw new ArgumentNullException(nameof(config), "GameSessionConfig is required — no silent defaults");

            // Store config fields early (needed by ResolveThresholdLevel below)
            _clock = config.Clock;
            _playerShadows = config.PlayerShadows;
            _dateeShadows = config.DateeShadows;
            _rules = config.Rules;
            _globalDcBias = config.GlobalDcBias;
            _shadowDcBias = config.ShadowDcBias;
            _horninessDcBias = config.HorninessDcBias;
            // #790/#425 follow-up (audit 2026-07-10): default to a CloneableRandom (not a
            // plain System.Random) so the fast-gameplay scheduler's session forking
            // (GameSession.Clone / AdoptStateFrom) works without reflecting into
            // System.Random's private internals. Callers that inject an explicit
            // steeringRng and never clone the session may still pass any Random.
            var steeringRng = config.SteeringRng ?? new CloneableRandom();
            _statDrawRng = config.StatDrawRng;
            _statDeliveryInstructions = config.StatDeliveryInstructions;
            _onTextLayerNoop = config.OnTextLayerNoop;
            _onShadowFilterTrace = config.OnShadowFilterTrace;
            _onRuleResolution = config.OnRuleResolution;
            _onDiagnostic = config.OnDiagnostic;
            _activeTrapInterestPenalty = config.ActiveTrapInterestPenalty;
            _hungerForIntimacy = config.HungerForIntimacy;
            _terrorOfRejection = config.TerrorOfRejection;

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
            _consequenceCatalog = config.ConsequenceCatalog;
            _maxDialogueOptions = config.MaxDialogueOptions ?? 3;
            _maxDeliveryWords = config.MaxDeliveryWords ?? 80;
            _steeringEngine = new SteeringEngine(steeringRng, _onDiagnostic);
            _horninessEngine = new HorninessEngine(steeringRng, _consequenceCatalog, _horninessDcBias);
            _shadowCheckEngine = new ShadowCheckEngine(steeringRng, _consequenceCatalog, _shadowDcBias);

            _turnOrchestrator = BuildTurnOrchestrator();

            // #788: stateful datee context now lives on this GameSession
            // (_dateeHistory). The adapter is pure-stateless and is fed the
            // history on each datee call. No initialisation needed here —
            // the list starts empty and grows after every successful datee
            // call in ResolveTurnAsync.
        }

        private TurnOrchestrator BuildTurnOrchestrator()
        {
            var rollResolutionStage = new RollResolutionStage(
                _dice,
                _trapRegistry,
                _rules,
                _shadowGrowthEvaluator,
                _xpRecorder,
                _globalDcBias,
                _activeTrapInterestPenalty,
                _onRuleResolution);

            var deliveryStage = new DeliveryStage(
                _llm,
                _rules,
                _steeringEngine,
                _horninessEngine,
                _shadowCheckEngine,
                _statDeliveryInstructions,
                _onTextLayerNoop,
                _onDiagnostic,
                _maxDeliveryWords);

            var dateeResponseStage = new DateeResponseStage(_llm);

            return new TurnOrchestrator(
                _llm,
                _dice,
                _rules,
                _statDrawRng,
                rollResolutionStage,
                deliveryStage,
                dateeResponseStage,
                _maxDialogueOptions,
                _onShadowFilterTrace,
                _onRuleResolution,
                _hungerForIntimacy,
                _terrorOfRejection);
        }
    }
}
