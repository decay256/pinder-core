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
    /// Regression tests for issues
    /// <c>decay256/pinder-core#769</c> (skipped player turn desyncs
    /// <c>turnIdx = i / 2</c>) and <c>decay256/pinder-core#774</c>
    /// (scene-prefixed conversation history desyncs the same pair-math).
    ///
    /// <para>
    /// Both tickets target the same pair-math hazard in
    /// <c>session-runner/Program.cs</c> <c>BuildTurnSnapshot</c>:
    /// <code>
    /// bool isPlayerEntry = (i % 2) == 0;
    /// int turnIdx = i / 2;
    /// </code>
    /// which assumes the <c>ConversationHistory</c> list is a strict
    /// alternating <c>(player, opponent, player, opponent, ...)</c>
    /// sequence. That assumption breaks for two distinct triggers:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <strong>#769:</strong> a player turn with empty
    ///     <c>DeliveredMessage</c> means no entry on the player axis at
    ///     all. Both lists (<c>conversationHistory</c> and
    ///     <c>perTurnTextDiffs</c>) stay in lock-step on the player axis
    ///     after #767's brace fix, but a downstream <c>turn_number</c>
    ///     read derived from <c>i / 2</c> in <c>BuildTurnSnapshot</c>
    ///     would still mis-attribute diffs to the wrong turn.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>#774:</strong> issue #333 seeded
    ///     <c>[scene]</c> entries at the front of
    ///     <c>conversationHistory</c>. Pair-math then identifies the
    ///     wrong indices as "player," and turn-1 diffs end up attached
    ///     to scene entries.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// The consolidated fix walks the conversation log via the canonical
    /// <see cref="ConversationIndexing.EnumerateConversation"/> helper
    /// (shipped on PR #773 / commit <c>3fda688</c>) so scene entries
    /// are transparently skipped, AND identifies player entries by
    /// sender equality with the supplied <c>playerSender</c> argument
    /// (live-engine path always passes it) so a desync on either side
    /// of a (player, opponent) pair never misattributes a diff.
    /// </para>
    /// </summary>
    [Trait("Category", "SessionRunner")]
    public class Issue769And774_BuildTurnSnapshotIndexingTests
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

        private static TurnSnapshot Build(
            int turnNumber,
            List<(string Sender, string Text)> conversationHistory,
            List<List<TextDiffSnapshot>> perTurnTextDiffs,
            string? playerSender = "Sable")
        {
            return global::Program.BuildTurnSnapshot(
                turnNumber: turnNumber,
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
                perTurnTextDiffs: perTurnTextDiffs,
                opponentHistory: null,
                playerSender: playerSender);
        }

        // ── #774 trigger: scene-prefixed conversation history ──────────────

        [Fact]
        public void Snapshot_AttachesDiffsToCorrectPlayerEntry_WhenSceneEntriesPrefixHistory()
        {
            // Mirrors the post-#333 reality: three [scene] entries at
            // the front (player bio, opponent bio, outfit description),
            // then real turns. The buggy pair-math (i % 2 == 0) would
            // identify indices 0, 2, 4 as "player" — but indices 0 and
            // 2 are scenes, and index 4 is the OPPONENT's turn-1 entry.
            // So turn-1's diffs would attach to scene entries, never
            // reaching the real player turn-1 entry at index 3.
            var conversationHistory = new List<(string Sender, string Text)>
            {
                (Senders.Scene, "player bio"),                       // 0
                (Senders.Scene, "opponent bio"),                     // 1
                (Senders.Scene, "outfit description"),               // 2
                ("Sable", "p1 turn 1"),                              // 3
                ("Brick", "p2 turn 1"),                              // 4
                ("Sable", "p1 turn 2"),                              // 5
                ("Brick", "p2 turn 2"),                              // 6
            };
            var perTurnTextDiffs = new List<List<TextDiffSnapshot>>
            {
                Diffs("turn1"), Diffs("turn2"),
            };

            var snap = Build(turnNumber: 2,
                conversationHistory: conversationHistory,
                perTurnTextDiffs: perTurnTextDiffs);

            // All seven entries survive into the wire shape, in the
            // same order, with their senders preserved.
            Assert.Equal(7, snap.ConversationHistory.Count);
            Assert.Equal(Senders.Scene, snap.ConversationHistory[0].Sender);
            Assert.Equal(Senders.Scene, snap.ConversationHistory[1].Sender);
            Assert.Equal(Senders.Scene, snap.ConversationHistory[2].Sender);
            Assert.Equal("Sable", snap.ConversationHistory[3].Sender);
            Assert.Equal("Brick", snap.ConversationHistory[4].Sender);

            // Scene entries carry NO TextDiffs (this is exactly the
            // misattribution the buggy pair-math would cause: turn-1's
            // Diffs would have ended up on the scene at index 0).
            Assert.Empty(snap.ConversationHistory[0].TextDiffs);
            Assert.Empty(snap.ConversationHistory[1].TextDiffs);
            Assert.Empty(snap.ConversationHistory[2].TextDiffs);

            // Turn-1's diffs land on the actual player turn-1 entry
            // (index 3), turn-2's diffs on the player turn-2 entry
            // (index 5). Opponent entries (indices 4, 6) carry no
            // diffs.
            Assert.Equal("turn1", Assert.Single(snap.ConversationHistory[3].TextDiffs).Layer);
            Assert.Empty(snap.ConversationHistory[4].TextDiffs);
            Assert.Equal("turn2", Assert.Single(snap.ConversationHistory[5].TextDiffs).Layer);
            Assert.Empty(snap.ConversationHistory[6].TextDiffs);
        }

        [Fact]
        public void Snapshot_HandlesAnyNumberOfSceneEntries_NotJustThree()
        {
            // Defensive coverage: ConversationIndexing skips scenes
            // regardless of count. A future change to SeedSceneEntries
            // (more or fewer scenes) must not require a snapshot fix.
            // Here we use 0, 1, 4, and 5 leading scenes to pin that the
            // helper is the indexing source-of-truth.
            foreach (int sceneCount in new[] { 0, 1, 4, 5 })
            {
                var conversationHistory = new List<(string Sender, string Text)>();
                for (int s = 0; s < sceneCount; s++)
                    conversationHistory.Add((Senders.Scene, $"scene {s}"));
                conversationHistory.Add(("Sable", "p1 turn 1"));
                conversationHistory.Add(("Brick", "p2 turn 1"));
                var perTurnTextDiffs = new List<List<TextDiffSnapshot>>
                {
                    Diffs("turn1"),
                };

                var snap = Build(turnNumber: 1,
                    conversationHistory: conversationHistory,
                    perTurnTextDiffs: perTurnTextDiffs);

                // Every scene index has empty diffs.
                for (int i = 0; i < sceneCount; i++)
                    Assert.Empty(snap.ConversationHistory[i].TextDiffs);

                // Player turn-1 entry (right after the scenes) carries
                // turn-1's diffs.
                Assert.Equal("turn1",
                    Assert.Single(snap.ConversationHistory[sceneCount].TextDiffs).Layer);

                // Opponent turn-1 entry (the very last) carries none.
                Assert.Empty(snap.ConversationHistory[sceneCount + 1].TextDiffs);
            }
        }

        // ── #769 trigger: skipped player turn ───────────────────────────────

        [Fact]
        public void Snapshot_SurvivesSkippedPlayerTurn_WithoutMisattributingDiffs()
        {
            // Reproduces the #769 trigger on top of the #767 brace fix.
            // Three turns total. Turn 2's DeliveredMessage was empty,
            // so turn 2 produced NO player entry on the conversation
            // log AND NO entry on perTurnTextDiffs. The opponent reply
            // for turn 2 still landed on the log.
            //
            // Resulting conversationHistory has 5 entries in order:
            //   [Sable p1 turn 1, Brick p2 turn 1,
            //    Brick p2 turn 2,
            //    Sable p1 turn 3, Brick p2 turn 3]
            //
            // perTurnTextDiffs has 2 entries: [turn1, turn3].
            //
            // The pair-math heuristic (turnIdx = i / 2) would attach:
            //   index 0 → diffs[0] = turn1   ✓ accidentally right
            //   index 2 → diffs[1] = turn3   ✗ but index 2 is P2 turn 2 (opponent!)
            //   index 4 → OOB                ✗
            // So the buggy code would attribute turn3's diffs to the
            // OPPONENT entry of turn 2, and the player's turn-3 entry
            // would carry no diffs at all.
            var conversationHistory = new List<(string Sender, string Text)>
            {
                ("Sable", "p1 turn 1"),
                ("Brick", "p2 turn 1"),
                // Player turn-2 entry intentionally absent (#769 case).
                ("Brick", "p2 turn 2"),
                ("Sable", "p1 turn 3"),
                ("Brick", "p2 turn 3"),
            };
            var perTurnTextDiffs = new List<List<TextDiffSnapshot>>
            {
                Diffs("turn1"), Diffs("turn3"),
            };

            var snap = Build(turnNumber: 3,
                conversationHistory: conversationHistory,
                perTurnTextDiffs: perTurnTextDiffs);

            Assert.Equal(5, snap.ConversationHistory.Count);

            // Under the consolidated fix, when playerSender is supplied
            // BuildTurnSnapshot identifies player entries by sender
            // equality, not by the helper's pair-math classification.
            // So:
            //   index 0 (Sable, p1 turn 1) → 1st player entry → diffs[0] = turn1
            //   index 1 (Brick, p2 turn 1) → not player → no diffs
            //   index 2 (Brick, p2 turn 2) → not player → no diffs
            //   index 3 (Sable, p1 turn 3) → 2nd player entry → diffs[1] = turn3
            //   index 4 (Brick, p2 turn 3) → not player → no diffs
            Assert.Equal("turn1", Assert.Single(snap.ConversationHistory[0].TextDiffs).Layer);
            Assert.Empty(snap.ConversationHistory[1].TextDiffs); // P2 turn 1
            Assert.Empty(snap.ConversationHistory[2].TextDiffs); // P2 turn 2 (sender-mismatch veto)
            Assert.Equal("turn3", Assert.Single(snap.ConversationHistory[3].TextDiffs).Layer);
            Assert.Empty(snap.ConversationHistory[4].TextDiffs); // P2 turn 3
        }

        [Fact]
        public void Snapshot_ResimResumeFiltersByPlayerSender_NotByEvenIndex()
        {
            // Mirrors the resim-restore lift in Program.Main (line
            // ~762): when resuming from a snapshot, perTurnTextDiffs is
            // built from the persisted ConversationHistory by selecting
            // the player entries' TextDiffs. The pre-fix code used
            // `idx % 2 == 0`, which captures scene indices instead of
            // real player entries when the snapshot has scene seeds.
            //
            // We can't call Program.Main directly here, but we can pin
            // the equivalent LINQ: filtering by sender == player1
            // produces the correct per-turn list, and is independent
            // of how many scenes prefix the snapshot.
            string player1 = "Sable";
            var snapshotConversationHistory = new List<ConversationEntry>
            {
                new ConversationEntry { Sender = Senders.Scene, Text = "player bio" },
                new ConversationEntry { Sender = Senders.Scene, Text = "opponent bio" },
                new ConversationEntry { Sender = Senders.Scene, Text = "outfit" },
                new ConversationEntry { Sender = "Sable", Text = "p1 turn 1",
                    TextDiffs = Diffs("turn1") },
                new ConversationEntry { Sender = "Brick", Text = "p2 turn 1" },
                new ConversationEntry { Sender = "Sable", Text = "p1 turn 2",
                    TextDiffs = Diffs("turn2") },
                new ConversationEntry { Sender = "Brick", Text = "p2 turn 2" },
            };

            // The fixed lift filters by sender, not by physical index.
            var lifted = snapshotConversationHistory
                .Where(e => e.Sender == player1)
                .Select(e => e.TextDiffs ?? new List<TextDiffSnapshot>())
                .ToList();

            Assert.Equal(2, lifted.Count);
            Assert.Equal("turn1", Assert.Single(lifted[0]).Layer);
            Assert.Equal("turn2", Assert.Single(lifted[1]).Layer);

            // Buggy form (kept here as a counter-example to make the
            // test pin the contract clearly): `idx % 2 == 0` lifts
            // indices 0, 2, 4, 6 from the snapshot — three of which
            // are scenes (empty TextDiffs) and one of which is an
            // opponent entry. The resulting list has 4 entries, two
            // empty, instead of the correct 2 entries with both diffs.
            var buggy = snapshotConversationHistory
                .Where((_, idx) => idx % 2 == 0)
                .Select(e => e.TextDiffs ?? new List<TextDiffSnapshot>())
                .ToList();
            Assert.Equal(4, buggy.Count);
            Assert.NotEqual(lifted.Count, buggy.Count);
        }

        // ── Combined: scene-prefix AND skipped player turn ─────────────────

        [Fact]
        public void Snapshot_HandlesScenePrefix_AndSkippedPlayerTurn_Together()
        {
            // Worst-of-both-worlds: scenes seeded (#333), AND a player
            // turn was skipped mid-game (#769). Pair-math would
            // misattribute on both axes simultaneously. The fix has to
            // hold under both perturbations.
            var conversationHistory = new List<(string Sender, string Text)>
            {
                (Senders.Scene, "player bio"),
                (Senders.Scene, "opponent bio"),
                (Senders.Scene, "outfit description"),
                ("Sable", "p1 turn 1"),
                ("Brick", "p2 turn 1"),
                // Player turn-2 entry skipped (#769 case).
                ("Brick", "p2 turn 2"),
                ("Sable", "p1 turn 3"),
                ("Brick", "p2 turn 3"),
            };
            var perTurnTextDiffs = new List<List<TextDiffSnapshot>>
            {
                Diffs("turn1"), Diffs("turn3"),
            };

            var snap = Build(turnNumber: 3,
                conversationHistory: conversationHistory,
                perTurnTextDiffs: perTurnTextDiffs);

            Assert.Equal(8, snap.ConversationHistory.Count);

            // Scenes: never carry diffs.
            for (int i = 0; i < 3; i++)
                Assert.Empty(snap.ConversationHistory[i].TextDiffs);

            // Opponent entries: never carry diffs, regardless of
            // surrounding desync.
            Assert.Empty(snap.ConversationHistory[4].TextDiffs); // P2 turn 1
            Assert.Empty(snap.ConversationHistory[5].TextDiffs); // P2 turn 2 (no preceding P1 entry)
            Assert.Empty(snap.ConversationHistory[7].TextDiffs); // P2 turn 3

            // Player entries that DID land carry their diffs.
            // turn-1 diffs land on the actual Sable turn-1 entry (index 3).
            Assert.Equal("turn1",
                Assert.Single(snap.ConversationHistory[3].TextDiffs).Layer);
            // turn-3 diffs land on the actual Sable turn-3 entry (index 6).
            // The pair-math+skipped-turn perturbation would have tried
            // to attach turn-3's diffs to the Brick p2 turn-2 opponent
            // entry at index 5, but the playerSender guard catches
            // that misattribution — only entries whose sender matches
            // playerSender ("Sable") are eligible.
            Assert.Equal("turn3",
                Assert.Single(snap.ConversationHistory[6].TextDiffs).Layer);
        }
    }
}
