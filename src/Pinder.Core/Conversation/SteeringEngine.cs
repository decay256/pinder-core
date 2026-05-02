using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Handles the steering roll mechanic — attempts to append a date-steering
    /// question after message delivery.
    /// Uses a separate RNG to avoid consuming values from the game dice queue.
    /// </summary>
    internal sealed class SteeringEngine
    {
        private readonly Random _steeringRng;

        public SteeringEngine(Random steeringRng)
        {
            _steeringRng = steeringRng ?? throw new ArgumentNullException(nameof(steeringRng));
        }

        /// <summary>
        /// #790 (Phase 4): expose the underlying steering RNG instance ONLY
        /// to <see cref="GameSession.Clone"/> so it can be deep-cloned via
        /// <see cref="RandomCloner"/>. The same RNG is shared with
        /// <see cref="HorninessEngine"/> by construction in
        /// <see cref="GameSession"/>'s public constructor; the clone path
        /// preserves that sharing shape on the new session.
        ///
        /// <para>
        /// Internal accessor; not part of the public API contract. Do not
        /// use this in production game-logic code paths — use
        /// <see cref="RollD20"/> / <see cref="AttemptSteeringRollAsync"/>.
        /// </para>
        /// </summary>
        internal Random SteeringRngForCloneOnly => _steeringRng;

        /// <summary>
        /// Rolls a d20 using the steering RNG (separate from game dice).
        /// Used by the shadow check mechanic to avoid consuming game dice values.
        /// </summary>
        public int RollD20() => _steeringRng.Next(1, 21);

        /// <summary>
        /// Attempts a steering roll after message delivery.
        /// Steering modifier = average of (CHARM + WIT + SA) effective modifiers (integer division).
        /// Steering DC = 16 + average of opponent's (SA + RIZZ + HONESTY) effective modifiers.
        /// On success, calls LLM to generate a steering question.
        /// </summary>
        public async Task<SteeringRollResult> AttemptSteeringRollAsync(
            string deliveredMessage,
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IReadOnlyList<(string Sender, string Text)> history,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            // Compute steering modifier: (playerCharm + playerWit + playerSA) / 3
            int playerCharm = player.Stats.GetEffective(StatType.Charm);
            int playerWit = player.Stats.GetEffective(StatType.Wit);
            int playerSA = player.Stats.GetEffective(StatType.SelfAwareness);
            int steeringMod = (playerCharm + playerWit + playerSA) / 3;

            // Compute steering DC: 16 + (opponentSA + opponentRizz + opponentHonesty) / 3
            int opponentSA = opponent.Stats.GetEffective(StatType.SelfAwareness);
            int opponentRizz = opponent.Stats.GetEffective(StatType.Rizz);
            int opponentHonesty = opponent.Stats.GetEffective(StatType.Honesty);
            int steeringDC = 16 + (opponentSA + opponentRizz + opponentHonesty) / 3;

            // Roll d20 using a separate RNG.
            int roll = _steeringRng.Next(1, 21);

            int total = roll + steeringMod;
            bool success = total >= steeringDC;

            string? steeringQuestion = null;
            if (success && llm is IStatefulLlmAdapter stateful)
            {
                var steeringContext = new SteeringContext(
                    playerPrompt: player.AssembledSystemPrompt,
                    opponentName: opponent.DisplayName,
                    playerName: player.DisplayName,
                    deliveredMessage: deliveredMessage,
                    conversationHistory: history);

                try
                {
                    steeringQuestion = await stateful.GetSteeringQuestionAsync(steeringContext, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Cancellation must propagate — this is the engine-level
                    // cancellation contract (#794). Don't swallow it like a
                    // generic LLM transport failure.
                    throw;
                }
                catch
                {
                    // LLM failure should not break the game
                    steeringQuestion = null;
                    success = false;
                }
            }
            else if (success)
            {
                // Non-stateful adapter: mark as not attempted
                success = false;
            }

            return new SteeringRollResult(
                steeringAttempted: true,
                steeringSucceeded: success,
                steeringRoll: roll,
                steeringMod: steeringMod,
                steeringDC: steeringDC,
                steeringQuestion: steeringQuestion);
        }
    }
}
