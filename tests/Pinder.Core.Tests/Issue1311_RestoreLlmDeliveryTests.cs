using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Contract and unit tests for issue #1311: restoring config-driven LLM failure corruption prompts.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1311_RestoreLlmDeliveryTests
    {
        [Fact]
        public async Task ApplyFailureCorruption_InvokedWhenFailedAndInstructionConfigured()
        {
            // 1. Setup GameSession with a mock LLM adapter
            var llm = new MockLlmAdapter1311();
            
            // Dice: 1 (horniness roll), 1 (Nat 1 d20 for turn, guaranteed failure), 50 (timing)
            var dice = new FixedDice(1, 1, 50);

            var statInstructions = new MockStatDeliveryInstructions("REWRITE WITH DISASTER STYLE");
            var activeArchetype = new ActiveArchetype("The Peacock", "Speak boastfully.", 10);

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: statInstructions
            );

            var player = MakeProfile("Sable", activeArchetype: activeArchetype);
            var datee = MakeProfile("Brick");

            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            // 2. Start turn and resolve picking the first option
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // 3. Verify it's a failed roll
            Assert.False(result.Roll.IsSuccess);

            // 4. Verify that ApplyFailureCorruptionAsync was invoked with the correct arguments
            Assert.True(llm.ApplyFailureCorruptionCalled, "ApplyFailureCorruptionAsync should have been called on the LLM adapter.");
            Assert.Equal("REWRITE WITH DISASTER STYLE", llm.CapturedInstruction);
            Assert.Equal(StatType.Charm, llm.CapturedStat);
            Assert.Equal(result.Roll.Tier, llm.CapturedTier);
            Assert.Equal(activeArchetype.Directive, llm.CapturedArchetypeDirective);

            // 5. Verify that the LLM returned value is used as the delivered message
            Assert.Equal("Corrupted by LLM!", result.DeliveredMessage);
        }

        [Fact]
        public async Task ApplyFailureCorruption_FallsBackToDeliveryOverlay_WhenLlmThrows()
        {
            // 1. Setup GameSession with a mock LLM adapter configured to throw
            var llm = new MockLlmAdapter1311
            {
                ExceptionToThrow = new TimeoutException("LLM timeout")
            };
            
            // Dice: 1 (horniness), 1 (Nat 1 d20), 50 (timing)
            var dice = new FixedDice(1, 1, 50);

            var statInstructions = new MockStatDeliveryInstructions("REWRITE WITH DISASTER STYLE");
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: statInstructions
            );

            var player = MakeProfile("Sable");
            var datee = MakeProfile("Brick");

            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // 2. Verify that it's a failed roll and falls back to deterministic overlay
            Assert.False(result.Roll.IsSuccess);
            Assert.True(llm.ApplyFailureCorruptionCalled, "ApplyFailureCorruptionAsync should be called and throw.");
            
            // Should fall back to DeliveryOverlay.Apply (meaning it does not throw an exception out of the session,
            // and instead delivers a deterministically degraded string).
            string expectedDeterministic = DeliveryOverlay.Apply("Test Option", result.Roll.Tier, result.Roll.MissMargin, StatType.Charm);
            expectedDeterministic = Pinder.Core.Text.MarkdownSanitizer.Strip(expectedDeterministic);
            Assert.Equal(expectedDeterministic, result.DeliveredMessage);
        }

        [Fact]
        public async Task ApplyFailureCorruption_FallsBackToDeliveryOverlay_WhenNoInstructionConfigured()
        {
            // 1. Setup GameSession with a mock LLM adapter but no failure instructions configured (returns null)
            var llm = new MockLlmAdapter1311();
            
            // Dice: 1 (horniness), 1 (Nat 1 d20), 50 (timing)
            var dice = new FixedDice(1, 1, 50);

            // Stat delivery instructions return null
            var statInstructions = new MockStatDeliveryInstructions(null);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: statInstructions
            );

            var player = MakeProfile("Sable");
            var datee = MakeProfile("Brick");

            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // 2. Verify that ApplyFailureCorruptionAsync is NOT called and falls back to deterministic overlay
            Assert.False(result.Roll.IsSuccess);
            Assert.False(llm.ApplyFailureCorruptionCalled, "ApplyFailureCorruptionAsync should not be called when instruction is null.");

            string expectedDeterministic = DeliveryOverlay.Apply("Test Option", result.Roll.Tier, result.Roll.MissMargin, StatType.Charm);
            expectedDeterministic = Pinder.Core.Text.MarkdownSanitizer.Strip(expectedDeterministic);
            Assert.Equal(expectedDeterministic, result.DeliveredMessage);
        }

        [Fact]
        public async Task ApplyFailureCorruption_ThrowsException_WhenLlmThrowsNonRetryableException()
        {
            // 1. Setup GameSession with a mock LLM adapter configured to throw a non-retryable exception
            var llm = new MockLlmAdapter1311 { ExceptionToThrow = new InvalidOperationException("Non-retryable validation error") };
            
            var dice = new FixedDice(1, 1, 50);
            var statInstructions = new MockStatDeliveryInstructions("REWRITE WITH DISASTER STYLE");
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: statInstructions
            );

            var player = MakeProfile("Sable");
            var datee = MakeProfile("Brick");

            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();

            // 2. Resolve turn should propagate the non-retryable exception out of the session
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));
        }

        [Fact]
        public async Task ApplyFailureCorruption_FallsBackToDeliveryOverlay_WhenLlmThrowsRetryableException()
        {
            // 1. Setup GameSession with a mock LLM adapter configured to throw a retryable exception
            var llm = new MockLlmAdapter1311 { ExceptionToThrow = new LlmTransportException("Rate limit exceeded!", LlmFailureKind.RateLimited) };
            
            var dice = new FixedDice(1, 1, 50);
            var statInstructions = new MockStatDeliveryInstructions("REWRITE WITH DISASTER STYLE");
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: statInstructions
            );

            var player = MakeProfile("Sable");
            var datee = MakeProfile("Brick");

            var session = new GameSession(player, datee, llm, dice, new NullTrapRegistry(), config);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // 2. Verify that it fell back to deterministic overlay instead of throwing
            Assert.False(result.Roll.IsSuccess);
            string expectedDeterministic = DeliveryOverlay.Apply("Test Option", result.Roll.Tier, result.Roll.MissMargin, StatType.Charm);
            expectedDeterministic = Pinder.Core.Text.MarkdownSanitizer.Strip(expectedDeterministic);
            Assert.Equal(expectedDeterministic, result.DeliveredMessage);
        }

        [Fact]
        public async Task ApplyFailureCorruption_PropagatesRawHttpFailureFromMisbehavingAdapter()
        {
            var llm = new MockLlmAdapter1311
            {
                ExceptionToThrow = new System.Net.Http.HttpRequestException("status 429 rate limit")
            };
            var dice = new FixedDice(1, 1, 50);
            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                statDeliveryInstructions: new MockStatDeliveryInstructions("REWRITE WITH DISASTER STYLE"));
            var session = new GameSession(
                MakeProfile("Sable"),
                MakeProfile("Brick"),
                llm,
                dice,
                new NullTrapRegistry(),
                config);

            await session.StartTurnAsync();

            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => session.ResolveTurnAsync(0));
        }

        // ======================== Helpers & Mocks ========================

        private static CharacterProfile MakeProfile(string name, int allStats = 2, ActiveArchetype? activeArchetype = null)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1,
                activeArchetype: activeArchetype,
                psychiatricDiagnosis: ValidDiagnosis(),
                backstory: TestHelpers.MakeBackstory(),
                stakeLines: TestHelpers.MakeStakeLines()
            );
        }

        private static IReadOnlyDictionary<string, string> ValidDiagnosis()
        {
            return new Dictionary<string, string>
            {
                ["derived_feeling"] = "curious",
                ["defense_reaction"] = "guarded",
            };
        }

        private class MockStatDeliveryInstructions : IStatDeliveryInstructionProvider
        {
            private readonly string? _instruction;

            public MockStatDeliveryInstructions(string? instruction)
            {
                _instruction = instruction;
            }

            public string? GetStatFailureInstruction(StatType stat, FailureTier tier)
            {
                return _instruction;
            }

            public string? GetHorninessOverlayInstruction(FailureTier tier) => null;

            public string? GetShadowCorruptionInstruction(ShadowStatType shadow, FailureTier tier) => null;
        }

        private class MockLlmAdapter1311 : ILlmAdapter
        {
            public bool ApplyFailureCorruptionCalled { get; set; }
            public string? CapturedMessage { get; set; }
            public string? CapturedInstruction { get; set; }
            public StatType? CapturedStat { get; set; }
            public FailureTier? CapturedTier { get; set; }
            public string? CapturedArchetypeDirective { get; set; }
            
            public string ReturnValue { get; set; } = "Corrupted by LLM!";
            public bool ShouldThrow { get; set; }
            public Exception? ExceptionToThrow { get; set; }

            public Task<string> ApplyFailureCorruptionAsync(
                string message, 
                string instruction, 
                StatType stat, 
                FailureTier tier, 
                string? archetypeDirective = null, 
                CancellationToken ct = default)
            {
                ApplyFailureCorruptionCalled = true;
                CapturedMessage = message;
                CapturedInstruction = instruction;
                CapturedStat = stat;
                CapturedTier = tier;
                CapturedArchetypeDirective = archetypeDirective;

                if (ExceptionToThrow != null)
                {
                    throw ExceptionToThrow;
                }

                if (ShouldThrow)
                {
                    throw new Exception("LLM failure!");
                }

                return Task.FromResult(ReturnValue);
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
            {
                return Task.FromResult(new[] { new DialogueOption(StatType.Charm, "Test Option") });
            }

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
            {
                return Task.FromResult(new DateeResponse("..."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
            {
                return Task.FromResult<string?>(null);
            }

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                return Task.FromResult(message);
            }

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
            {
                return Task.FromResult(message);
            }

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                return Task.FromResult(message);
            }
        }
    }
}
