using System.Collections.Generic;

namespace Pinder.Rules
{
    /// <summary>
    /// Immutable snapshot of game state for rule condition evaluation.
    /// Constructed by the caller (GameSession or test) before evaluating rules.
    /// </summary>
    public sealed class GameState
    {
        public int Interest { get; }
        public int MissMargin { get; }
        public int BeatMargin { get; }
        public int NaturalRoll { get; }
        public int NeedToHit { get; }
        public int Level { get; }
        public int Streak { get; }
        public string? Action { get; }
        public bool IsConversationStart { get; }
        public IReadOnlyDictionary<string, int>? ShadowValues { get; }

        public GameState(
            int interest = 0,
            int missMargin = 0,
            int beatMargin = 0,
            int naturalRoll = 0,
            int needToHit = 0,
            int level = 1,
            int streak = 0,
            string? action = null,
            bool isConversationStart = false,
            IReadOnlyDictionary<string, int>? shadowValues = null)
        {
            Interest = interest;
            MissMargin = missMargin;
            BeatMargin = beatMargin;
            NaturalRoll = naturalRoll;
            NeedToHit = needToHit;
            Level = level;
            Streak = streak;
            Action = action;
            IsConversationStart = isConversationStart;
            ShadowValues = shadowValues;
        }
    }
}
