using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class Issue1243_SuccessImprovementFailClosedTests
    {
        private const string DeterministicLine = "That sounds like a perfectly reasonable answer to me.";

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private sealed class DeliveryAdapterWithFailClosed : ILlmAdapter, IStatefulLlmAdapter
        {
            private readonly string _improvementResponse;
            public string? CapturedTrapMessage { get; private set; }

            public DeliveryAdapterWithFailClosed(string improvementResponse)
            {
                _improvementResponse = improvementResponse;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
                => Task.FromResult(new[] { new DialogueOption(StatType.Charm, DeterministicLine) });

            public Task<DateeResponse> GetDateeResponseAsync(DateeContext context, CancellationToken ct = default)
                => Task.FromResult(new DateeResponse("ok, go on..."));

            public Task<StatefulDateeResult> GetDateeResponseAsync(
                DateeContext context, IReadOnlyList<ConversationMessage> history, CancellationToken cancellationToken = default)
            {
                var resp = new DateeResponse("ok, go on...");
                var entries = new[] { ConversationMessage.User(string.Empty), ConversationMessage.Assistant("ok, go on...") };
                return Task.FromResult(new StatefulDateeResult(resp, entries));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> GetHorninessQuestionAsync(HorninessQuestionContext context, CancellationToken ct = default) => Task.FromResult("question?");
            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult("so... when are we actually doing this?");

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
            {
                CapturedTrapMessage = message;
                return Task.FromResult(message + " [TRAPPED]");
            }

            public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default)
            {
                return Task.FromResult(_improvementResponse);
            }
        }

        private sealed class AlwaysMinRandom : Random
        {
            public override int Next(int minValue, int maxValue) => minValue;
        }

        private static GameSession NewSession(
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry traps,
            Action<TextLayerNoopEvent>? onNoop = null,
            int playerStats = 5,
            int dateeStats = 0)
        {
            return new GameSession(
                MakeProfile("Player", playerStats), MakeProfile("Datee", dateeStats),
                llm, dice, traps,
                new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: new AlwaysMinRandom(), onTextLayerNoop: onNoop));
        }

        [Fact]
        public async Task A1_InvalidEngineStateSentinel_RejectsAndRetainsOriginalLine()
        {
            var dice = new FixedDice(5, 16, 50); // strong success
            var llm = new DeliveryAdapterWithFailClosed("Here are your options: \n INVALID_ENGINE_STATE detected.");
            var session = NewSession(llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(DeterministicLine, result.DeliveredMessage);
            Assert.DoesNotContain("INVALID_ENGINE_STATE", result.DeliveredMessage);
            // No success improvement textdiff is retained
            
            // To be precise: the original rule is "No 'Strong success' improvement TextDiff was added that contains the meta text".
            // If the layer is totally skipped, it's safer. Let's just assert it doesn't contain the diff.
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Strong success");
        }

        [Fact]
        public async Task A2_MetaAnalysisOutput_RejectsAndRetainsOriginalLine()
        {
            var dice = new FixedDice(5, 16, 50); // strong success
            var llm = new DeliveryAdapterWithFailClosed("I need to analyze the conversation. I need ENGINE_STATE. Now I need to generate OPTIONS for the PLAYER AVATAR.");
            var session = NewSession(llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(DeterministicLine, result.DeliveredMessage);
            Assert.DoesNotContain("generate OPTIONS", result.DeliveredMessage);
            Assert.DoesNotContain(result.TextDiffs, d => d.LayerName == "Strong success");
        }

        [Fact]
        public async Task A3_DegradedTelemetryObservable_WhenRejected()
        {
            var captured = new List<TextLayerNoopEvent>();
            var dice = new FixedDice(5, 16, 50); // strong success
            var llm = new DeliveryAdapterWithFailClosed("<ENGINE_STATE> INVALID_ENGINE_STATE");
            var session = NewSession(llm, dice, new NullTrapRegistry(), onNoop: evt => captured.Add(evt));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Contains(captured, e => e.Layer == "Success improvement");
        }

        [Fact]
        public async Task A4_ValidRewrite_AppliesNormally()
        {
            var dice = new FixedDice(5, 16, 50); // strong success
            var validRewrite = "That's a delightfully chaotic origin story.";
            var llm = new DeliveryAdapterWithFailClosed(validRewrite);
            var session = NewSession(llm, dice, new NullTrapRegistry());

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Contains(validRewrite, result.DeliveredMessage);
            Assert.Contains(result.TextDiffs, d => d.LayerName == "Strong success");
        }

        [Fact]
        public async Task A5_DownstreamOverlayReceivesOriginal_AfterRejection()
        {
            var trapRegistry = new TestTrapRegistry();
            var trapDef = new TrapDefinition("id", StatType.Charm, TrapEffect.Disadvantage, 0, 3, "AWKWARD_INSTRUCTION", "Roll Charm DC 15", "nat1 bonus");
            trapRegistry.Register(trapDef);

            var llm = new DeliveryAdapterWithFailClosed("INVALID_ENGINE_STATE - meta text");
            var captured = new List<TextLayerNoopEvent>();
            // Turn 1: roll 4 to miss DC 15 (with allStats=2, total=6, miss by 9 => TropeTrap tier)
            // Turn 2: roll 16 to strong success on DC 15 (total=18)
            var dice = new FixedDice(
                5,  // ctor horniness
                4,  // Turn 1 roll: troptrap miss
                10, // Turn 1 delay
                20, // Turn 2 roll: strong success
                50, 10, 10, 10, 10  // padding
            );

            var session = NewSession(llm, dice, trapRegistry, onNoop: evt => captured.Add(evt), playerStats: 2, dateeStats: 2);

            // Turn 1: Miss and trigger trap
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2: Success improvement fires, rejects, then trap applies
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0); // strong success with active trap

            Assert.True(result.Roll.IsSuccess);
            
            // Trap apply should have been called
            Assert.NotNull(llm.CapturedTrapMessage);
            
            // Should equal the deterministic line, not the meta text
            Assert.Equal(DeterministicLine, llm.CapturedTrapMessage);
            Assert.DoesNotContain("INVALID_ENGINE_STATE", llm.CapturedTrapMessage);
        }
    }
}
