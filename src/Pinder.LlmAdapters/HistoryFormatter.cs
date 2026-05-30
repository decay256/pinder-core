using System;
using System.Collections.Generic;
using System.Text;
using Pinder.Core.Conversation;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Helper for formatting conversation history for LLM prompts.
    /// </summary>
    public static class HistoryFormatter
    {
        /// <summary>
        /// Formats conversation history with [T{n}|PLAYER|name] markers.
        /// Full history — never truncated.
        /// </summary>
        public static void Format(
            StringBuilder sb,
            IReadOnlyList<(string Sender, string Text)> history,
            string playerName)
        {
            if (sb == null) throw new ArgumentNullException(nameof(sb));
            if (history == null) throw new ArgumentNullException(nameof(history));

            sb.AppendLine("[CONVERSATION_START]");

            // #951: turn index must count only real conversational entries.
            // Scene entries (sender == "[scene]") must never appear in the
            // LLM context — GameSession.BuildHistoryForLlmContext() normally
            // filters them, but a defense-in-depth guard here prevents any
            // caller from accidentally passing an unfiltered list and having
            // the LLM confuse "[scene]" for the opponent's character name.
            int filteredIndex = 0;
            for (int i = 0; i < history.Count; i++)
            {
                var entry = history[i];
                if (Senders.IsScene(entry.Sender)) continue;
                int turn = (filteredIndex / 2) + 1;
                string role = entry.Sender == playerName ? "PLAYER AVATAR" : "DATEE";
                sb.AppendLine($"[T{turn}|{role}] \"{entry.Text}\"");
                filteredIndex++;
            }

            sb.AppendLine("[CURRENT_TURN]");
        }

        /// <summary>
        /// Formats recent conversation history without turn numbers for context.
        /// Filters scene entries and takes the last 6 entries.
        /// </summary>
        public static void FormatRecent(
            StringBuilder sb,
            IReadOnlyList<(string Sender, string Text)> history,
            string? playerName)
        {
            if (sb == null) throw new ArgumentNullException(nameof(sb));
            if (history == null) throw new ArgumentNullException(nameof(history));

            // #951: skip scene entries (sender == "[scene]") before computing
            // the last-6 window so turn-0 bios and outfit descriptions never
            // appear as [OPPONENT] lines that confuse the LLM about the
            // opponent's character name.
            var filtered = new List<(string Sender, string Text)>(history.Count);
            foreach (var e in history)
            {
                if (!Senders.IsScene(e.Sender))
                {
                    filtered.Add(e);
                }
            }

            int start = Math.Max(0, filtered.Count - 6);
            for (int i = start; i < filtered.Count; i++)
            {
                var entry = filtered[i];
                string role = (!string.IsNullOrEmpty(playerName) && entry.Sender == playerName) ? "PLAYER AVATAR" : "DATEE";
                sb.AppendLine($"[{role}] \"{entry.Text}\"");
            }
        }
    }
}
