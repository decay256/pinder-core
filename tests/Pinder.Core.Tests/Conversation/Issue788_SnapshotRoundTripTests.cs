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
    /// Issue #788: snapshot/restore round-trip for the engine-owned datee
    /// LLM history. Locks that <see cref="GameSession.DateeHistory"/>
    /// survives a <see cref="GameSession.RestoreState"/> call so a replayed
    /// session can reproduce the same multi-turn datee context the
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
                level: 1,
                psychiatricDiagnosis: ValidDiagnosis(),
                backstory: TestHelpers.MakeBackstory(),
                stakeLines: TestHelpers.MakeStakeLines());
        }

        private static IReadOnlyDictionary<string, string> ValidDiagnosis()
        {
            return new Dictionary<string, string>
            {
                ["derived_feeling"] = "curious",
                ["defense_reaction"] = "guarded",
            };
        }

        [Fact]
        public async Task PlayingTurns_AccumulatesDateeHistory()
        {
            // Provide enough dice values for a few turns: ctor d10 + per-turn d20 main + d100 timing.
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5, 15, 50, 15, 50, 15, 50),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Engine starts with empty datee history.
            Assert.Empty(session.DateeHistory);

            // Resolve one turn — NullLlmAdapter contributes one user + one assistant entry.
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.Equal(2, session.DateeHistory.Count);
            Assert.Equal(ConversationMessage.UserRole, session.DateeHistory[0].Role);
            Assert.Equal(ConversationMessage.AssistantRole, session.DateeHistory[1].Role);
        }

        [Fact]
        public void RestoreState_RebuildsDateeHistoryFromResimData()
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
                DateeHistory = new List<(string, string)>
                {
                    ("user", "first user prompt"),
                    ("assistant", "first datee reply"),
                    ("user", "second user prompt"),
                    ("assistant", "second datee reply"),
                },
            };
            session.RestoreState(resim, new NullTrapRegistry());

            Assert.Equal(4, session.DateeHistory.Count);
            Assert.Equal("user", session.DateeHistory[0].Role);
            Assert.Equal("first user prompt", session.DateeHistory[0].Content);
            Assert.Equal("assistant", session.DateeHistory[1].Role);
            Assert.Equal("first datee reply", session.DateeHistory[1].Content);
            Assert.Equal("user", session.DateeHistory[2].Role);
            Assert.Equal("assistant", session.DateeHistory[3].Role);
            Assert.Equal("second datee reply", session.DateeHistory[3].Content);

            // CreateSnapshot reflects the restored history.
            var snap = session.CreateSnapshot();
            Assert.Equal(4, snap.DateeHistory.Count);
            Assert.Equal("first datee reply", snap.DateeHistory[1].Content);
        }

        [Theory]
        [InlineData("moderator", "datee")]
        [InlineData("", "datee")]
        [InlineData("system", "avatar")]
        public void RestoreState_WithMalformedPersistedHistoryRole_FailsFast(string role, string historyKind)
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
            };

            if (historyKind == "datee")
            {
                resim.DateeHistory = new List<(string, string)> { (role, "bad datee message") };
            }
            else
            {
                resim.AvatarHistory = new List<(string, string)> { (role, "bad avatar message") };
            }

            var ex = Assert.Throws<InvalidOperationException>(
                () => session.RestoreState(resim, new NullTrapRegistry()));

            Assert.Contains(historyKind, ex.Message);
            Assert.Contains("entry 0", ex.Message);
            Assert.Contains("role", ex.Message);
        }

        [Fact]
        public void RestoreState_WithEmptyDateeHistory_ClearsAndStaysEmpty()
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
                DateeHistory = new List<(string, string)>
                {
                    ("user", "stale"),
                    ("assistant", "stale"),
                },
            }, new NullTrapRegistry());
            Assert.Equal(2, session.DateeHistory.Count);

            // Now restore with empty datee history — the list should clear.
            session.RestoreState(new ResimulateData
            {
                TargetInterest = session.CreateSnapshot().Interest,
                TurnNumber = 0,
                DateeHistory = new List<(string, string)>(),
            }, new NullTrapRegistry());
            Assert.Empty(session.DateeHistory);
        }

        [Fact]
        public async Task PlayedSession_SnapshotedAndReplayed_ReproducesDateeHistory()
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
            var historyA = sessionA.DateeHistory.ToArray();

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
                DateeHistory = historyA.Select(m => (m.Role, m.Content)).ToList(),
            }, new NullTrapRegistry());

            Assert.Equal(historyA.Length, sessionB.DateeHistory.Count);
            for (int i = 0; i < historyA.Length; i++)
            {
                Assert.Equal(historyA[i].Role, sessionB.DateeHistory[i].Role);
                Assert.Equal(historyA[i].Content, sessionB.DateeHistory[i].Content);
            }
        }

        /// <summary>
        /// #1123 review blocker: the public <see cref="GameSession.CreateSnapshot"/>
        /// must populate <see cref="GameStateSnapshot.AvatarHistory"/> symmetrically
        /// with <see cref="GameStateSnapshot.DateeHistory"/>. The instance method
        /// previously omitted the avatar-history argument, so the returned snapshot's
        /// AvatarHistory was silently always empty. This drives avatar history onto a
        /// session via RestoreState, calls the PUBLIC CreateSnapshot(), and asserts the
        /// snapshot round-trips the avatar history (NOT empty).
        /// </summary>
        [Fact]
        public void CreateSnapshot_PopulatesAvatarHistory_FromRestoredState()
        {
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Engine starts with empty avatar history -> snapshot reflects empty.
            Assert.Empty(session.CreateSnapshot().AvatarHistory);

            session.RestoreState(new ResimulateData
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
                AvatarHistory = new List<(string, string)>
                {
                    ("user", "first avatar prompt"),
                    ("assistant", "first avatar reply"),
                    ("user", "second avatar prompt"),
                    ("assistant", "second avatar reply"),
                },
            }, new NullTrapRegistry());

            // The live engine view reflects the restored avatar history.
            Assert.Equal(4, session.AvatarHistory.Count);

            // The PUBLIC CreateSnapshot() must round-trip the avatar history, not drop it.
            var snap = session.CreateSnapshot();
            Assert.Equal(4, snap.AvatarHistory.Count);
            Assert.Equal("user", snap.AvatarHistory[0].Role);
            Assert.Equal("first avatar prompt", snap.AvatarHistory[0].Content);
            Assert.Equal("assistant", snap.AvatarHistory[1].Role);
            Assert.Equal("first avatar reply", snap.AvatarHistory[1].Content);
            Assert.Equal("user", snap.AvatarHistory[2].Role);
            Assert.Equal("assistant", snap.AvatarHistory[3].Role);
            Assert.Equal("second avatar reply", snap.AvatarHistory[3].Content);
        }

        [Fact]
        public void RestoreState_WithMalformedPersistedRole_IsAtomic()
        {
            var tracker = new SessionShadowTracker(TestHelpers.MakeStatBlock());
            var session = new GameSession(
                MakeProfile("P1"),
                MakeProfile("P2"),
                new NullLlmAdapter(),
                new FixedDice(5),
                new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: tracker));

            session.RestoreState(new ResimulateData
            {
                TargetInterest = 14,
                TurnNumber = 3,
                MomentumStreak = 2,
                ConversationHistory = new List<(string, string)>
                {
                    ("P1", "kept player line"),
                    ("P2", "kept datee line"),
                },
                DateeHistory = new List<(string, string)>
                {
                    ("user", "kept datee prompt"),
                    ("assistant", "kept datee reply"),
                },
                AvatarHistory = new List<(string, string)>
                {
                    ("user", "kept avatar prompt"),
                    ("assistant", "kept avatar reply"),
                },
                ComboHistory = new List<(string, bool)>(),
                PendingTripleBonus = true,
                RizzCumulativeFailureCount = 1,
            }, new NullTrapRegistry());
            tracker.ApplyGrowth(ShadowStatType.Dread, 2, "state before restore");

            var before = session.CreateSnapshot();
            var conversationBefore = session.ConversationHistory
                .Select(e => (e.Sender, e.Text))
                .ToList();
            var dateeBefore = session.DateeHistory
                .Select(m => (m.Role, m.Content))
                .ToList();
            var avatarBefore = session.AvatarHistory
                .Select(m => (m.Role, m.Content))
                .ToList();

            var badRestore = new ResimulateData
            {
                TargetInterest = 1,
                TurnNumber = 99,
                MomentumStreak = 99,
                ConversationHistory = new List<(string, string)>
                {
                    ("P1", "replacement line"),
                },
                DateeHistory = new List<(string, string)>
                {
                    ("system", "unsupported role"),
                },
                AvatarHistory = new List<(string, string)>
                {
                    ("user", "replacement avatar prompt"),
                },
                ComboHistory = new List<(string, bool)>(),
                PendingTripleBonus = false,
                RizzCumulativeFailureCount = 99,
                ShadowValues = new Dictionary<string, int>
                {
                    [ShadowStatType.Dread.ToString()] = 19,
                },
            };

            var ex = Assert.Throws<InvalidOperationException>(
                () => session.RestoreState(badRestore, new NullTrapRegistry()));

            Assert.Contains("datee", ex.Message);
            Assert.Contains("role", ex.Message);

            var after = session.CreateSnapshot();
            Assert.Equal(before.Interest, after.Interest);
            Assert.Equal(before.MomentumStreak, after.MomentumStreak);
            Assert.Equal(before.TurnNumber, after.TurnNumber);
            Assert.Equal(before.TripleBonusActive, after.TripleBonusActive);
            Assert.Equal(2, tracker.GetEffectiveShadow(ShadowStatType.Dread));
            Assert.Equal(conversationBefore, session.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList());
            Assert.Equal(dateeBefore, session.DateeHistory.Select(m => (m.Role, m.Content)).ToList());
            Assert.Equal(avatarBefore, session.AvatarHistory.Select(m => (m.Role, m.Content)).ToList());
        }
    }
}
