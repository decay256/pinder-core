using System.Collections.Generic;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Helpers for mapping a physical <c>ConversationHistory</c> index
    /// to a logical (turn-number, role) pair while transparently skipping
    /// non-conversational <see cref="Senders.Scene"/> entries seeded at
    /// the front of the history (issue #333).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pre-#333 the conversation log was strict alternating
    /// <c>(player, opponent, player, opponent, ...)</c> pairs, so consumers
    /// could derive the turn number with <c>(i / 2) + 1</c> and the role
    /// with <c>i % 2 == 0</c>. Once the engine started seeding
    /// <c>[scene]</c> entries (player bio, opponent bio, outfit
    /// description) at indices 0..N before the first turn, that pair-math
    /// shifts by N and silently misattributes turn numbers / text-diffs.
    /// </para>
    /// <para>
    /// This class encapsulates the "skip scenes, then pair-math the
    /// remaining entries" rule so every consumer agrees on the same
    /// mapping. Two patterns are exposed:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="TurnNumberAt"/> / <see cref="IsPlayerEntryAt"/> for
    ///     point queries (random access — O(N) per call, fine when
    ///     called at most once per entry).
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="EnumerateConversation(IReadOnlyList{ValueTuple{string, string}})"/>
    ///     for streaming iteration through the whole history — emits
    ///     <c>(physicalIndex, sender, text, isScene, turnNumber, isPlayerEntry)</c>
    ///     in O(N) total. Use this in batch loops (persistence, log
    ///     rendering).
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static class ConversationIndexing
    {
        /// <summary>
        /// True when the entry at <paramref name="index"/> is a synthetic
        /// scene-setting entry rather than a real conversational turn.
        /// </summary>
        public static bool IsSceneAt(
            IReadOnlyList<(string Sender, string Text)> history, int index)
        {
            return Senders.IsScene(history[index].Sender);
        }

        /// <summary>
        /// 1-based turn number for the conversational entry at
        /// <paramref name="index"/>. Returns <c>0</c> when the entry is a
        /// <c>[scene]</c> entry (scene entries do not belong to any turn).
        /// </summary>
        /// <remarks>
        /// Turn 1 starts at the first non-scene entry. The first non-scene
        /// entry (the player's turn-1 message) and the second non-scene
        /// entry (the opponent's turn-1 reply) both return <c>1</c>; the
        /// third and fourth return <c>2</c>; and so on.
        /// </remarks>
        public static int TurnNumberAt(
            IReadOnlyList<(string Sender, string Text)> history, int index)
        {
            if (Senders.IsScene(history[index].Sender)) return 0;
            int nonSceneCount = 0;
            for (int i = 0; i <= index; i++)
            {
                if (!Senders.IsScene(history[i].Sender)) nonSceneCount++;
            }
            // nonSceneCount is 1-based count of conversational entries up
            // to and including the current one. Pairs are (player, opp)
            // so entries 1+2 → turn 1, 3+4 → turn 2, etc.
            return ((nonSceneCount - 1) / 2) + 1;
        }

        /// <summary>
        /// True when the entry at <paramref name="index"/> is the player
        /// half of a (player, opponent) turn pair. False for opponent
        /// entries and for <c>[scene]</c> entries.
        /// </summary>
        public static bool IsPlayerEntryAt(
            IReadOnlyList<(string Sender, string Text)> history, int index)
        {
            if (Senders.IsScene(history[index].Sender)) return false;
            int nonSceneCount = 0;
            for (int i = 0; i <= index; i++)
            {
                if (!Senders.IsScene(history[i].Sender)) nonSceneCount++;
            }
            // 1st, 3rd, 5th, ... non-scene entry is a player entry.
            return (nonSceneCount - 1) % 2 == 0;
        }

        /// <summary>
        /// Walks <paramref name="history"/> once and yields a tagged view
        /// of every entry — physical index, sender, text, whether it's a
        /// scene entry, the 1-based turn number it belongs to (0 for
        /// scenes), and whether it's the player half of its turn.
        /// </summary>
        /// <remarks>
        /// Single-pass O(N) — use this in batch loops where every entry
        /// needs to be classified (e.g. building a persisted history row
        /// array, rendering the public log endpoint).
        /// </remarks>
        public static IEnumerable<ConversationEntryView> EnumerateConversation(
            IReadOnlyList<(string Sender, string Text)> history)
        {
            int nonSceneCount = 0;
            for (int i = 0; i < history.Count; i++)
            {
                var (sender, text) = history[i];
                bool isScene = Senders.IsScene(sender);
                int turnNumber;
                bool isPlayerEntry;
                if (isScene)
                {
                    turnNumber = 0;
                    isPlayerEntry = false;
                }
                else
                {
                    nonSceneCount++;
                    turnNumber = ((nonSceneCount - 1) / 2) + 1;
                    isPlayerEntry = (nonSceneCount - 1) % 2 == 0;
                }
                yield return new ConversationEntryView(
                    physicalIndex: i,
                    sender: sender,
                    text: text,
                    isScene: isScene,
                    turnNumber: turnNumber,
                    isPlayerEntry: isPlayerEntry);
            }
        }
    }

    /// <summary>
    /// Tagged view of a single <c>ConversationHistory</c> entry — the
    /// physical (sender, text) plus its derived classification
    /// (<see cref="IsScene"/>, <see cref="TurnNumber"/>, <see cref="IsPlayerEntry"/>).
    /// Returned by <see cref="ConversationIndexing.EnumerateConversation(IReadOnlyList{ValueTuple{string, string}})"/>.
    /// </summary>
    /// <remarks>
    /// Implemented as a plain immutable struct (no <c>record struct</c>)
    /// because Pinder.Core targets <c>netstandard2.0</c> with
    /// <c>LangVersion=8.0</c>.
    /// </remarks>
    public readonly struct ConversationEntryView
    {
        public ConversationEntryView(
            int physicalIndex,
            string sender,
            string text,
            bool isScene,
            int turnNumber,
            bool isPlayerEntry)
        {
            PhysicalIndex = physicalIndex;
            Sender = sender;
            Text = text;
            IsScene = isScene;
            TurnNumber = turnNumber;
            IsPlayerEntry = isPlayerEntry;
        }

        public int PhysicalIndex { get; }
        public string Sender { get; }
        public string Text { get; }
        public bool IsScene { get; }
        public int TurnNumber { get; }
        public bool IsPlayerEntry { get; }
    }
}
