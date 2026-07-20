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

            // #1156: this formatter attributes every non-scene entry to either
            // the player avatar or the datee by comparing entry.Sender to
            // playerName. If playerName is null/whitespace the comparison can
            // NEVER match, so the player's own messages silently fall through to
            // "DATEE". That silent mislabel — masked by SessionDocumentBuilder's
            // FallbackName("" -> "Player") — is exactly what hid #1156. Fail
            // loudly instead of producing a corrupt prompt: callers (the options
            // and datee contexts) MUST pass player.DisplayName.
            bool hasAttributableEntry = false;
            foreach (var probe in history)
            {
                if (!Senders.IsScene(probe.Sender)) { hasAttributableEntry = true; break; }
            }
            if (hasAttributableEntry && string.IsNullOrWhiteSpace(playerName))
            {
                throw new InvalidOperationException(
                    "HistoryFormatter.Format: playerName is empty — cannot attribute " +
                    "conversation roles; the options/datee context must pass " +
                    "player.DisplayName (see #1156).");
            }

            sb.AppendLine("[CONVERSATION_START]");

            // #951: turn index must count only real conversational entries.
            // Scene entries (sender == "[scene]") must never appear in the
            // LLM context — GameSession.BuildHistoryForLlmContext() normally
            // filters them, but a defense-in-depth guard here prevents any
            // caller from accidentally passing an unfiltered list and having
            // the LLM confuse "[scene]" for the datee's character name.
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
            // the last-6 window so turn-0 bios never appear as [DATEE]
            // lines that confuse the LLM about the datee's character name.
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
