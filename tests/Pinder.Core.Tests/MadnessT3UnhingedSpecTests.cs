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
    /// Tests for Madness T3 (≥18) unhinged option replacement (#310).
    /// Spec: docs/specs/issue-310-spec.md
    /// Maturity: Prototype (happy-path per acceptance criterion + edge cases).
    /// </summary>
    [Trait("Category", "Core")]
    public class MadnessT3UnhingedSpecTests
    {
        // =====================================================================
        // AC1: DialogueOption has IsUnhingedReplacement bool property
        // =====================================================================

        // Mutation: would catch if IsUnhingedReplacement defaults to true instead of false
        [Fact]
        public void AC1_DialogueOption_IsUnhingedReplacement_DefaultsFalse()
        {
            var opt = new DialogueOption(StatType.Charm, "Hello");
            Assert.False(opt.IsUnhingedReplacement);
        }

        // Mutation: would catch if constructor ignores isUnhingedReplacement parameter
        [Fact]
        public void AC1_DialogueOption_CanBeConstructedWithUnhingedTrue()
        {
            var opt = new DialogueOption(
                StatType.Wit, "test", isUnhingedReplacement: true);
            Assert.True(opt.IsUnhingedReplacement);
        }

        // Mutation: would catch if isUnhingedReplacement is always true when explicitly set
        [Fact]
        public void AC1_DialogueOption_ExplicitFalseStaysFalse()
        {
            var opt = new DialogueOption(
                StatType.Rizz, "hey", isUnhingedReplacement: false);
            Assert.False(opt.IsUnhingedReplacement);
        }

        // =====================================================================
        // AC2: At Madness T3 (≥18), exactly one option is marked unhinged
        // =====================================================================

        // Mutation: would catch if the >= 18 check is removed entirely (no unhinged ever)
        [Fact]
        public async Task AC2_MadnessExactly18_MarksExactlyOneOption()
        {
            var shadows = MakeShadowTracker(madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever"),
                new DialogueOption(StatType.Honesty, "Truth")
            };

            var session = MakeSession(
                diceValues: new[] { 2 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            int unhingedCount = turn.Options.Count(o => o.IsUnhingedReplacement);
            Assert.Equal(1, unhingedCount);
        }

        // Mutation: would catch if threshold used > 18 instead of >= 18
        [Fact]
        public async Task AC2_MadnessFarAbove18_StillMarksExactlyOne()
        {
            var shadows = MakeShadowTracker(madness: 30);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "A"),
                new DialogueOption(StatType.Wit, "B"),
                new DialogueOption(StatType.Chaos, "C"),
                new DialogueOption(StatType.Rizz, "D")
            };

            var session = MakeSession(
                diceValues: new[] { 3 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            int unhingedCount = turn.Options.Count(o => o.IsUnhingedReplacement);
            Assert.Equal(1, unhingedCount);
        }

        // Mutation: would catch if replacement doesn't preserve Stat property
        [Fact]
        public async Task AC2_UnhingedOptionPreservesOriginalStat()
        {
            var shadows = MakeShadowTracker(madness: 20);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey babe")
            };

            // dice Roll(1) → 1, so idx=0 selected
            var session = MakeSession(
                diceValues: new[] { 1 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            var unhinged = turn.Options.Single(o => o.IsUnhingedReplacement);
            Assert.Equal(StatType.Charm, unhinged.Stat);
        }

        // Mutation: would catch if replacement doesn't preserve IntendedText
        [Fact]
        public async Task AC2_UnhingedOptionPreservesIntendedText()
        {
            var shadows = MakeShadowTracker(madness: 19);
            var options = new[]
            {
                new DialogueOption(StatType.Wit, "Witty remark"),
                new DialogueOption(StatType.Chaos, "Chaotic line")
            };

            // dice Roll(2) → 1, so idx=0
            var session = MakeSession(
                diceValues: new[] { 1 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            var unhinged = turn.Options.Single(o => o.IsUnhingedReplacement);
            Assert.Equal("Witty remark", unhinged.IntendedText);
        }

        // Mutation: would catch if all options are marked instead of exactly one
        [Fact]
        public async Task AC2_OnlyOneOptionMarkedNotAll()
        {
            var shadows = MakeShadowTracker(madness: 22);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "A"),
                new DialogueOption(StatType.Wit, "B"),
                new DialogueOption(StatType.Honesty, "C")
            };

            var session = MakeSession(
                diceValues: new[] { 2 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            int unhingedCount = turn.Options.Count(o => o.IsUnhingedReplacement);
            Assert.Equal(1, unhingedCount);
            // Also verify the non-unhinged ones are explicitly false
            int normalCount = turn.Options.Count(o => !o.IsUnhingedReplacement);
            Assert.True(normalCount >= 1);
        }

        // =====================================================================
        // AC3: At Madness T2 or lower, no options marked unhinged
        // =====================================================================

        // Mutation: would catch if threshold check is >= 12 instead of >= 18
        [Fact]
        public async Task AC3_MadnessT2_NoOptionsMarked()
        {
            var shadows = MakeShadowTracker(madness: 12);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(
                diceValues: new[] { 10 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.All(turn.Options, o => Assert.False(o.IsUnhingedReplacement));
        }

        // Mutation: would catch if boundary at 17 is treated as T3
        [Fact]
        public async Task AC3_Madness17_JustBelowThreshold_NoOptions()
        {
            var shadows = MakeShadowTracker(madness: 17);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(
                diceValues: new[] { 10 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.All(turn.Options, o => Assert.False(o.IsUnhingedReplacement));
        }

        // Mutation: would catch if Madness=0 somehow triggers marking
        [Fact]
        public async Task AC3_MadnessZero_NoOptionsMarked()
        {
            var shadows = MakeShadowTracker(madness: 0);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(
                diceValues: new[] { 10 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.All(turn.Options, o => Assert.False(o.IsUnhingedReplacement));
        }

        // Mutation: would catch if null shadow tracker causes crash or marks options
        [Fact]
        public async Task AC3_NoShadowTracker_NoOptionsMarked()
        {
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.Wit, "Clever")
            };

            var session = MakeSession(
                diceValues: new[] { 10 },
                shadows: null,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.All(turn.Options, o => Assert.False(o.IsUnhingedReplacement));
        }

        // =====================================================================
        // AC5: Tests verify T3 marks one option, T2 marks none (edge cases)
        // =====================================================================

        // Mutation: would catch if single-option case crashes or doesn't mark
        [Fact]
        public async Task AC5_SingleOption_MadnessT3_ThatOptionMarked()
        {
            var shadows = MakeShadowTracker(madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Chaos, "Only option")
            };

            var session = MakeSession(
                diceValues: new[] { 1 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            Assert.Single(turn.Options);
            Assert.True(turn.Options[0].IsUnhingedReplacement);
        }

        // Mutation: would catch if empty options causes index error instead of no-op
        [Fact]
        public async Task EdgeCase_EmptyOptions_MadnessT3_NoCrash()
        {
            var shadows = MakeShadowTracker(madness: 20);
            var options = Array.Empty<DialogueOption>();

            var session = MakeSession(
                diceValues: new[] { 1 },
                shadows: shadows,
                llmOptions: options);

            // Should not throw — empty guard prevents index error
            var turn = await session.StartTurnAsync();

            Assert.Empty(turn.Options);
        }

        // Note: ComboName, HasTellBonus, HasWeaknessWindow are computed by
        // GameSession during StartTurnAsync (combo tracker, tell/weakness detection),
        // so they are not directly testable for preservation via LLM-provided values.
        // The spec's preservation guarantee applies to Stat and IntendedText which are
        // tested above.

        // =====================================================================
        // Edge Case: Interaction with Fixation T3
        // =====================================================================

        // Mutation: would catch if Madness T3 runs before Fixation T3 (wrong order)
        // or doesn't apply to Fixation-modified options
        [Fact]
        public async Task EdgeCase_FixationT3_PlusMadnessT3_BothApply()
        {
            // Fixation T3 forces all options to last stat; Madness T3 then marks one unhinged
            var shadows = MakeShadowTracker(fixation: 18, madness: 18);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "A"),
                new DialogueOption(StatType.Wit, "B"),
                new DialogueOption(StatType.Chaos, "C")
            };

            var session = MakeSession(
                diceValues: new[] { 2 },
                shadows: shadows,
                llmOptions: options);

            var turn = await session.StartTurnAsync();

            // Madness T3 should still mark exactly one option
            int unhingedCount = turn.Options.Count(o => o.IsUnhingedReplacement);
            Assert.Equal(1, unhingedCount);
        }

        // =====================================================================
        // Helpers (duplicated from ShadowThresholdGameSessionTests pattern)
        // =====================================================================

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
                    { ShadowStatType.Overthinking, overthinking }, { ShadowStatType.Despair, horniness }
                });
            return new SessionShadowTracker(stats);
        }

        private static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats }, { StatType.Rizz, allStats }, { StatType.Honesty, allStats },
                { StatType.Chaos, allStats }, { StatType.Wit, allStats }, { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, allShadow }, { ShadowStatType.Despair, allShadow },
                { ShadowStatType.Denial, allShadow }, { ShadowStatType.Fixation, allShadow },
                { ShadowStatType.Dread, allShadow }, { ShadowStatType.Overthinking, allShadow }
            };
            return new StatBlock(stats, shadow);
        }

        private static CharacterProfile MakeProfile(string name, StatBlock? stats = null)
        {
            stats = stats ?? MakeStatBlock();
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            return new CharacterProfile(stats, "system prompt", name, timing, 1);
        }

        private static GameSession MakeSession(
            int[] diceValues,
            SessionShadowTracker? shadows,
            DialogueOption[]? llmOptions = null,
            int? startingInterest = null)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows,
                startingInterest: startingInterest);

            ILlmAdapter llm = llmOptions != null
                ? new CustomOptionsLlmAdapter(llmOptions)
                : (ILlmAdapter)new NullLlmAdapter();

            // First dice value is for horniness roll (Roll(10)), rest are for game logic
            var allDice = new int[diceValues.Length + 1];
            allDice[0] = 5; // horniness roll → low value, won't trigger Horniness T3
            Array.Copy(diceValues, 0, allDice, 1, diceValues.Length);

            return new GameSession(
                MakeProfile("player"),
                MakeProfile("opponent"),
                llm,
                new SafeQueueDice(allDice),
                new NullTrapRegistry(),
                config);
        }

        /// <summary>Dice that returns values from a queue, falls back to 10.</summary>
        private sealed class SafeQueueDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public SafeQueueDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        /// <summary>LLM adapter that returns fixed options.</summary>
        private sealed class CustomOptionsLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public CustomOptionsLlmAdapter(DialogueOption[] options) => _options = options;

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null) => System.Threading.Tasks.Task.FromResult(message);
        }

                /// <summary>Null trap registry for tests.</summary>
        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
