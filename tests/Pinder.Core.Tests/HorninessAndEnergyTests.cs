using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public class HorninessAndEnergyTests
    {
        /// <summary>
        /// When a clock is present, session horniness = dice roll + clock modifier.
        /// Clock returns +3, dice returns 7 → sessionHorniness = 10.
        /// Since 10 < 12, RequiresRizzOption should be false.
        /// Since 10 < 18, options should NOT all be Rizz.
        /// </summary>
        [Fact]
        public async Task GameSession_HorninessRolledAtSessionStart_WithClock_AppliesModifier()
        {
            // Dice queue: 7 (horniness roll), then enough for StartTurnAsync (no rolls needed there)
            var dice = new FixedDice(7);
            var clock = new ConfigurableClock(horninessModifier: 3, remainingEnergy: 10);

            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Opponent"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            // StartTurnAsync builds the DialogueContext with horninessLevel
            var turn = await session.StartTurnAsync();

            // horniness = 7 + 3 = 10. < 12, so no RequiresRizzOption.
            // Options should have their original stats (not all Rizz).
            Assert.Equal(4, turn.Options.Length);
            // At least one option should NOT be Rizz (NullLlmAdapter returns Charm, Honesty, Wit, Chaos)
            bool hasNonRizz = false;
            for (int i = 0; i < turn.Options.Length; i++)
            {
                if (turn.Options[i].Stat != StatType.Rizz)
                    hasNonRizz = true;
            }
            Assert.True(hasNonRizz, "Horniness 10 should not convert all options to Rizz");
        }

        /// <summary>
        /// When horniness >= 18 (T3), all dialogue options become Rizz.
        /// Clock returns +5 (AfterTwoAm), dice returns 10 → horniness = 15 (not T3).
        /// So we use dice=10 + modifier=+8... but max modifier is +5.
        /// We need horniness >= 18: use dice=10, modifier=+8? No, IGameClock max is +5.
        /// So: dice roll must be high enough. dice=10 + 5 = 15. Not enough.
        /// We can't reach 18 with a d10 + max modifier 5 (max 15).
        /// Wait — the spec says 1d10. Max roll = 10, max modifier = 5 → max 15.
        /// But the code uses Math.Max(0, roll + modifier). So 18 is not reachable naturally.
        /// The T3 threshold is checked against _sessionHorniness which is set in constructor.
        /// For testing, we need to set it to >= 18 somehow.
        /// Since we can't modify _sessionHorniness directly, we need a custom dice and clock
        /// that can produce a total >= 18. But dice.Roll(10) can return any int — it's not
        /// clamped. The FixedDice just returns queued values.
        /// So FixedDice(15) + modifier(+3) = 18. That works!
        /// </summary>
        [Fact]
        public async Task GameSession_HorninessT3_AllOptionsBecomeRizz()
        {
            // FixedDice returns 15 for the horniness roll (not clamped to 1-10 in FixedDice)
            var dice = new FixedDice(15);
            var clock = new ConfigurableClock(horninessModifier: 3, remainingEnergy: 10);

            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Opponent"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            // horniness = 15 + 3 = 18 → T3
            var turn = await session.StartTurnAsync();

            Assert.Equal(4, turn.Options.Length);
            for (int i = 0; i < turn.Options.Length; i++)
            {
                Assert.Equal(StatType.Rizz, turn.Options[i].Stat);
            }
        }

        /// <summary>
        /// When clock.ConsumeEnergy returns false, ResolveTurnAsync should throw GameEndedException
        /// with Unmatched outcome.
        /// </summary>
        [Fact]
        public async Task GameSession_EnergyDepletion_ThrowsGameEnded()
        {
            // Dice queue: 5 (horniness roll), then enough for StartTurnAsync
            // ResolveTurnAsync will check energy before rolling, so no more dice needed if energy fails.
            var dice = new FixedDice(5);
            var clock = new ConfigurableClock(horninessModifier: 0, remainingEnergy: 0);

            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Opponent"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            var turn = await session.StartTurnAsync();
            Assert.NotNull(turn);

            var ex = await Assert.ThrowsAsync<GameEndedException>(
                () => session.ResolveTurnAsync(0));
            Assert.Equal(GameOutcome.Unmatched, ex.Outcome);
        }

        /// <summary>
        /// When horniness < 12, options retain original stats (no T2/T3 effect on options).
        /// When horniness >= 12 but < 18, RequiresRizzOption is set in context but options aren't all forced to Rizz.
        /// </summary>
        [Fact]
        public async Task GameSession_HorninessT2_DoesNotForceAllRizz()
        {
            // dice=10 + modifier(+3) = 13 → T2 (>= 12), but not T3 (< 18)
            var dice = new FixedDice(10);
            var clock = new ConfigurableClock(horninessModifier: 3, remainingEnergy: 10);

            var config = new GameSessionConfig(clock: clock);
            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Opponent"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            var turn = await session.StartTurnAsync();
            Assert.Equal(4, turn.Options.Length);

            // Options should NOT all be Rizz at T2
            bool hasNonRizz = false;
            for (int i = 0; i < turn.Options.Length; i++)
            {
                if (turn.Options[i].Stat != StatType.Rizz)
                    hasNonRizz = true;
            }
            Assert.True(hasNonRizz, "Horniness T2 (13) should not force all options to Rizz");
        }

        /// <summary>
        /// Without a clock, horniness is still rolled (1d10) but with no time-of-day modifier.
        /// No energy is consumed.
        /// </summary>
        [Fact]
        public async Task GameSession_WithClock_HorninessRolled_NoEnergyCheck()
        {
            // Clock required. Horniness roll (1d10) = 5, then Turn 1: d20=15 (roll), d100=50 (timing delay)
            var dice = new FixedDice(
                5,   // horniness roll
                15, 50);
            var clock = new ConfigurableClock(horninessModifier: 0, remainingEnergy: 100);
            var config = new GameSessionConfig(clock: clock);

            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Opponent"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            var turn = await session.StartTurnAsync();
            Assert.Equal(4, turn.Options.Length);

            // Should not throw
            var result = await session.ResolveTurnAsync(0);
            Assert.NotNull(result);
        }

        /// <summary>
        /// Without a clock, session horniness equals the raw dice roll (no time-of-day modifier).
        /// Horniness >= 18 forces all options to Rizz even without a clock.
        /// </summary>
        [Fact]
        public async Task GameSession_WithClock_HorninessRolled_ForcesRizzAtT3()
        {
            // Clock required. Dice roll 20 + modifier 0 = 20 >= 18, forces all options to Rizz.
            var dice = new FixedDice(20);
            var clock = new ConfigurableClock(horninessModifier: 0, remainingEnergy: 100);
            var config = new GameSessionConfig(clock: clock);

            var session = new GameSession(
                MakeProfile("Player"),
                MakeProfile("Opponent"),
                new NullLlmAdapter(),
                dice,
                new NullTrapRegistry(),
                config);

            var turn = await session.StartTurnAsync();

            // Horniness = 20 (modifier 0) >= 18, all options should be Rizz
            for (int i = 0; i < turn.Options.Length; i++)
            {
                Assert.Equal(StatType.Rizz, turn.Options[i].Stat);
            }
        }

        // ======================== Helpers ========================

        private static CharacterProfile MakeProfile(string name)
        {
            var stats = TestHelpers.MakeStatBlock();
            var timing = new TimingProfile(5, 0.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, $"You are {name}.", name, timing, 1);
        }

        private sealed class ConfigurableClock : IGameClock
        {
            private readonly int _horninessModifier;
            private int _remainingEnergy;

            public ConfigurableClock(int horninessModifier, int remainingEnergy)
            {
                _horninessModifier = horninessModifier;
                _remainingEnergy = remainingEnergy;
            }

            public DateTimeOffset Now => DateTimeOffset.UtcNow;
            public int RemainingEnergy => _remainingEnergy;
            public void Advance(TimeSpan amount) { }
            public void AdvanceTo(DateTimeOffset target) { }
            public TimeOfDay GetTimeOfDay() => TimeOfDay.LateNight;
            public int GetHorninessModifier() => _horninessModifier;

            public bool ConsumeEnergy(int amount)
            {
                if (_remainingEnergy < amount) return false;
                _remainingEnergy -= amount;
                return true;
            }
        }
    }
}
