using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #788: snapshot/restore round-trip for the engine-owned opponent
    /// LLM history. Locks that <see cref="GameSession.OpponentHistory"/>
    /// survives a <see cref="GameSession.RestoreState"/> call so a replayed
    /// session can reproduce the same multi-turn opponent context the
    /// original session ran with.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue788_SnapshotRoundTripTests
    {
        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public async Task PlayingTurns_AccumulatesOpponentHistory()
        {
            // Provide enough dice values for a few turns: ctor d10 + per-turn d20 main + d100 timing.
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5, 15, 50, 15, 50, 15, 50),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Engine starts with empty opponent history.
            Assert.Empty(session.OpponentHistory);

            // Resolve one turn — NullLlmAdapter contributes one user + one assistant entry.
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.Equal(2, session.OpponentHistory.Count);
            Assert.Equal(ConversationMessage.UserRole, session.OpponentHistory[0].Role);
            Assert.Equal(ConversationMessage.AssistantRole, session.OpponentHistory[1].Role);
        }

        [Fact]
        public void RestoreState_RebuildsOpponentHistoryFromResimData()
        {
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var resim = new ResimulateData
            {
                TargetInterest = session.CreateSnapshot().Interest,
                TurnNumber = 2,
                MomentumStreak = 0,
                ShadowValues = new Dictionary<string, int>(),
                ActiveTraps = new List<(string, int)>(),
                ConversationHistory = new List<(string, string)>(),
                ComboHistory = new List<(string, bool)>(),
                PendingTripleBonus = false,
                RizzCumulativeFailureCount = 0,
                OpponentHistory = new List<(string, string)>
                {
                    ("user", "first user prompt"),
                    ("assistant", "first opponent reply"),
                    ("user", "second user prompt"),
                    ("assistant", "second opponent reply"),
                },
            };
            session.RestoreState(resim, new NullTrapRegistry());

            Assert.Equal(4, session.OpponentHistory.Count);
            Assert.Equal("user", session.OpponentHistory[0].Role);
            Assert.Equal("first user prompt", session.OpponentHistory[0].Content);
            Assert.Equal("assistant", session.OpponentHistory[1].Role);
            Assert.Equal("first opponent reply", session.OpponentHistory[1].Content);
            Assert.Equal("user", session.OpponentHistory[2].Role);
            Assert.Equal("assistant", session.OpponentHistory[3].Role);
            Assert.Equal("second opponent reply", session.OpponentHistory[3].Content);

            // CreateSnapshot reflects the restored history.
            var snap = session.CreateSnapshot();
            Assert.Equal(4, snap.OpponentHistory.Count);
            Assert.Equal("first opponent reply", snap.OpponentHistory[1].Content);
        }

        [Fact]
        public void RestoreState_WithEmptyOpponentHistory_ClearsAndStaysEmpty()
        {
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Pre-load with garbage so we can prove RestoreState clears it.
            session.RestoreState(new ResimulateData
            {
                TargetInterest = session.CreateSnapshot().Interest,
                TurnNumber = 1,
                OpponentHistory = new List<(string, string)>
                {
                    ("user", "stale"),
                    ("assistant", "stale"),
                },
            }, new NullTrapRegistry());
            Assert.Equal(2, session.OpponentHistory.Count);

            // Now restore with empty opponent history — the list should clear.
            session.RestoreState(new ResimulateData
            {
                TargetInterest = session.CreateSnapshot().Interest,
                TurnNumber = 0,
                OpponentHistory = new List<(string, string)>(),
            }, new NullTrapRegistry());
            Assert.Empty(session.OpponentHistory);
        }

        [Fact]
        public async Task PlayedSession_SnapshotedAndReplayed_ReproducesOpponentHistory()
        {
            // Run A: play 2 turns straight, capture snapshot.
            var sessionA = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5, 15, 50, 15, 50),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
            await sessionA.StartTurnAsync(); await sessionA.ResolveTurnAsync(0);
            await sessionA.StartTurnAsync(); await sessionA.ResolveTurnAsync(0);
            var snapA = sessionA.CreateSnapshot();
            var historyA = sessionA.OpponentHistory.ToArray();

            Assert.Equal(4, historyA.Length); // 2 turns × (user + assistant)

            // Run B: fresh session, restore from A's history+state, verify equivalence.
            var sessionB = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
            sessionB.RestoreState(new ResimulateData
            {
                TargetInterest = snapA.Interest,
                TurnNumber = snapA.TurnNumber,
                MomentumStreak = snapA.MomentumStreak,
                ShadowValues = new Dictionary<string, int>(),
                ActiveTraps = new List<(string, int)>(),
                ConversationHistory = sessionA.ConversationHistory
                    .Select(e => (e.Sender, e.Text)).ToList(),
                ComboHistory = new List<(string, bool)>(),
                PendingTripleBonus = snapA.TripleBonusActive,
                OpponentHistory = historyA.Select(m => (m.Role, m.Content)).ToList(),
            }, new NullTrapRegistry());

            Assert.Equal(historyA.Length, sessionB.OpponentHistory.Count);
            for (int i = 0; i < historyA.Length; i++)
            {
                Assert.Equal(historyA[i].Role, sessionB.OpponentHistory[i].Role);
                Assert.Equal(historyA[i].Content, sessionB.OpponentHistory[i].Content);
            }
        }
    }
}
