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
    /// Tests for Issue #50: Tells — §15 datee tell detection and hidden roll bonus.
    /// Covers AC1–AC7 and edge cases 1–12 from docs/specs/issue-50-spec.md.
    /// </summary>
    [Trait("Category", "Core")]
    public partial class TellBonusTests
    {
        // ================================================================
        // AC1: GameSession stores active tell from DateeResponse.DetectedTell
        // ================================================================

        [Fact]
        public async Task AC1_TellStoredFromDateeResponse()
        {
            // Turn 0: datee returns a tell for Wit
            // Turn 1: Wit option should have HasTellBonus=true
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes a joke"));
            llm.EnqueueOptions(
                new DialogueOption(StatType.Wit, "Funny"),
                new DialogueOption(StatType.Charm, "Smooth"));

            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            var start0 = await session.StartTurnAsync();
            Assert.False(start0.Options[0].HasTellBonus); // No tell yet
            await session.ResolveTurnAsync(0);

            // Turn 1: tell now active
            var start1 = await session.StartTurnAsync();
            Assert.True(start1.Options[0].HasTellBonus);   // Wit matches tell
            Assert.False(start1.Options[1].HasTellBonus);  // Charm doesn't match
        }

        // ================================================================
        // AC2: Matching stat → +2 via externalBonus on RollEngine.Resolve
        // ================================================================

        [Fact]
        public async Task AC2_MatchingStatAppliesTellBonusViaExternalBonus()
        {
            // Set up: datee gives Wit tell, player picks Wit
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Wit, "Makes a joke"));
            llm.EnqueueOptions(new DialogueOption(StatType.Wit, "Funny"));
            llm.EnqueueTell(null);

            // Turn 0: d20=15, timing=5. Turn 1: d20=15, timing=5
            var dice = new FixedDice(5, 15, 5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 0
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 1: pick Wit (matches tell)
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // ExternalBonus should be 4
            Assert.Equal(4, result.Roll.ExternalBonus);
            Assert.Equal(result.Roll.Total + 4, result.Roll.FinalTotal);
        }

        // ================================================================
        // AC4: TurnResult.TellReadBonus and TellReadMessage populated
        // ================================================================

        [Fact]
        public async Task AC4_TellReadBonusPopulatedOnMatch()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Honesty, "Shares vulnerability"));
            llm.EnqueueOptions(new DialogueOption(StatType.Honesty, "Truth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(4, result.TellReadBonus);
            Assert.Equal("📖 You read the moment. +4 bonus.", result.TellReadMessage);
        }

        [Fact]
        public async Task AC4_TellReadBonusZeroOnNoMatch()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Hey"));
            llm.EnqueueTell(new Tell(StatType.Honesty, "Shares vulnerability"));
            // Player picks Charm, not Honesty
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Smooth"));
            llm.EnqueueTell(null);

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(0, result.TellReadBonus);
            Assert.Null(result.TellReadMessage);
        }

        // ================================================================
        // AC5: HasTellBonus set on matching options in StartTurnAsync
        // ================================================================

        [Fact]
        public async Task AC5_HasTellBonusSetOnMatchingOptions()
        {
            var llm = new TellTestLlm();
            llm.EnqueueOptions(new DialogueOption(StatType.Charm, "Setup"));
            llm.EnqueueTell(new Tell(StatType.SelfAwareness, "Goes silent"));
            llm.EnqueueOptions(
                new DialogueOption(StatType.SelfAwareness, "I notice"),
                new DialogueOption(StatType.Charm, "Hey"),
                new DialogueOption(StatType.SelfAwareness, "Read the room"),
                new DialogueOption(StatType.Wit, "Joke"));

            var dice = new FixedDice(5, 15, 5, 15, 5);
            var session = new GameSession(MakeProfile("P"), MakeProfile("O"), llm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var start = await session.StartTurnAsync();
            Assert.True(start.Options[0].HasTellBonus);   // SA matches
            Assert.False(start.Options[1].HasTellBonus);  // Charm doesn't
            Assert.True(start.Options[2].HasTellBonus);   // SA matches
            Assert.False(start.Options[3].HasTellBonus);  // Wit doesn't
        }

        // ================================================================
        // Helpers
        // ================================================================

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

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                if (_optionSets.Count > 0)
                    return Task.FromResult(_optionSets.Dequeue());
                return Task.FromResult(new[] { new DialogueOption(StatType.Charm, "Default") });
            }


            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, System.Threading.CancellationToken ct = default)
            {
                var tell = _tells.Count > 0 ? _tells.Dequeue() : null;
                return Task.FromResult(new DateeResponse("...", detectedTell: tell));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult(message);
        
        public System.Threading.Tasks.Task<string> ApplyFailureCorruptionAsync(string message, string instruction, Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
        {
            return System.Threading.Tasks.Task.FromResult(message);
        }
}
    }
}
