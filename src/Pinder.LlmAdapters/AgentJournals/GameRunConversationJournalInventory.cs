using System;
using System.Collections.Generic;

namespace Pinder.LlmAdapters.AgentJournals
{
    public static class GameRunConversationJournalInventory
    {
        public const string DateePerformance = "game.datee.performance";
        public const string AvatarReply = "game.avatar.reply";
        public const string EmotionalDirector = "game.emotional-director";
        public const string AvatarEmotionalDirector = "game.avatar.emotional-director";
        public const string PrefetchBranchClone = "game.prefetch.option-branch";
        public const string SpeculativeBranchClone = "game.speculation.option-branch";

        private static readonly string[] Approved =
        {
            DateePerformance,
            AvatarReply,
            EmotionalDirector,
            AvatarEmotionalDirector,
            PrefetchBranchClone,
            SpeculativeBranchClone,
        };

        private static readonly HashSet<string> ApprovedSet =
            new HashSet<string>(Approved, StringComparer.Ordinal);

        public static IReadOnlyList<string> ApprovedCallPaths => Approved;

        public static bool IsApproved(string callPathId)
            => !string.IsNullOrWhiteSpace(callPathId) && ApprovedSet.Contains(callPathId);

        public static void ThrowIfNotApproved(string callPathId)
        {
            if (!IsApproved(callPathId))
            {
                throw new InvalidOperationException(
                    "Conversational Game Run Agent Journal call path is not in the static #1373-approved inventory: "
                    + (callPathId ?? string.Empty));
            }
        }

        internal static string NormalizeForCorrelation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == ':'))
                    chars[i] = '-';
            }

            return new string(chars).Trim('-');
        }
    }
}
