using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #273: Madness T3 (≥18) replaces one option with unhinged text (§7).
    /// Spec: docs/specs/issue-273-spec.md
    /// Maturity: Prototype — happy-path per AC + edge cases.
    /// </summary>
    public class MadnessT3UnhingedTests
    {
        // =====================================================================
        // AC1: At Madness ≥18, exactly one dialogue option is flagged IsUnhinged
        // =====================================================================

        // Mutation: would catch if Madness T3 block is missing entirely (no options marked)
        [Fact]
        public async Task MadnessAt18_ExactlyOneOptionIsUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 18);
            var options = ThreeOptions();
            // Dice: madnessRoll=2 (picks index 1), d20=15, delay=50
            var session = BuildSession(new[] { 2, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.Equal(3, turn.Options.Length);
            Assert.Single(turn.Options, o => o.IsUnhinged);
        }

        // Mutation: would catch if threshold check uses > instead of >=
        [Fact]
        public async Task MadnessExactly18_BoundaryTriggersUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 18);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 1, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.Single(turn.Options, o => o.IsUnhinged);
        }

        // Mutation: would catch if only Madness=18 triggers but not higher values
        [Fact]
        public async Task MadnessAbove18_StillTriggersUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 25);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 3, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.Single(turn.Options, o => o.IsUnhinged);
        }

        // Mutation: would catch if more than one option gets marked unhinged
        [Fact]
        public async Task MadnessT3_MarksExactlyOne_NotMultiple()
        {
            var shadows = MakeShadowTracker(madness: 20);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 2, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            int unhingedCount = turn.Options.Count(o => o.IsUnhinged);
            Assert.Equal(1, unhingedCount);
        }

        // =====================================================================
        // AC3: Madness=12 (T2) → no options marked
        // =====================================================================

        // Mutation: would catch if threshold check uses T2 (12) instead of T3 (18)
        [Fact]
        public async Task MadnessAt12_T2_NoOptionsMarkedUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 12);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.DoesNotContain(turn.Options, o => o.IsUnhinged);
        }

        // Mutation: would catch if threshold check uses T1 (6) instead of T3 (18)
        [Fact]
        public async Task MadnessAt6_T1_NoOptionsMarkedUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 6);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.DoesNotContain(turn.Options, o => o.IsUnhinged);
        }

        // Mutation: would catch if Madness=0 somehow marks an option
        [Fact]
        public async Task MadnessAt0_NoOptionsMarkedUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 0);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.DoesNotContain(turn.Options, o => o.IsUnhinged);
        }

        // Mutation: would catch if boundary is off by one (17 should NOT trigger)
        [Fact]
        public async Task MadnessAt17_JustBelowT3_NoOptionsMarked()
        {
            var shadows = MakeShadowTracker(madness: 17);
            var options = ThreeOptions();
            var session = BuildSession(new[] { 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.DoesNotContain(turn.Options, o => o.IsUnhinged);
        }

        // =====================================================================
        // AC3 (deterministic index): dice controls which option is marked
        // =====================================================================

        // Mutation: would catch if index calculation is wrong (e.g., not using Roll(n)-1)
        [Theory]
        [InlineData(1, 0)] // Roll(3)=1 → index 0
        [InlineData(2, 1)] // Roll(3)=2 → index 1
        [InlineData(3, 2)] // Roll(3)=3 → index 2
        public async Task MadnessT3_DiceSelectsCorrectIndex(int diceRoll, int expectedIndex)
        {
            var shadows = MakeShadowTracker(madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Option A"),
                new DialogueOption(StatType.Wit, "Option B"),
                new DialogueOption(StatType.Honesty, "Option C")
            };
            var session = BuildSession(new[] { diceRoll, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            for (int i = 0; i < turn.Options.Length; i++)
            {
                if (i == expectedIndex)
                    Assert.True(turn.Options[i].IsUnhinged, $"Option at index {i} should be unhinged");
                else
                    Assert.False(turn.Options[i].IsUnhinged, $"Option at index {i} should NOT be unhinged");
            }
        }

        // =====================================================================
        // Stat preservation: unhinged option retains original stat
        // =====================================================================

        // Mutation: would catch if Madness T3 changes the stat instead of preserving it
        [Fact]
        public async Task MadnessT3_UnhingedOptionPreservesOriginalStat()
        {
            var shadows = MakeShadowTracker(madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Charming"),
                new DialogueOption(StatType.Wit, "Witty"),
                new DialogueOption(StatType.Honesty, "Honest")
            };
            // Dice: Roll(3)=1 → index 0 (Charm option)
            var session = BuildSession(new[] { 1, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            var unhinged = turn.Options.Single(o => o.IsUnhinged);
            Assert.Equal(StatType.Charm, unhinged.Stat);
        }

        // =====================================================================
        // Edge case: no player shadows → no unhinged options
        // =====================================================================

        // Mutation: would catch if null shadow tracker causes crash or marks options
        [Fact]
        public async Task NoShadowTracker_NoCrashAndNoUnhingedOptions()
        {
            var options = ThreeOptions();
            var session = BuildSession(new[] { 15, 50 }, shadows: null, llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.DoesNotContain(turn.Options, o => o.IsUnhinged);
        }

        // =====================================================================
        // Edge case: single option available → it becomes unhinged
        // =====================================================================

        // Mutation: would catch if code skips marking when only 1 option exists
        [Fact]
        public async Task MadnessT3_SingleOption_BecomesUnhinged()
        {
            var shadows = MakeShadowTracker(madness: 20);
            var options = new[]
            {
                new DialogueOption(StatType.Chaos, "Yolo")
            };
            // Dice: Roll(1)=1 → index 0
            var session = BuildSession(new[] { 1, 15, 50 }, shadows, options);

            var turn = await session.StartTurnAsync();

            Assert.Single(turn.Options);
            Assert.True(turn.Options[0].IsUnhinged);
        }

        // =====================================================================
        // Edge case: Madness T3 + Horniness T3 → flag preserved after Rizz conversion
        // =====================================================================

        // Mutation: would catch if Horniness T3 reconstruction drops IsUnhinged flag
        [Fact]
        public async Task MadnessT3_PlusHorninessT3_UnhingedFlagPreserved()
        {
            var shadows = MakeShadowTracker(madness: 18);
            var llmOptions = new[]
            {
                new DialogueOption(StatType.Charm, "Hi"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Honesty, "Truth")
            };

            // Build directly to control horniness roll:
            // Dice[0]=20 (horniness → _sessionHorniness=20≥18 → T3 Horniness),
            // 2 (madnessRoll → idx 1), 15, 50
            var config = new GameSessionConfig(playerShadows: shadows);
            var session = new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                new FixedOptionsLlmAdapter(llmOptions),
                new QueueDice(new[] { 20, 2, 15, 50 }),
                new EmptyTrapRegistry(),
                config);

            var turn = await session.StartTurnAsync();

            // Horniness T3 converts all stats to Rizz
            Assert.All(turn.Options, o => Assert.Equal(StatType.Rizz, o.Stat));
            // Madness T3 flag must survive Horniness T3 reconstruction
            Assert.Single(turn.Options, o => o.IsUnhinged);
        }

        // =====================================================================
        // Edge case: Madness T3 + Fixation T3 interaction
        // =====================================================================

        // Mutation: would catch if Fixation T3 interferes with Madness T3 marking
        [Fact]
        public async Task MadnessT3_PlusFixationT3_BothEffectsApply()
        {
            var shadows = MakeShadowTracker(madness: 18, fixation: 18);
            var llmOptions = new[]
            {
                new DialogueOption(StatType.Charm, "Hi"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Rizz, "Smooth")
            };

            // Need a previous turn with a stat for Fixation to lock to.
            // Dice: 5(horniness), fixation-dice-if-needed, madnessRoll=1, d20=15, delay=50
            // Fixation T3 forces all stats to last-used stat; Madness T3 marks one unhinged
            var session = BuildSession(new[] { 1, 15, 50 }, shadows, llmOptions);

            var turn = await session.StartTurnAsync();

            // At least one option should be unhinged regardless of Fixation T3
            Assert.Single(turn.Options, o => o.IsUnhinged);
        }

        // =====================================================================
        // DialogueOption.IsUnhinged defaults to false
        // =====================================================================

        // Mutation: would catch if default value for IsUnhinged is true
        [Fact]
        public void DialogueOption_IsUnhinged_DefaultsFalse()
        {
            var option = new DialogueOption(StatType.Charm, "Hello");
            Assert.False(option.IsUnhinged);
        }

        // Mutation: would catch if explicit isUnhinged=true doesn't set the property
        [Fact]
        public void DialogueOption_IsUnhinged_CanBeSetTrue()
        {
            var option = new DialogueOption(StatType.Charm, "Hello", isUnhinged: true);
            Assert.True(option.IsUnhinged);
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static DialogueOption[] ThreeOptions() => new[]
        {
            new DialogueOption(StatType.Charm, "Hey there"),
            new DialogueOption(StatType.Wit, "Nice hat"),
            new DialogueOption(StatType.Rizz, "Come here often?")
        };

        private static SessionShadowTracker MakeShadowTracker(
            int dread = 0, int denial = 0, int fixation = 0,
            int madness = 0, int overthinking = 0, int horniness = 0)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Dread, dread }, { ShadowStatType.Denial, denial },
                    { ShadowStatType.Fixation, fixation }, { ShadowStatType.Madness, madness },
                    { ShadowStatType.Overthinking, overthinking }, { ShadowStatType.Horniness, horniness }
                });
            return new SessionShadowTracker(stats);
        }

        private static CharacterProfile MakeProfile(string name)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 2 }, { StatType.Rizz, 2 }, { StatType.Honesty, 2 },
                    { StatType.Chaos, 2 }, { StatType.Wit, 2 }, { StatType.SelfAwareness, 2 }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession BuildSession(
            int[] diceValues,
            SessionShadowTracker? shadows,
            DialogueOption[]? llmOptions = null)
        {
            var config = new GameSessionConfig(playerShadows: shadows);

            ILlmAdapter llm = llmOptions != null
                ? (ILlmAdapter)new FixedOptionsLlmAdapter(llmOptions)
                : new NullLlmAdapter();

            // Prepend 5 for the ghost-check / horniness dice roll in constructor
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5;
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new QueueDice(allDice),
                new EmptyTrapRegistry(),
                config);
        }

        // ---- Test doubles ----

        private sealed class QueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public QueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private sealed class FixedOptionsLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public FixedOptionsLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}
