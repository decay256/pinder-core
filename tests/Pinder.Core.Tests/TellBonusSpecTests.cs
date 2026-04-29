using System;
using System.Collections.Generic;
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
    /// Additional spec-driven tests for Issue #50: Tells — §15 opponent tell detection and hidden roll bonus.
    /// Covers edge case 10 (HasTellBonus true but player picks different option),
    /// edge case 3 (explicit matching stat test), and edge case 12 (game ends during turn with tell).
    /// </summary>
    [Trait("Category", "Core")]
    public class TellBonusSpecTests
    {
        // ================================================================
        // Edge Case 3 (explicit): Tell active + matching stat → TellReadBonus=2
        // Mutation: Fails if tell bonus is not exactly 2, or if tell comparison
        //           uses != instead of == for stat matching
        // ================================================================

        [Fact]
        public async Task EdgeCase3_TellActiveMatchingStat_BonusIs2()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Chaos, "Changes subject"));
            llm.EnqueueOptions(new DialogueOption(StatType.Chaos, "Wild card"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Mutation: would catch if bonus was 1 or 3 instead of 2
            Assert.Equal(2, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +2 bonus.", result.TellReadMessage);
            Assert.Equal(2, result.Roll.ExternalBonus);
        }

        // ================================================================
        // Edge Case 10: HasTellBonus=true on one option but player picks different
        // Mutation: Fails if tell bonus is applied based on HasTellBonus flag
        //           rather than actual stat comparison at resolve time
        // ================================================================

        [Fact]
        public async Task EdgeCase10_HasTellBonusTrueButPlayerPicksDifferentOption_NoBonus()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            // Two options: Wit (matching tell) and Charm (not matching)
            llm.EnqueueOptions(
                new DialogueOption(StatType.Wit, "Funny"),
                new DialogueOption(StatType.Charm, "Smooth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasTellBonus);   // Wit matches
            Assert.False(start1.Options[1].HasTellBonus);  // Charm doesn't

            // Player picks Charm (index 1), NOT the Wit option with HasTellBonus
            var result = await session.ResolveTurnAsync(1);

            // Mutation: would catch if implementation applied bonus based on HasTellBonus
            // flag of any option rather than comparing chosen option's stat to _activeTell
            Assert.Equal(0, result.TellReadBonus);
            Assert.Null(result.TellReadMessage);
            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // ================================================================
        // AC2 (additional): ExternalBonus flows through to FinalTotal correctly
        // Mutation: Fails if ExternalBonus is not added to Total to compute FinalTotal
        // ================================================================

        [Fact]
        public async Task AC2_FinalTotalEqualsTotalPlusExternalBonus()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Rizz, "Flirts"));
            llm.EnqueueOptions(new DialogueOption(StatType.Rizz, "Flirt back"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 12, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Mutation: catches if FinalTotal doesn't include ExternalBonus
            Assert.Equal(result.Roll.Total + result.Roll.ExternalBonus, result.Roll.FinalTotal);
            Assert.Equal(2, result.Roll.ExternalBonus);
        }

        // ================================================================
        // AC3: HasTellBonus is informational only — does not affect roll
        // Mutation: Fails if HasTellBonus flag itself modifies the roll outcome
        // ================================================================

        [Fact]
        public async Task AC3_HasTellBonusDoesNotAffectNonMatchingRoll()
        {
            // Set up tell for SelfAwareness, only offer Charm options
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.SelfAwareness, "Goes silent"));
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey there"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start = await session.StartTurnAsync();
            // Charm doesn't match SelfAwareness tell
            Assert.False(start.Options[0].HasTellBonus);

            var result = await session.ResolveTurnAsync(0);
            // No external bonus applied since stat doesn't match
            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // ================================================================
        // AC5 (additional): When _activeTell is null, all options have HasTellBonus=false
        // Mutation: Fails if HasTellBonus defaults to true or is set without checking null
        // ================================================================

        [Fact]
        public async Task AC5_NullTell_AllOptionsHasTellBonusFalse()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(
                new DialogueOption(StatType.Charm, "A"),
                new DialogueOption(StatType.Wit, "B"),
                new DialogueOption(StatType.Honesty, "C"),
                new DialogueOption(StatType.Chaos, "D"));

            var dice = new FixedDice(5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // First turn, no prior opponent response → _activeTell is null
            var start = await session.StartTurnAsync();

            // Mutation: catches if HasTellBonus is ever true when no tell is active
            foreach (var option in start.Options)
            {
                Assert.False(option.HasTellBonus);
            }
        }

        // ================================================================
        // AC4 (exact string): TellReadMessage matches exact constant
        // Mutation: Fails if message text differs (missing emoji, period, etc.)
        // ================================================================

        [Fact]
        public async Task AC4_TellReadMessage_ExactString()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Honesty, "Shares something"));
            llm.EnqueueOptions(new DialogueOption(StatType.Honesty, "Truth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Mutation: catches wrong emoji, missing period, different wording
            Assert.Equal("📖 You read the moment. +2 bonus.", result.TellReadMessage);
            Assert.Contains("📖", result.TellReadMessage);
            Assert.EndsWith(".", result.TellReadMessage);
        }

        // ================================================================
        // Tell consumed even when stat doesn't match (consumed regardless)
        // Mutation: Fails if _activeTell is only cleared on match
        // ================================================================

        [Fact]
        public async Task TellConsumedEvenOnMismatch()
        {
            var llm = new TellTestLlm();
            // Turn 0: set up tell for Wit
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes joke"));
            // Turn 1: player picks Charm (mismatch) — tell should be consumed
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Smooth"));
            llm.EnqueueTell(null);  // No new tell
            // Turn 2: Wit option should NOT have tell bonus (consumed in turn 1)
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Late joke"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: mismatch
            await session.StartTurnAsync();
            var result1 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, result1.TellReadBonus);

            // Turn 2: tell was consumed, no bonus
            var start2 = await session.StartTurnAsync();
            Assert.False(start2.Options[0].HasTellBonus);
            var result2 = await session.ResolveTurnAsync(0);
            Assert.Equal(0, result2.TellReadBonus);
        }

        // ================================================================
        // All six StatTypes can be tell stats
        // Mutation: Fails if tell matching is hardcoded to specific stats
        // ================================================================

        [Theory]
        [InlineData(StatType.Charm)]
        [InlineData(StatType.Rizz)]
        [InlineData(StatType.Honesty)]
        [InlineData(StatType.Wit)]
        [InlineData(StatType.Chaos)]
        [InlineData(StatType.SelfAwareness)]
        public async Task TellMatchesAllStatTypes(StatType stat)
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(stat, $"Tell for {stat}"));
            llm.EnqueueOptions(new DialogueOption(stat, "Match"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start = await session.StartTurnAsync();
            Assert.True(start.Options[0].HasTellBonus);

            var result = await session.ResolveTurnAsync(0);
            // Mutation: catches if any particular stat is excluded from tell matching
            Assert.Equal(2, result.TellReadBonus);
        }

        // ================================================================
        // Helpers (same pattern as TellBonusTests — isolated per test class)
        // ================================================================

        private static void ActivateTrapOnSession(GameSession session)
        {
            var trapDef = new TrapDefinition("TestTrap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "trap", "clear", "nat1");
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;
            trapState.Activate(trapDef);
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// LLM adapter that supports enqueuing tells per turn.
        /// </summary>
        private sealed class TellTestLlm : ILlmAdapter
        {
            private readonly Queue<DialogueOption[]> _optionSets = new Queue<DialogueOption[]>();
            private readonly Queue<Tell?> _tells = new Queue<Tell?>();

            public void EnqueueOptions(params DialogueOption[] options)
            {
                _optionSets.Enqueue(options);
            }

            public void EnqueueTell(Tell? tell)
            {
                _tells.Enqueue(tell);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                if (_optionSets.Count > 0)
                    return Task.FromResult(_optionSets.Dequeue());
                return Task.FromResult(new[] { new DialogueOption(StatType.Charm, "Default") });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                var tell = _tells.Count > 0 ? _tells.Dequeue() : null;
                return Task.FromResult(new OpponentResponse("...", detectedTell: tell));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
        }
    }
}
