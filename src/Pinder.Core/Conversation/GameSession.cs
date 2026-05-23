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
    public sealed class GameSession
    {
        private readonly CharacterProfile _player;
        private readonly CharacterProfile _opponent;
        private readonly ILlmAdapter _llm;
        private readonly IDiceRoller _dice;
        private readonly ITrapRegistry _trapRegistry;

        private readonly GameSessionState _state;

        private InterestMeter _interest { get => _state.Interest; set => _state.Interest = value; }
        private TrapState _traps { get => _state.Traps; set => _state.Traps = value; }
        private List<(string Sender, string Text)> _history { get => _state.History; set => _state.History = value; }

        // #562: outfit / scene description set by SeedSceneEntries so the
        // dialogue-options call site can surface it to the player as part
        // of the opponent's visible-profile (Tinder-card-equivalent)
        // payload, replacing the raw equipped-items list. Empty when no
        // describer was wired or the call failed; renderer then falls
        // back to the items list.
        private string _opponentOutfitDescription { get => _state.OpponentOutfitDescription; set => _state.OpponentOutfitDescription = value; }

        // #788: opponent LLM conversation history lives here, not in the adapter.
        // The adapter is pure-stateless across calls; the engine passes this list
        // in on every opponent call and appends the new entries returned by the
        // adapter. Survives snapshot/restore via ResimulateData.OpponentHistory.
        private List<ConversationMessage> _opponentHistory { get => _state.OpponentHistory; set => _state.OpponentHistory = value; }

        // Sprint 8 Wave 0: optional config fields
        private readonly IGameClock? _clock;
        private SessionShadowTracker? _playerShadows { get => _state.PlayerShadows; set => _state.PlayerShadows = value; }
        private SessionShadowTracker? _opponentShadows { get => _state.OpponentShadows; set => _state.OpponentShadows = value; }

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

        // Weakness window from opponent's last response (#49)
        private WeaknessWindow? _activeWeakness { get => _state.ActiveWeakness; set => _state.ActiveWeakness = value; }

        // Tell from opponent's last response (#50)
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
        private readonly object? _statDeliveryInstructions;

        // #314: optional callback invoked when a text-transform layer (Horniness /
        // Shadow / Trap overlay) ran an LLM call but produced byte-identical
        // output. Lets the host distinguish "layer ran but no-op" from "layer
        // didn't run" in audit logs. Null when not configured — same shape as
        // before the field existed.
        private readonly Action<TextLayerNoopEvent>? _onTextLayerNoop;

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
        // Dedicated RNG for OptionFilterEngine.DrawRandomStats. Kept separate from
        // the steering RNG so tests can queue exact steering values without our
        // stat-draw shuffle consuming them (see issue #130).
        private Random? _statDrawRng;
        private readonly IConsequenceCatalog? _consequenceCatalog;

        internal GameSessionState State => _state;

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
            _state = new GameSessionState();

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
            _statDrawRng = config.StatDrawRng;
            _statDeliveryInstructions = config.StatDeliveryInstructions;
            _onTextLayerNoop = config.OnTextLayerNoop;

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
            _steeringEngine = new SteeringEngine(steeringRng);
            _horninessEngine = new HorninessEngine(steeringRng, _consequenceCatalog);
            _shadowCheckEngine = new ShadowCheckEngine(steeringRng, _consequenceCatalog);

            // #788: stateful opponent context now lives on this GameSession
            // (_opponentHistory). The adapter is pure-stateless and is fed the
            // history on each opponent call. No initialisation needed here —
            // the list starts empty and grows after every successful opponent
            // call in ResolveTurnAsync.
        }

        /// <summary>
        /// #790 (Phase 4): private clone constructor used by
        /// <see cref="Clone"/>. Copies <paramref name="src"/>'s state
        /// field-by-field into the new instance, with all mutable engine
        /// state deep-copied so the clone is fully independent of the parent.
        ///
        /// <para>
        /// Sharing rules (categorised per plan v2 §2):
        /// </para>
        /// <list type="bullet">
        ///   <item><b>Shared by reference (immutable / stateless / pure):</b>
        ///     <see cref="_player"/>, <see cref="_opponent"/>,
        ///     <see cref="_llm"/>, <see cref="_dice"/>,
        ///     <see cref="_trapRegistry"/>, <see cref="_clock"/>,
        ///     <see cref="_rules"/>, <see cref="_statDeliveryInstructions"/>,
        ///     <see cref="_onTextLayerNoop"/>. The active fast-gameplay
        ///     scheduler (#425) is expected to inject branch-specific
        ///     <see cref="Pinder.Core.Rolls.PerOptionDicePool"/> via
        ///     <see cref="InjectNextDicePool"/>; <see cref="_dice"/> itself
        ///     is not consumed on the resolve path after Phase 2.</item>
        ///   <item><b>Deep-copied (mutable engine state):</b> every other
        ///     field. Includes value-type fields (turn number, momentum,
        ///     pending bonuses, horniness session roll, etc.) and
        ///     reference-type fields (interest meter, trap state, combo
        ///     tracker, XP ledger, shadow trackers, shadow-growth
        ///     evaluator, conversation history lists, topic list, etc.).</item>
        ///   <item><b>Deep-copied via reflection:</b>
        ///     <see cref="System.Random"/> RNGs (steering / stat-draw) via
        ///     <see cref="RandomCloner"/>. The cloned RNGs produce the same
        ///     next-sequence as the parent at clone-time but never share
        ///     internal seed state.</item>
        /// </list>
        ///
        /// <para>
        /// Reflection-based completeness pin: <c>Phase4_GameSessionCloneTests</c>
        /// enumerates every instance field on <see cref="GameSession"/> via
        /// reflection and asserts each is either explicitly listed in this
        /// constructor's allowlist or is a documented shared-by-reference
        /// field. Adding a new mutable field to <see cref="GameSession"/>
        /// without extending this constructor will fail-fast at test time.
        /// </para>
        /// </summary>
        private GameSession(GameSession src) : this(src, src._llm) { }

        /// <summary>
        /// #425 (Phase 5): private clone constructor that swaps the LLM
        /// adapter on the cloned instance. Used by the fast-gameplay
        /// scheduler so each speculative branch can capture LLM I/O into
        /// its own per-branch <c>ITurnSnapshotSink</c>: the adapter wraps
        /// a per-branch transport that decorates the shared session
        /// transport with a branch-scoped <c>SnapshotRecordingLlmTransport</c>.
        /// </summary>
        private GameSession(GameSession src, ILlmAdapter llmOverride)
        {
            // ── Shared-by-reference fields (Category B/C: immutable / stateless / pure adapters) ──
            _player          = src._player;
            _opponent        = src._opponent;
            _llm             = llmOverride ?? throw new ArgumentNullException(nameof(llmOverride));
            _dice            = src._dice;
            _trapRegistry    = src._trapRegistry;
            _clock           = src._clock;
            _rules           = src._rules;
            _globalDcBias    = src._globalDcBias;
            _statDeliveryInstructions = src._statDeliveryInstructions;
            _onTextLayerNoop = src._onTextLayerNoop;
            _consequenceCatalog = src._consequenceCatalog;

            // ── Mutable engine state — deep copies (Category A) ──
            _state           = src._state.Clone();

            _shadowGrowthEvaluator = src._shadowGrowthEvaluator != null && _state.PlayerShadows != null
                ? src._shadowGrowthEvaluator.Clone(_state.PlayerShadows)
                : null;
            _xpRecorder      = new SessionXpRecorder(_state.XpLedger, _rules);

            // ── RNG state — deep-cloned for independent forks (§2.3) ──
            // Steering RNG is shared between SteeringEngine and HorninessEngine
            // in the public ctor; preserve that shape on the clone.
            var clonedSteeringRng = RandomCloner.Clone(src._steeringEngine.SteeringRngForCloneOnly);
            _steeringEngine  = new SteeringEngine(clonedSteeringRng);
            _horninessEngine = new HorninessEngine(clonedSteeringRng, _consequenceCatalog);
            _shadowCheckEngine = new ShadowCheckEngine(clonedSteeringRng, _consequenceCatalog);
            _statDrawRng     = src._statDrawRng != null ? RandomCloner.Clone(src._statDrawRng) : null;
        }

        /// <summary>
        /// #790 (Phase 4): produce a fully-independent <see cref="GameSession"/>
        /// snapshot of the current engine state. The clone shares
        /// configuration / character / adapter / clock references with the
        /// parent (those are immutable or stateless), but every piece of
        /// mutable game state — interest, traps, momentum, shadow trackers,
        /// XP ledger, conversation histories, RNG state, per-turn carry-over
        /// — is deep-copied. Mutating either side does not affect the other.
        ///
        /// <para>
        /// <b>Use case (#393 Phase 5):</b> the fast-gameplay scheduler
        /// (#425) calls <c>Clone()</c> three times per turn after
        /// <see cref="StartTurnAsync"/> resolves but before the player
        /// commits to an option, then runs <see cref="ResolveTurnAsync"/>
        /// in parallel on each fork with a different
        /// <see cref="Pinder.Core.Rolls.PerOptionDicePool"/> injected via
        /// <see cref="InjectNextDicePool"/>. After the player commits, the
        /// chosen branch's session replaces the parent; the other two are
        /// discarded. This requires that:
        /// </para>
        /// <list type="number">
        ///   <item>Mutating one branch (e.g. by running
        ///     <see cref="ResolveTurnAsync"/>) does not perturb the parent
        ///     or the other branches — the <i>independence</i> property,
        ///     locked by <c>Phase4_GameSessionCloneTests.Clone_IsIndependent</c>.</item>
        ///   <item>Running the same option through the parent and a clone
        ///     with the same injected dice pool produces a byte-identical
        ///     post-state (after <see cref="CreateSnapshot"/>) — the
        ///     <i>determinism</i> property, locked by
        ///     <c>Phase4_GameSessionCloneTests.Clone_IsDeterministic</c>.</item>
        ///   <item>Adding a new mutable field on <see cref="GameSession"/>
        ///     without extending the clone constructor fails fast — the
        ///     <i>completeness</i> property, locked by
        ///     <c>Phase4_GameSessionCloneTests.Clone_CoversEveryGameSessionField</c>.</item>
        /// </list>
        ///
        /// <para>
        /// <b>Path 1 vs Path 2 decision:</b> see PR #790 / fix/790-game-session-clone
        /// body. Path 1 (explicit Clone()) chosen over Path 2
        /// (snapshot-and-restore round-trip) because the existing
        /// <see cref="CreateSnapshot"/> / <see cref="RestoreState"/> machinery
        /// is materially incomplete (no XP ledger, no horniness session
        /// roll, no opponent shadow tracker, no shadow-growth evaluator
        /// counters, no per-turn carry-over). Path 2 would have required
        /// expanding both Snapshot and RestoreState to full coverage — a
        /// much larger blast radius on the production replay path. The
        /// reflection-based completeness test guards against the Path 1
        /// risk ("future PR adds field, forgets to extend Clone").
        /// </para>
        /// </summary>
        /// <returns>An independent <see cref="GameSession"/> with a deep
        /// copy of every piece of mutable engine state.</returns>
        public GameSession Clone() => new GameSession(this);

        /// <summary>
        /// #425 (Phase 5): produce an independent clone whose LLM adapter
        /// is replaced by <paramref name="llm"/>. Every other piece of
        /// state is deep-copied per the documented sharing rules on
        /// <see cref="Clone()"/>; only the adapter reference is swapped.
        /// Used by the fast-gameplay scheduler so each speculative
        /// branch's LLM exchanges land in its own per-branch sink.
        /// </summary>
        /// <param name="llm">
        /// Replacement LLM adapter. The caller is responsible for ensuring
        /// the adapter is functionally equivalent to the parent's adapter
        /// (same model, same prompt-assembly behaviour) — typically a
        /// fresh <see cref="ILlmAdapter"/> wrapping a per-branch
        /// <c>SnapshotRecordingLlmTransport</c> over the session's shared
        /// inner transport. Must not be <c>null</c>.
        /// </param>
        public GameSession Clone(ILlmAdapter llm)
        {
            if (llm == null) throw new ArgumentNullException(nameof(llm));
            return new GameSession(this, llm);
        }

        /// <summary>
        /// #425 (Phase 5): adopt the mutable engine state of
        /// <paramref name="src"/> into this session, preserving this
        /// session's <see cref="_llm"/> + dependency references. Inverse
        /// of <see cref="Clone(ILlmAdapter)"/>: a parent session can call
        /// <c>parent.AdoptStateFrom(chosenClone)</c> after the
        /// fast-gameplay scheduler resolves three speculative branches
        /// against three clones; the chosen branch's clone holds the
        /// authoritative post-resolve state, which we transplant back
        /// into the parent.
        ///
        /// <para>
        /// The shared-by-reference fields documented on <see cref="Clone(ILlmAdapter)"/>
        /// are <em>preserved</em> on the parent (LLM adapter, dice
        /// roller, trap registry, clock, rules — these were already
        /// shared with the source's parent at clone time, so no swap is
        /// needed). Every mutable field is overwritten with a deep copy
        /// (or, for value types, a copy by value) of <paramref name="src"/>'s
        /// state.
        /// </para>
        ///
        /// <para>
        /// Mirrors the field-by-field assignment in the clone
        /// constructor exactly so that
        /// <c>parent.Clone() → clone.Resolve() → parent.AdoptStateFrom(clone)</c>
        /// is byte-equivalent to <c>parent.Resolve()</c> on a session
        /// with the same dice pool injected.
        /// </para>
        /// </summary>
        public void AdoptStateFrom(GameSession src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            // Delegate core mutable state adoption to GameSessionState
            _state.AdoptStateFrom(src._state);

            // Re-initialize/adopt the retained single-responsibility modules
            _shadowGrowthEvaluator = src._shadowGrowthEvaluator != null && _state.PlayerShadows != null
                ? src._shadowGrowthEvaluator.Clone(_state.PlayerShadows)
                : null;
            _xpRecorder      = new SessionXpRecorder(_state.XpLedger, _rules);

            // RNGs (deep-clone to avoid sharing internal state with src).
            var clonedSteeringRng = RandomCloner.Clone(src._steeringEngine.SteeringRngForCloneOnly);
            _steeringEngine  = new SteeringEngine(clonedSteeringRng);
            _horninessEngine = new HorninessEngine(clonedSteeringRng, _consequenceCatalog);
            _shadowCheckEngine = new ShadowCheckEngine(clonedSteeringRng, _consequenceCatalog);
            _statDrawRng     = src._statDrawRng != null ? RandomCloner.Clone(src._statDrawRng) : null;
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

        /// <summary>Current 0-based turn number. Incremented by ResolveTurnAsync.</summary>
        public int TurnNumber => _turnNumber;

        /// <summary>True after the session has reached a terminal <see cref="GameOutcome"/>.</summary>
        public bool IsEnded => _ended;

        /// <summary>Terminal outcome, or null while the session is still running.</summary>
        public GameOutcome? Outcome => _outcome;

        /// <summary>
        /// Restore an already-ended session from persisted state. Sets the
        /// terminal flags so subsequent <see cref="StartTurnAsync"/> throws
        /// <see cref="GameEndedException"/> with the right outcome.
        ///
        /// Intended for post-game replay/rehydration paths (e.g. loading a
        /// finished session back from storage). <see cref="RestoreState"/>
        /// targets mid-game resimulation and deliberately does not touch the
        /// terminal flags; callers reviving an ended session must call this
        /// in addition.
        /// </summary>
        /// <param name="outcome">The terminal <see cref="GameOutcome"/> the session ended with.</param>
        public void MarkEnded(GameOutcome outcome)
        {
            _ended = true;
            _outcome = outcome;
        }

        /// <summary>
        /// Conversation history as (sender, text) tuples, in emission order.
        /// Read-only snapshot view; safe to enumerate concurrently with session mutation
        /// since the underlying list is only appended during ResolveTurnAsync.
        /// </summary>
        /// <remarks>
        /// Includes any turn-0 scene-setting entries (issue #333) tagged with
        /// <see cref="Senders.Scene"/>. Callers that feed the history back
        /// into an LLM should use <see cref="BuildHistoryForLlmContext"/>
        /// instead so the analyzer/delivery LLM does not see the scene
        /// entries.
        /// </remarks>
        public System.Collections.Generic.IReadOnlyList<(string Sender, string Text)> ConversationHistory
            => _history;

        /// <summary>
        /// #788: opponent-LLM conversation history owned by the engine. Each
        /// entry's role is <c>"user"</c> or <c>"assistant"</c>. Read-only view
        /// over the live mutable list so callers see updates as turns resolve.
        /// Survives snapshot/restore via
        /// <see cref="ResimulateData.OpponentHistory"/>.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<ConversationMessage> OpponentHistory
            => _opponentHistory;

        /// <summary>
        /// Build the conversation history view fed to subsequent LLM calls.
        /// Excludes synthetic scene-setting entries (issue #333) so the
        /// matchup analyser / delivery LLM / opponent-response LLM never
        /// sees its own scene-description output as prior conversation.
        /// </summary>
        private System.Collections.Generic.IReadOnlyList<(string Sender, string Text)> BuildHistoryForLlmContext()
        {
            // Hot path: when there are no scene entries, return the full
            // list as-is so we don’t allocate a copy on every turn.
            bool anyScene = false;
            for (int i = 0; i < _history.Count; i++)
            {
                if (Senders.IsScene(_history[i].Sender)) { anyScene = true; break; }
            }
            if (!anyScene) return _history.AsReadOnly();

            var view = new List<(string Sender, string Text)>(_history.Count);
            for (int i = 0; i < _history.Count; i++)
            {
                var entry = _history[i];
                if (Senders.IsScene(entry.Sender)) continue;
                view.Add(entry);
            }
            return view.AsReadOnly();
        }

        /// <summary>
        /// Issue #333: append the three turn-0 scene-setting entries
        /// (player bio, opponent bio, LLM-generated outfit description) to
        /// the conversation log BEFORE the first player turn. Sender for
        /// each entry is <see cref="Senders.Scene"/>; the frontend renders
        /// these distinctly from player/opponent dialogue.
        /// </summary>
        /// <param name="playerBio">Player bio text. Empty entries are skipped.</param>
        /// <param name="opponentBio">Opponent bio text. Empty entries are skipped.</param>
        /// <param name="outfitDescription">LLM-generated outfit description. Empty entries are skipped.</param>
        /// <exception cref="InvalidOperationException">If any turn has already been resolved.</exception>
        public void SeedSceneEntries(string? playerBio, string? opponentBio, string? outfitDescription)
        {
            if (_turnNumber > 0)
            {
                throw new InvalidOperationException(
                    "SeedSceneEntries must be called before the first turn is resolved.");
            }
            if (!string.IsNullOrWhiteSpace(playerBio))
                _history.Add(($"{Senders.Scene}:{_player.DisplayName}", playerBio!.Trim()));
            if (!string.IsNullOrWhiteSpace(opponentBio))
                _history.Add(($"{Senders.Scene}:{_opponent.DisplayName}", opponentBio!.Trim()));
            if (!string.IsNullOrWhiteSpace(outfitDescription))
            {
                string trimmed = outfitDescription!.Trim();
                _history.Add((Senders.Scene, trimmed));
                // #562: also retain on the session so
                // BuildOpponentVisibleProfile can surface it on every
                // dialogue-options call. Scene-history entries are
                // excluded from the LLM context view, so without this
                // field the player-LLM never sees the outfit.
                _opponentOutfitDescription = trimmed;
            }
        }

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
            _state.RestoreFromSnapshot(data, trapRegistry);
        }

        /// <summary>
        /// Start a new turn. Checks end conditions, determines advantage/disadvantage,
        /// and fetches dialogue options from the LLM adapter.
        /// </summary>
        /// <param name="ct">
        /// Cancellation token forwarded to the LLM adapter call (#794). Defaults to
        /// <c>default</c> for backwards compatibility — existing callers that don't
        /// pass a token continue to work unchanged.
        /// </param>
        /// <exception cref="GameEndedException">If the game has already ended.</exception>
        /// <exception cref="OperationCanceledException">If <paramref name="ct"/> is cancelled.</exception>
        public async Task<TurnStart> StartTurnAsync(CancellationToken ct = default)
        {
            return await TurnProcessor.StartTurnAsync(
                _state,
                _player,
                _opponent,
                _llm,
                _dice,
                _trapRegistry,
                _clock,
                _rules,
                _statDeliveryInstructions,
                _onTextLayerNoop,
                _statDrawRng,
                _globalDcBias,
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// #789 Phase 2 (D1) — fill the chosen option's dice pool at the
        /// start of <c>ResolveTurnAsync</c>, BEFORE any LLM call fires.
        ///
        /// <para>
        /// Returns a <see cref="Pinder.Core.Rolls.PerOptionDicePool"/>
        /// containing the exact per-option dice budget for the chosen option:
        /// 1× d20 (main) + optional 1× d20 (advantage/disadvantage) +
        /// 1× d100 (timing variance). Drawn from the underlying <c>_dice</c>
        /// in the order <c>RollEngine</c> and <c>TimingProfile</c> will consume
        /// them, so the wrapping <see cref="Pinder.Core.Rolls.PlaybackDiceRoller"/>
        /// replays them in FIFO order.
        /// </para>
        ///
        /// <para>
        /// Lazy fill keeps the per-turn <c>_dice</c> budget at 1 + (2 or 3)
        /// — unchanged from the pre-Phase-2 flow — so Phase 0 I2's fixture
        /// keeps passing. Only the chosen pool is filled; the other
        /// placeholders returned from <c>StartTurnAsync</c> remain empty.
        /// </para>
        /// </summary>
        /// <summary>
        /// #425 (Phase 5): pre-fill the per-option dice pools for every
        /// option in the current turn, drawing from <c>_dice</c> in
        /// option-index order. After this call <see cref="CurrentDicePools"/>
        /// returns a fully populated pool for each option, suitable for
        /// injection into a cloned <see cref="GameSession"/> via
        /// <see cref="InjectNextDicePool"/>.
        ///
        /// <para>
        /// Idempotent: if all pools are already populated (e.g. a prior
        /// <c>EnsureAllDicePoolsFilled</c> call already ran for this
        /// turn), this call is a no-op. The shared <c>_dice</c> source
        /// is consumed once per option — N × (2-or-3 dice) per turn.
        /// </para>
        ///
        /// <para>
        /// MUST be called between <see cref="StartTurnAsync"/> and
        /// <see cref="ResolveTurnAsync(int)"/>. Calling without an
        /// active turn throws <see cref="InvalidOperationException"/>.
        /// </para>
        /// </summary>
        /// <returns>
        /// The fully-populated per-option pools. Same array as
        /// <see cref="CurrentDicePools"/> after the call — returned for
        /// caller convenience.
        /// </returns>
        public Pinder.Core.Rolls.PerOptionDicePool[] EnsureAllDicePoolsFilled()
        {
            if (_currentOptions == null || _currentDicePools == null)
                throw new InvalidOperationException(
                    "EnsureAllDicePoolsFilled requires an active turn (StartTurnAsync first).");

            for (int i = 0; i < _currentOptions.Length; i++)
            {
                if (_currentDicePools[i].Count > 0) continue;
                bool resolveHasDisadvantage = _currentHasDisadvantage;
                _currentDicePools[i] = FillChosenDicePool(
                    i, _currentOptions[i], resolveHasDisadvantage);
            }
            return _currentDicePools;
        }

        /// <summary>
        /// #425 (Phase 5): read-only accessor for the current turn's
        /// dice pools. Returns <c>null</c> when no turn is active.
        /// </summary>
        public IReadOnlyList<Pinder.Core.Rolls.PerOptionDicePool>? CurrentDicePools
            => _currentDicePools;

        private Pinder.Core.Rolls.PerOptionDicePool FillChosenDicePool(
            int optionIndex, DialogueOption chosenOption, bool resolveHasDisadvantage)
        {
            // Mirror RollEngine.Resolve's trap-derived disadvantage logic
            // (RollEngine.cs:41-49). Single-slot trap state means the active
            // trap may be on any stat; we only force disadvantage if the
            // trap is active on THIS option's stat AND its effect is
            // Disadvantage. The caller already factored shadow-pair
            // disadvantage into <paramref name="resolveHasDisadvantage"/>.
            bool trapDisadvantage = false;
            var activeTrap = _traps.GetActive(chosenOption.Stat);
            if (activeTrap != null
                && activeTrap.Definition.Effect == Pinder.Core.Traps.TrapEffect.Disadvantage)
                trapDisadvantage = true;

            bool rollTwice = _currentHasAdvantage || resolveHasDisadvantage || trapDisadvantage;

            // Worst-case-static budget: d20 [+ d20 if rollTwice] + d100.
            int rolls = rollTwice ? 3 : 2;
            var values = new int[rolls];
            int idx = 0;
            values[idx++] = _dice.Roll(20); // RollEngine.cs:52 main d20
            if (rollTwice)
                values[idx++] = _dice.Roll(20); // RollEngine.cs:53 second d20 (adv/disadv)
            values[idx++] = _dice.Roll(100); // TimingProfile.cs:53 timing variance d100

            return new Pinder.Core.Rolls.PerOptionDicePool(optionIndex, values);
        }

        /// <summary>
        /// Resolve a turn after the player selects an option.
        /// Sequences: roll → interest delta → momentum → shadow growth → trap advance → deliver → opponent response.
        /// </summary>
        /// <param name="optionIndex">Index into the options array from StartTurnAsync.</param>
        /// <exception cref="GameEndedException">If the game has already ended.</exception>
        /// <exception cref="InvalidOperationException">If StartTurnAsync was not called first or index is invalid.</exception>
        public Task<TurnResult> ResolveTurnAsync(int optionIndex)
            => ResolveTurnAsync(optionIndex, progress: null, ct: default);

        /// <summary>
        /// #789 Phase 2 (D1) — inject a specific <see cref="Pinder.Core.Rolls.PerOptionDicePool"/>
        /// for the next <c>ResolveTurnAsync</c> call, replacing the engine's
        /// natural draw from <c>_dice</c>. Single-use: cleared after the next
        /// resolve. Used by deterministic replay tooling and the W3B
        /// determinism test ("two resolves with the same pool produce
        /// byte-equivalent post-state").
        ///
        /// <para>
        /// The injected pool must contain enough values to satisfy the
        /// chosen option's runtime dice consumption (1× d20 + optional 1×
        /// d20 + 1× d100). Under-allocation throws
        /// <see cref="InvalidOperationException"/> from
        /// <c>PlaybackDiceRoller</c>. Over-allocation leaves the pool
        /// non-drained — the I2-equivalent invariant fails loudly.
        /// </para>
        /// </summary>
        public void InjectNextDicePool(Pinder.Core.Rolls.PerOptionDicePool pool)
        {
            _injectedNextPool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <summary>
        /// Resolve the current turn with an optional progress reporter.
        /// <paramref name="progress"/> receives one <see cref="TurnProgressEvent"/>
        /// per coarse stage of the multi-LLM resolution pipeline. Intended for
        /// the web session-runner's SSE endpoint — see <see cref="TurnProgressStage"/>.
        /// </summary>
        /// <remarks>
        /// This overload exists for backward compatibility with callers that
        /// pre-date the cancellation-token thread (#794). It delegates to the
        /// CT-aware overload with <c>ct = default</c>.
        /// </remarks>
        public Task<TurnResult> ResolveTurnAsync(int optionIndex, System.IProgress<TurnProgressEvent>? progress)
            => ResolveTurnAsync(optionIndex, progress, ct: default);

        /// <summary>
        /// Resolve the current turn with an optional progress reporter and
        /// cancellation token. The token is forwarded to every awaited LLM
        /// adapter call inside the resolution pipeline (steering, delivery,
        /// trap overlay, horniness overlay, shadow corruption, opponent
        /// response). When the token is cancelled mid-turn the engine surfaces
        /// <see cref="OperationCanceledException"/> at the next adapter call;
        /// the post-cancel observable invariants are documented in
        /// <c>docs/development/regression-pins-787.md</c> and locked by the
        /// Phase 0 I6 / F3 invariant tests (#794, prerequisite for the
        /// fast-gameplay scheduler #425).
        /// </summary>
        public async Task<TurnResult> ResolveTurnAsync(int optionIndex, System.IProgress<TurnProgressEvent>? progress, CancellationToken ct)
        {
            return await TurnProcessor.ResolveTurnAsync(
                _state,
                optionIndex,
                _player,
                _opponent,
                _llm,
                _dice,
                _trapRegistry,
                _rules,
                _consequenceCatalog,
                _shadowGrowthEvaluator,
                _xpRecorder,
                _steeringEngine,
                _horninessEngine,
                _shadowCheckEngine,
                progress,
                _statDeliveryInstructions,
                _onTextLayerNoop,
                _globalDcBias,
                ct).ConfigureAwait(false);
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

        /// <summary>
        /// Build a fresh <see cref="GameStateSnapshot"/> for the current session state.
        /// Public so test/debug code can observe restored or mid-flight state without
        /// running a turn (e.g. the W2a #371 RestoreState round-trip tests).
        /// </summary>
        public GameStateSnapshot CreateSnapshot()
        {
            return GameSessionHelpers.CreateSnapshot(
                _interest,
                ResolveInterestState(),
                _momentumStreak,
                _traps,
                _turnNumber,
                _comboTracker.HasTripleBonus,
                _opponentHistory);
        }

        /// <summary>
        /// Wait action: −1 interest, advance trap timers. No roll.
        /// Synchronous — no LLM calls.
        /// Self-contained turn action — does NOT require StartTurnAsync() first.
        /// </summary>
        /// <remarks>
        /// #957: uniform transactional contract — helpers (CheckInterestEndConditions,
        /// CheckGhostTrigger) and step-8 check do not mutate state before throwing.
        /// Callers catch GameEndedException and call session.MarkEnded(ex.Outcome) +
        /// reapply shadow growth from ex.ShadowGrowthEffects.
        /// </remarks>
        /// <exception cref="GameEndedException">If the game has already ended or ghost trigger fires.</exception>
        public void Wait()
        {
            // 1. Check if game already ended
            if (_ended)
                throw new GameEndedException(_outcome!.Value);

            // 2 & 3. Check interest end conditions and ghost trigger (transactional — #957)
            GameSessionRulesEvaluator.EvaluateEndState(_interest, _playerShadows, _dice, _rules);

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

            // 8. Check end conditions after interest change (transactional — #957)
            GameSessionRulesEvaluator.CheckInterestEndConditions(_interest, _playerShadows);
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
        /// <remarks>
        /// #920: explicitly synthesises a <see cref="Pinder.Core.Rolls.RollCheckResult"/> via
        /// <see cref="Pinder.Core.Rolls.RollCheckResult.Synthesise"/> so the returned
        /// <see cref="Pinder.Core.Rolls.RollResult.Check"/> is never null — prerequisite for the
        /// Phase 2 wire-DTO serializer which will read <c>Check.*</c>.
        /// </remarks>
        private static RollResult CreateForcedFailResult(RollResult original, FailureTier shadowTier)
        {
            // Build a result that looks like a miss at the given tier.
            // We derive a miss margin that maps to the tier, then compute a die roll that misses DC.
            int fakeDie = original.DC > 1 ? original.DC - 1 : 1; // just below DC
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

        // ── #314: text-layer no-op breadcrumb ─────────────────────────────

        /// <summary>
        /// Issue #314: emit a structured event when a text-transform layer
        /// (Horniness / Shadow / Trap overlay) ran an LLM call but produced
        /// byte-identical output. The diff is silently dropped from
        /// <c>TextDiffs</c> in that case (correctly — there's nothing to
        /// render), but without this breadcrumb the audit cannot tell
        /// "layer ran and produced no delta" apart from "layer didn't run
        /// at all". Hosts that wire <c>OnTextLayerNoop</c> can log a
        /// structured INFO line with <c>{turn, layer, before_hash,
        /// after_hash}</c>.
        /// </summary>
        private void EmitTextLayerNoop(string layer, string beforeText, string afterText)
        {
            if (_onTextLayerNoop == null) return;
            try
            {
                string beforeHash = ComputeStableHash(beforeText);
                string afterHash = ComputeStableHash(afterText);
                _onTextLayerNoop(new TextLayerNoopEvent(_turnNumber, layer, beforeHash, afterHash));
            }
            catch
            {
                // Diagnostic-only path — never let a logging failure break
                // the turn. Swallow and move on.
            }
        }

        /// <summary>
        /// Stable, non-cryptographic, run-independent hash for the layer-noop
        /// breadcrumb. Uses SHA-256 truncated to 16 hex chars; the value is
        /// an audit identifier, not a security primitive.
        /// </summary>
        private static string ComputeStableHash(string? text)
        {
            if (text == null) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < 8 && i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
