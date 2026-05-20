using System;
using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Typed record for a single shadow growth event, carrying the same data as the
    /// human-readable string in <see cref="GameEndedException.ShadowGrowthEvents"/>.
    /// Callers that need to reapply growth (e.g. after catching a Ghosted throw)
    /// should consume <see cref="GameEndedException.ShadowGrowthEffects"/> rather
    /// than regex-parsing the string list.
    /// </summary>
    public sealed class ShadowGrowthEffect : IEquatable<ShadowGrowthEffect>
    {
        public ShadowStatType Stat { get; }
        public int Amount { get; }
        public string Reason { get; }

        public ShadowGrowthEffect(ShadowStatType stat, int amount, string reason)
        {
            Stat = stat;
            Amount = amount;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public override bool Equals(object obj) => obj is ShadowGrowthEffect other && Equals(other);
        public bool Equals(ShadowGrowthEffect other) =>
            !(other is null) && Stat == other.Stat && Amount == other.Amount && Reason == other.Reason;
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Stat.GetHashCode();
                hash = hash * 31 + Amount.GetHashCode();
                hash = hash * 31 + (Reason?.GetHashCode() ?? 0);
                return hash;
            }
        }
        public override string ToString() => $"{Stat} {Amount:+0;-#} ({Reason})";
    }

    /// <summary>
    /// Thrown when an operation is attempted on a GameSession that has already ended.
    /// </summary>
    public sealed class GameEndedException : InvalidOperationException
    {
        /// <summary>The outcome that ended the game.</summary>
        public GameOutcome Outcome { get; }

        /// <summary>Shadow growth events that occurred when the game ended (e.g., ghost Dread +1). Empty if none.</summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        /// <summary>
        /// Typed shadow growth effects parallel to <see cref="ShadowGrowthEvents"/>.
        /// Same events, same order — callers can reapply mechanically without parsing strings.
        /// </summary>
        public IReadOnlyList<ShadowGrowthEffect> ShadowGrowthEffects { get; }

        public GameEndedException(GameOutcome outcome)
            : base($"Game has ended with outcome: {outcome}")
        {
            Outcome = outcome;
            ShadowGrowthEvents = Array.Empty<string>();
            ShadowGrowthEffects = Array.Empty<ShadowGrowthEffect>();
        }

        public GameEndedException(GameOutcome outcome, string message)
            : base(message)
        {
            Outcome = outcome;
            ShadowGrowthEvents = Array.Empty<string>();
            ShadowGrowthEffects = Array.Empty<ShadowGrowthEffect>();
        }

        public GameEndedException(GameOutcome outcome, IReadOnlyList<string> shadowGrowthEvents)
            : base($"Game has ended with outcome: {outcome}")
        {
            Outcome = outcome;
            ShadowGrowthEvents = shadowGrowthEvents ?? Array.Empty<string>();
            ShadowGrowthEffects = Array.Empty<ShadowGrowthEffect>();
        }

        public GameEndedException(GameOutcome outcome, IReadOnlyList<string> shadowGrowthEvents, IReadOnlyList<ShadowGrowthEffect> shadowGrowthEffects)
            : base($"Game has ended with outcome: {outcome}")
        {
            Outcome = outcome;
            ShadowGrowthEvents = shadowGrowthEvents ?? Array.Empty<string>();
            ShadowGrowthEffects = shadowGrowthEffects ?? Array.Empty<ShadowGrowthEffect>();
        }
    }
}
