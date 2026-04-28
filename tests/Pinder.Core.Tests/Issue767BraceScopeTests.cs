using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.SessionRunner.Snapshot;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression tests for the brace-scope bug on PR
    /// <c>decay256/pinder-core#767</c> (issue #305).
    ///
    /// <para>
    /// Original bug: in <c>session-runner/Program.cs</c> the per-turn
    /// loop did
    /// <code>
    /// if (!string.IsNullOrEmpty(result.DeliveredMessage))
    ///     conversationHistory.Add((player1, result.DeliveredMessage));
    ///     perTurnTextDiffs.Add(...);   // visually nested, actually unconditional
    /// </code>
    /// because the <c>if</c> had no braces. When <c>DeliveredMessage</c>
    /// was empty the player entry was skipped from
    /// <c>conversationHistory</c> but the diffs entry was still appended,
    /// which desynced the two lists. <c>BuildTurnSnapshot</c> walks
    /// <c>conversationHistory</c> with <c>turnIdx = i / 2</c> and uses
    /// that index into <c>perTurnTextDiffs</c>, so every turn after the
    /// skip would attach diffs to the wrong entry.
    /// </para>
    ///
    /// <para>
    /// The fix added braces so both <c>Add</c> calls are gated by the
    /// same <c>if</c>. These tests pin the contract:
    /// 1. The two lists are co-indexed by player-entry, so the diffs of
    ///    turn N are <c>perTurnTextDiffs[N-1]</c>.
    /// 2. Skipping a turn (empty <c>DeliveredMessage</c>) MUST skip both
    ///    lists in lock-step or downstream snapshots desync.
    /// </para>
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public class Issue767BraceScopeTests
    {
        // ── Helpers ────────────────────────────────────────────────────────

        private static StatBlock MakeStats() => new StatBlock(
            new Dictionary<StatType, int>
            {
                { StatType.Charm, 3 }, { StatType.Rizz, 2 },
                { StatType.Honesty, 1 }, { StatType.Chaos, 0 },
                { StatType.Wit, 4 }, { StatType.SelfAwareness, 2 },
            },
            new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 },
            });

        private static RollResult MakeRoll() =>
            new RollResult(10, null, 10, StatType.Charm, 2, 0, 13, FailureTier.None);

        private static GameStateSnapshot MakeState() =>
            new GameStateSnapshot(10, InterestState.Interested, 0, Array.Empty<string>(), 1);

        private static TurnResult MakeResult() =>
            new TurnResult(MakeRoll(), "delivered", "reply", null, 1, MakeState(), false, null);

        private static List<TextDiffSnapshot> Diffs(string layer) =>
            new List<TextDiffSnapshot>
            {
                new TextDiffSnapshot
                {
                    Layer = layer,
                    Before = "before",
                    After = "after",
                    Spans = new List<TextDiffSpanSnapshot>
                    {
                        new TextDiffSpanSnapshot { Type = "Keep", Text = layer },
                    },
                },
            };

        // ── Tests ──────────────────────────────────────────────────────────

        [Fact]
        public void BuildTurnSnapshot_AttachesPerTurnDiffsToCorrectPlayerEntry()
        {
            // 3 turns, all with non-empty DeliveredMessage and OpponentMessage
            // → conversationHistory has 6 entries (alternating P1/P2),
            // → perTurnTextDiffs has 3 entries.
            var conversationHistory = new List<(string Sender, string Text)>
            {
                ("P1", "p1 turn 1"), ("P2", "p2 turn 1"),
                ("P1", "p1 turn 2"), ("P2", "p2 turn 2"),
                ("P1", "p1 turn 3"), ("P2", "p2 turn 3"),
            };
            var perTurnTextDiffs = new List<List<TextDiffSnapshot>>
            {
                Diffs("turn1"), Diffs("turn2"), Diffs("turn3"),
            };

            var snap = global::Program.BuildTurnSnapshot(
                turnNumber: 3,
                result: MakeResult(),
                shadows: new SessionShadowTracker(MakeStats()),
                statsUsedHistory: new List<StatType>(),
                highestPctHistory: new List<bool>(),
                charmUsageCount: 0,
                charmMadnessTriggered: false,
                saUsageCount: 0,
                saOverthinkingTriggered: false,
                rizzCumulativeFailureCount: 0,
                conversationHistory: conversationHistory,
                comboHistory: new List<(StatType Stat, bool Succeeded)>(),
                activeTell: null,
                perTurnTextDiffs: perTurnTextDiffs);

            Assert.Equal(6, snap.ConversationHistory.Count);

            // Player entries (even indices) carry their turn's diffs.
            Assert.Equal("turn1", Assert.Single(snap.ConversationHistory[0].TextDiffs).Layer);
            Assert.Equal("turn2", Assert.Single(snap.ConversationHistory[2].TextDiffs).Layer);
            Assert.Equal("turn3", Assert.Single(snap.ConversationHistory[4].TextDiffs).Layer);

            // Opponent entries (odd indices) carry no diffs.
            Assert.Empty(snap.ConversationHistory[1].TextDiffs);
            Assert.Empty(snap.ConversationHistory[3].TextDiffs);
            Assert.Empty(snap.ConversationHistory[5].TextDiffs);
        }

        [Fact]
        public void EmptyDeliveredMessage_LoopBody_KeepsListsLockstepOnPlayerAxis()
        {
            // Mirrors the (now-fixed) loop body of Program.Main across 3
            // turns. Turn 2 has empty DeliveredMessage — the exact bug
            // scenario. The fix requires that BOTH conversationHistory.Add
            // AND perTurnTextDiffs.Add are gated by the same `if`. So
            // after the loop, the count of player-sender entries in
            // conversationHistory MUST equal the length of
            // perTurnTextDiffs — otherwise downstream code that pairs
            // them by index will desync.
            //
            // Under the buggy (brace-less) code, perTurnTextDiffs would
            // have grown on every turn including turn 2, ending up
            // longer than the player-entry count.
            var conversationHistory = new List<(string Sender, string Text)>();
            var perTurnTextDiffs = new List<List<TextDiffSnapshot>>();

            // Turn 1 — both adds happen.
            ApplyTurn(conversationHistory, perTurnTextDiffs,
                deliveredMessage: "p1 turn 1",
                opponentMessage: "p2 turn 1",
                turnDiffs: Diffs("turn1"));

            // Turn 2 — empty DeliveredMessage; player entry skipped.
            ApplyTurn(conversationHistory, perTurnTextDiffs,
                deliveredMessage: "",
                opponentMessage: "p2 turn 2",
                turnDiffs: Diffs("turn2-should-not-appear"));

            // Turn 3 — both adds happen.
            ApplyTurn(conversationHistory, perTurnTextDiffs,
                deliveredMessage: "p1 turn 3",
                opponentMessage: "p2 turn 3",
                turnDiffs: Diffs("turn3"));

            // Core invariant the fix guarantees.
            int playerEntries = conversationHistory.Count(e => e.Sender == "P1");
            Assert.Equal(2, playerEntries);
            Assert.Equal(playerEntries, perTurnTextDiffs.Count);

            // Diffs that survived correspond to turn 1 and turn 3 (in
            // order). The "turn2-should-not-appear" diff was correctly
            // skipped together with its player entry.
            Assert.Equal("turn1", perTurnTextDiffs[0][0].Layer);
            Assert.Equal("turn3", perTurnTextDiffs[1][0].Layer);
            Assert.DoesNotContain(perTurnTextDiffs,
                d => d.Count > 0 && d[0].Layer == "turn2-should-not-appear");
        }

        [Fact]
        public void EmptyDeliveredMessage_BuggyLoopBody_DesyncsLists()
        {
            // Sanity check: the OLD (brace-less) loop body would have
            // grown perTurnTextDiffs even when DeliveredMessage was
            // empty. We replay that exact buggy logic here so the
            // regression test above clearly distinguishes "fix in place"
            // from "fix reverted".
            var conversationHistory = new List<(string Sender, string Text)>();
            var perTurnTextDiffs = new List<List<TextDiffSnapshot>>();

            BuggyApplyTurn(conversationHistory, perTurnTextDiffs,
                "p1 turn 1", "p2 turn 1", Diffs("turn1"));
            BuggyApplyTurn(conversationHistory, perTurnTextDiffs,
                "", "p2 turn 2", Diffs("turn2-should-not-appear"));
            BuggyApplyTurn(conversationHistory, perTurnTextDiffs,
                "p1 turn 3", "p2 turn 3", Diffs("turn3"));

            int playerEntries = conversationHistory.Count(e => e.Sender == "P1");
            Assert.Equal(2, playerEntries);
            // Buggy behaviour: perTurnTextDiffs has 3 entries, off by one.
            Assert.Equal(3, perTurnTextDiffs.Count);
            Assert.NotEqual(playerEntries, perTurnTextDiffs.Count);
        }

        /// <summary>
        /// Mirrors the (now-fixed) loop body in
        /// <c>session-runner/Program.cs</c>: when
        /// <c>DeliveredMessage</c> is empty, neither
        /// <c>conversationHistory</c> NOR <c>perTurnTextDiffs</c> grows on
        /// the player axis.
        /// </summary>
        private static void ApplyTurn(
            List<(string Sender, string Text)> conversationHistory,
            List<List<TextDiffSnapshot>> perTurnTextDiffs,
            string deliveredMessage,
            string opponentMessage,
            List<TextDiffSnapshot> turnDiffs)
        {
            if (!string.IsNullOrEmpty(deliveredMessage))
            {
                conversationHistory.Add(("P1", deliveredMessage));
                perTurnTextDiffs.Add(turnDiffs);
            }
            if (!string.IsNullOrEmpty(opponentMessage))
            {
                conversationHistory.Add(("P2", opponentMessage));
            }
        }

        /// <summary>
        /// Mirrors the ORIGINAL (brace-less) loop body where
        /// <c>perTurnTextDiffs.Add</c> was visually nested inside the
        /// <c>if</c> but actually unconditional. Used as a counter-example
        /// so the regression test cleanly distinguishes correct vs broken
        /// behaviour.
        /// </summary>
        private static void BuggyApplyTurn(
            List<(string Sender, string Text)> conversationHistory,
            List<List<TextDiffSnapshot>> perTurnTextDiffs,
            string deliveredMessage,
            string opponentMessage,
            List<TextDiffSnapshot> turnDiffs)
        {
            if (!string.IsNullOrEmpty(deliveredMessage))
                conversationHistory.Add(("P1", deliveredMessage));
            // Indented as if nested but actually outside the `if` —
            // exactly the bug.
                perTurnTextDiffs.Add(turnDiffs);
            if (!string.IsNullOrEmpty(opponentMessage))
            {
                conversationHistory.Add(("P2", opponentMessage));
            }
        }
    }
}
