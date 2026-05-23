using System;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Offloads end-of-game checks and rules evaluation from GameSession.
    /// </summary>
    public static class GameSessionRulesEvaluator
    {
        /// <summary>
        /// Evaluates end-of-game rules (interest end conditions and ghost triggers).
        /// </summary>
        /// <exception cref="GameEndedException">Thrown if game has ended or ghost trigger fires.</exception>
        public static void EvaluateEndState(
            InterestMeter interest,
            SessionShadowTracker? playerShadows,
            IDiceRoller dice,
            IRuleResolver? rules)
        {
            CheckInterestEndConditions(interest, playerShadows);
            CheckGhostTrigger(interest, playerShadows, dice, rules);
        }

        /// <summary>
        /// Checks interest-based end conditions and throws GameEndedException if triggered.
        /// </summary>
        public static void CheckInterestEndConditions(
            InterestMeter interest,
            SessionShadowTracker? playerShadows)
        {
            if (interest.IsZero)
            {
                if (playerShadows != null)
                {
                    var dreadEvents = new[] { $"{ShadowStatType.Dread} +1 (Conversation ended without date)" };
                    var dreadEffects = new[] { new ShadowGrowthEffect(ShadowStatType.Dread, 1, "Conversation ended without date") };
                    throw new GameEndedException(GameOutcome.Unmatched, dreadEvents, dreadEffects);
                }
                throw new GameEndedException(GameOutcome.Unmatched);
            }

            if (interest.IsMaxed)
            {
                throw new GameEndedException(GameOutcome.DateSecured);
            }
        }

        /// <summary>
        /// Checks ghost trigger: if Bored state, 25% chance (dice.Roll(4)==1) to ghost.
        /// </summary>
        public static void CheckGhostTrigger(
            InterestMeter interest,
            SessionShadowTracker? playerShadows,
            IDiceRoller dice,
            IRuleResolver? rules)
        {
            InterestState interestState = ResolveInterestState(interest, rules);
            if (interestState == InterestState.Bored)
            {
                int ghostRoll = dice.Roll(4);
                if (ghostRoll == 1)
                {
                    if (playerShadows != null)
                    {
                        var events = new[] { $"{ShadowStatType.Dread} +1 (Ghosted)" };
                        var effects = new[] { new ShadowGrowthEffect(ShadowStatType.Dread, 1, "Ghosted") };
                        throw new GameEndedException(GameOutcome.Ghosted, events, effects);
                    }

                    throw new GameEndedException(GameOutcome.Ghosted);
                }
            }
        }

        private static InterestState ResolveInterestState(InterestMeter interest, IRuleResolver? rules)
        {
            if (rules != null)
            {
                var resolved = rules.GetInterestState(interest.Current);
                if (resolved.HasValue)
                    return resolved.Value;
            }
            return interest.GetState();
        }
    }
}
