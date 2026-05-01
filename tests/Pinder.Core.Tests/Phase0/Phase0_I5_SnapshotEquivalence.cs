using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I5 — a session played turn-by-turn to turn N produces the same
    /// post-state as a session played to turn M (M &lt; N), snapshot-restored to
    /// turn M's state, and continued to turn N. Snapshot/restore is a pure
    /// equivalence on the externally observable game state.
    ///
    /// <para>
    /// The "externally observable state" being locked here is the public surface of
    /// <see cref="GameStateSnapshot"/>: interest, momentum streak, active traps,
    /// turn number, pending triple bonus. Internal/private fields are out of scope
    /// (Phase 1's #788 will refactor opponent-conversation state out of the adapter,
    /// changing internal layout but not <c>GameStateSnapshot</c>).
    /// </para>
    ///
    /// <para>
    /// LLM transport responses and dice draws are made deterministic so the only
    /// thing under test is the snapshot/restore equivalence. The replay path
    /// uses <see cref="GameSession.RestoreState(ResimulateData, Pinder.Core.Interfaces.ITrapRegistry)"/>
    /// — the same path the production session-runner uses for replay.
    /// </para>
    ///
    /// <para>
    /// If this test fails, do NOT mutate it to chase the symptom; surface the
    /// drift to the orchestrator. Phase 5 fast-gameplay scheduling assumes
    /// snapshot equivalence as a hard prerequisite (the adopted-branch byte-equality
    /// test in #393's epic). A regression here blocks the campaign.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I5_SnapshotEquivalence
    {
        // I5.1 — three turns straight vs (snapshot at turn 1) → restore → continue 2 more.
        // The replay session is wired with a transport queue that starts at
        // the POST-MID-SNAPSHOT position so its dequeue sequence matches what
        // session A drew on turns 2-3. This is what production replay actually does
        // (re-uses the audit log's stored responses for the post-snapshot tail).
        [Fact]
        public async Task ThreeTurns_SnapshotAtTurn1_RestoredContinuation_MatchesStraightLine()
        {
            // ── Run A: three full turns straight ──
            var sessionA = MakeFreshSession(turnsToQueue: 3);
            await PlayThreeTurns(sessionA);
            var finalA = sessionA.CreateSnapshot();
            var historyA = sessionA.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();

            // ── Run B1: one turn (the 'pre-snapshot' run) ──
            var sessionB1 = MakeFreshSession(turnsToQueue: 1);
            await PlayOneTurn(sessionB1);
            var midSnap = sessionB1.CreateSnapshot();
            var midHistory = sessionB1.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();

            // ── Run B2: replay session, transport starts at post-snapshot position ──
            //   - canned responses queued for turns T1, T2 only (matches A's tail).
            //   - dice queue drained for the constructor's d10, then turns T1/T2's draws.
            var transportB2 = new RecordingLlmTransport { DefaultResponse = "" };
            for (int t = 1; t <= 2; t++) // matches PlayThreeTurns's tail (T=1..2)
            {
                transportB2.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transportB2.QueueDelivery($"delivered-msg-T{t}");
                transportB2.QueueOpponent($"opponent-reply-T{t}");
            }
            var adapterB2 = Phase0Fixtures.MakeAdapter(transportB2);
            var diceB2 = new PlaybackDiceRoller(
                5,           // ctor d10
                15, 50,      // turn 2
                15, 50);     // turn 3
            var sessionB2 = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapterB2, diceB2, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());

            sessionB2.RestoreState(BuildResimData(midSnap, midHistory), new NullTrapRegistry());
            await PlayTwoTurns(sessionB2);
            var finalB = sessionB2.CreateSnapshot();
            var historyB = sessionB2.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList();

            // ── Equivalence on the public state surface ──
            Assert.Equal(finalA.Interest, finalB.Interest);
            Assert.Equal(finalA.MomentumStreak, finalB.MomentumStreak);
            Assert.Equal(finalA.TurnNumber, finalB.TurnNumber);
            Assert.Equal(finalA.TripleBonusActive, finalB.TripleBonusActive);
            Assert.Equal(finalA.ActiveTrapNames.Length, finalB.ActiveTrapNames.Length);

            // Conversation history equivalence: same number of entries, same texts
            // in the same order. (Sender labels are deterministic given the profile.)
            Assert.Equal(historyA.Count, historyB.Count);
            for (int i = 0; i < historyA.Count; i++)
            {
                Assert.Equal(historyA[i].Sender, historyB[i].Sender);
                Assert.Equal(historyA[i].Text, historyB[i].Text);
            }
        }

        // I5.2 — restore at turn 0 (mid-snapshot just after construction): replay
        // of the entire session against the restored start point matches the
        // straight-line baseline.
        [Fact]
        public async Task TwoTurns_SnapshotAtTurn0_RestoredFullReplay_MatchesStraightLine()
        {
            var sessionA = MakeFreshSession(turnsToQueue: 2);
            await PlayTwoTurns(sessionA);
            var finalA = sessionA.CreateSnapshot();

            // Build a "turn 0" ResimulateData — empty history, default interest.
            var sessionB = MakeFreshSession(turnsToQueue: 2);
            var resim0 = new ResimulateData
            {
                TargetInterest = sessionB.CreateSnapshot().Interest,
                TurnNumber = 0,
                MomentumStreak = 0,
                ShadowValues = new Dictionary<string, int>(),
                ActiveTraps = new List<(string, int)>(),
                ConversationHistory = new List<(string, string)>(),
                ComboHistory = new List<(string, bool)>(),
                PendingTripleBonus = false,
                RizzCumulativeFailureCount = 0,
            };
            sessionB.RestoreState(resim0, new NullTrapRegistry());
            await PlayTwoTurns(sessionB);
            var finalB = sessionB.CreateSnapshot();

            Assert.Equal(finalA.Interest, finalB.Interest);
            Assert.Equal(finalA.MomentumStreak, finalB.MomentumStreak);
            Assert.Equal(finalA.TurnNumber, finalB.TurnNumber);
        }

        // ── helpers ──────────────────────────────────────────────────────

        private static GameSession MakeFreshSession(int turnsToQueue)
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            for (int t = 0; t < turnsToQueue; t++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery($"delivered-msg-T{t}");
                transport.QueueOpponent($"opponent-reply-T{t}");
            }

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var diceValues = new List<int> { 5 }; // ctor d10
            for (int t = 0; t < turnsToQueue; t++)
            {
                diceValues.Add(15); // d20 main
                diceValues.Add(50); // d100 timing
            }
            var dice = new PlaybackDiceRoller(diceValues.ToArray());

            return new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
        }

        private static async Task PlayOneTurn(GameSession s)
        {
            await s.StartTurnAsync();
            await s.ResolveTurnAsync(0);
        }

        private static async Task PlayTwoTurns(GameSession s)
        {
            for (int i = 0; i < 2; i++)
            {
                await s.StartTurnAsync();
                await s.ResolveTurnAsync(0);
            }
        }

        private static async Task PlayThreeTurns(GameSession s)
        {
            for (int i = 0; i < 3; i++)
            {
                await s.StartTurnAsync();
                await s.ResolveTurnAsync(0);
            }
        }

        private static ResimulateData BuildResimData(
            GameStateSnapshot snap,
            List<(string Sender, string Text)> history)
        {
            return new ResimulateData
            {
                TargetInterest = snap.Interest,
                TurnNumber = snap.TurnNumber,
                MomentumStreak = snap.MomentumStreak,
                ShadowValues = new Dictionary<string, int>(),
                ActiveTraps = (snap.ActiveTrapDetails ?? Array.Empty<TrapDetail>())
                    .Select(t => (t.Stat, t.TurnsRemaining))
                    .ToList(),
                ConversationHistory = history,
                ComboHistory = new List<(string, bool)>(),
                PendingTripleBonus = snap.TripleBonusActive,
                RizzCumulativeFailureCount = 0,
            };
        }
    }
}
