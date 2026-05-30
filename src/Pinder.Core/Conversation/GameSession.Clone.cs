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
    public sealed partial class GameSession
    {
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
            _maxDialogueOptions = src._maxDialogueOptions;

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

            _turnOrchestrator = BuildTurnOrchestrator();
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

            _turnOrchestrator = BuildTurnOrchestrator();
        }
    }
}
