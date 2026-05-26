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
            return await _turnOrchestrator.StartTurnAsync(
                _state,
                _player,
                _opponent,
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
            return await _turnOrchestrator.ResolveTurnAsync(
                _state,
                optionIndex,
                _player,
                _opponent,
                progress,
                ct).ConfigureAwait(false);
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
    }
}