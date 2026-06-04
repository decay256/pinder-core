using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.Core.Tests.Prompts
{
    [Trait("Category", "Core")]
    public class Issue811OverlayLengthBudgetTests
    {
        private static StatBlock MakeStats()
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, 5 },
                { StatType.Rizz, 5 },
                { StatType.Honesty, 5 },
                { StatType.Chaos, 5 },
                { StatType.Wit, 5 },
                { StatType.SelfAwareness, 5 }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 },
                { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 },
                { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 },
                { ShadowStatType.Overthinking, 0 }
            };
            return new StatBlock(stats, shadow);
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                MakeStats(),
                $"You are {name}.",
                name,
                new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public async Task Pipeline_LengthClamp_TruncatesToSentenceBoundaries_WithoutExceedingLimit()
        {
            // Arrange
            var player = MakeProfile("Sable");
            var opponent = MakeProfile("Brick");
            
            // Limit to 20 words
            int maxWords = 20;

            // Base message is 16 words.
            // Steering question is 10 words.
            // Horniness overlay is 10 words.
            // Total words before clamp = 36 words.
            var stubLlm = new StubLlm(
                baseMessage: "This is a base dialogue option chosen by the player which is quite long and interesting.", // 16 words
                steeringQuestion: "Are you free tonight to grab some pizza and hang out?", // 10 words
                horninessOverlay: " This is extra horny text that is appended by the overlay applier." // 11 words
            );

            // Roll d10 for horniness = 1, then subsequent rolls for d20/etc = 20 (success)
            var dice = new FixedDice(1, 20, 20, 20);
            var steeringRng = new FixedRandom(20); // steering success

            var config = new GameSessionConfig(
                clock: TestHelpers.MakeClock(),
                steeringRng: steeringRng,
                maxDeliveryWords: maxWords,
                statDeliveryInstructions: new StubStatDeliveryInstructions() // activate horniness lookup
            );

            var session = new GameSession(player, opponent, stubLlm, dice, new NullTrapRegistry(), config);

            // Act
            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Assert
            string delivered = result.DeliveredMessage;
            Assert.NotNull(delivered);

            var words = delivered.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.True(words.Length <= maxWords, $"Delivered message word count ({words.Length}) exceeded limit ({maxWords}). Message: {delivered}");

            // Verify sentence boundary truncation
            // The last char should be a punctuation sentence-ender (. or ? or !)
            Assert.True(delivered.EndsWith(".") || delivered.EndsWith("?") || delivered.EndsWith("!"), $"Delivered message must end with sentence punctuation. Got: '{delivered}'");

            // Verify a "LengthClamp" text diff was recorded
            Assert.NotNull(result.TextDiffs);
            Assert.Contains(result.TextDiffs, d => d.LayerName == "LengthClamp");
        }

        [Fact]
        public void Aggregator_Conflict_Or_Absence_Of_Minimum80Words()
        {
            // Assert that "minimum 80 words per message, no exceptions" does not exist in any production texting_style_fragment
            string repoRoot = "";
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "data")) &&
                    Directory.Exists(Path.Combine(dir, "src")))
                {
                    repoRoot = dir;
                    break;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }

            Assert.NotEmpty(repoRoot);

            string anatomyJson = File.ReadAllText(Path.Combine(repoRoot, "data", "anatomy", "anatomy-parameters.json"));
            string itemsJson = File.ReadAllText(Path.Combine(repoRoot, "data", "items", "starter-items.json"));

            Assert.DoesNotContain("minimum 80 words per message, no exceptions", anatomyJson, StringComparison.Ordinal);
            Assert.DoesNotContain("minimum 80 words per message, no exceptions", itemsJson, StringComparison.Ordinal);
        }

        private class StubStatDeliveryInstructions
        {
            public string GetHorninessOverlayInstruction(FailureTier tier)
            {
                return "Append extra horniness.";
            }
        }

        private sealed class StubLlm : ILlmAdapter, IStatefulLlmAdapter
        {
            private readonly string _baseMessage;
            private readonly string _steeringQuestion;
            private readonly string _horninessOverlay;

            public StubLlm(string baseMessage, string steeringQuestion, string horninessOverlay)
            {
                _baseMessage = baseMessage;
                _steeringQuestion = steeringQuestion;
                _horninessOverlay = horninessOverlay;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, _baseMessage),
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<string> GetSteeringQuestionAsync(SteeringContext context, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(_steeringQuestion);
            }

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
            {
                return Task.FromResult(message + _horninessOverlay);
            }

            public Task<StatefulOpponentResult> GetOpponentResponseAsync(
                OpponentContext context,
                System.Collections.Generic.IReadOnlyList<ConversationMessage> history,
                System.Threading.CancellationToken ct = default)
                => Task.FromResult(new StatefulOpponentResult(
                    new OpponentResponse("..."),
                    new ConversationMessage[]
                    {
                        ConversationMessage.User(string.Empty),
                        ConversationMessage.Assistant("..."),
                    }));

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult(new OpponentResponse("..."));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, System.Threading.CancellationToken ct = default)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult(message);
        }

        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) { _values = new Queue<int>(values); }
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class FixedRandom : Random
        {
            private readonly Queue<int> _values;
            public FixedRandom(params int[] values) { _values = new Queue<int>(values); }
            public override int Next(int minValue, int maxValue) => _values.Count > 0 ? _values.Dequeue() : minValue;
        }

        private sealed class NullTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
