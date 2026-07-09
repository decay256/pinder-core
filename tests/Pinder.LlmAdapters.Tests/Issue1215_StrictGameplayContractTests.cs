using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Regression tests for the STRICT gameplay LLM contract (Issue #1215).
    /// </summary>
    public class Issue1215_StrictGameplayContractTests
    {
        // ── helpers ──────────────────────────────────────────────────────

        private static DialogueContext MakeDialogueContext(StatType[] availableStats)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("O", "Hi"), ("P", "Hey there") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerName: "P",
                dateeName: "O",
                currentTurn: 2,
                availableStats: availableStats
            );
        }

        private static DateeContext MakeDateeContext()
        {
            return new DateeContext(
                dateePrompt: "datee system prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 15,
                playerDeliveredMessage: "hello",
                interestBefore: 14,
                interestAfter: 15,
                responseDelayMinutes: 2.0,
                playerName: "Velvet",
                dateeName: "Sable"
            );
        }

        private static void AssertContractException(Exception ex, string phaseToken, string reasonToken)
        {
            Assert.NotNull(ex);
            var name = ex.GetType().Name;
            
            // The implementer will name it something like LlmContractException / GameplayContractException
            Assert.Contains("Contract", name, StringComparison.OrdinalIgnoreCase);
            
            // Exclude the missing GameDefinition / #1217 guard or generic null references
            Assert.NotEqual(typeof(ArgumentNullException), ex.GetType());
            Assert.NotEqual(typeof(InvalidOperationException), ex.GetType());
            Assert.DoesNotContain("GameDefinition", ex.Message, StringComparison.OrdinalIgnoreCase);

            var msg = ex.Message;
            Assert.NotNull(msg);
            
            // Check phaseToken leniently
            if (phaseToken == "dialogue" || phaseToken == "option")
            {
                Assert.True(
                    msg.Contains("dialogue", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("option", StringComparison.OrdinalIgnoreCase),
                    $"Message should contain 'dialogue' or 'option'. Actual: '{msg}'"
                );
            }
            else
            {
                Assert.Contains(phaseToken, msg, StringComparison.OrdinalIgnoreCase);
            }

            // Check reasonToken leniently
            Assert.True(
                msg.Contains("malformed", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("required", StringComparison.OrdinalIgnoreCase),
                $"Message should contain a reason like malformed/missing/empty/invalid/required. Actual: '{msg}'"
            );
        }

        private sealed class FixedResponseTransport : ILlmTransport
        {
            private readonly string _response;
            public FixedResponseTransport(string response) => _response = response;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(_response);
        }

        // ── tests ────────────────────────────────────────────────────────

        [Fact]
        public async Task Test1_DialogueOptions_EmptyProviderOutput_ThrowsContractException()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            var transport = new FixedResponseTransport("");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.GetDialogueOptionsAsync(context));
            AssertContractException(ex, "option", "empty");
        }

        [Fact]
        public async Task Test2_DialogueOptions_NoValidOptions_ThrowsContractException()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            var transport = new FixedResponseTransport("I refuse to give options.");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.GetDialogueOptionsAsync(context));
            AssertContractException(ex, "option", "malformed");
        }

        [Fact]
        public async Task Test3_DialogueOptions_PartialOptions_ThrowsContractException()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            var transport = new FixedResponseTransport("OPTION_1\n[STAT: CHARM]\n\"This is a valid option.\"");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.GetDialogueOptionsAsync(context));
            AssertContractException(ex, "option", "missing");
        }

        [Fact]
        public async Task Test4_DialogueOptions_InvalidStat_ThrowsContractException()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            var transport = new FixedResponseTransport(
                "OPTION_1\n[STAT: Bogus]\n\"This option has an invalid stat.\"\n\n" +
                "OPTION_2\n[STAT: CHARM]\n\"This is a valid option.\"\n\n" +
                "OPTION_3\n[STAT: RIZZ]\n\"This is another valid option.\""
            );
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.GetDialogueOptionsAsync(context));
            AssertContractException(ex, "option", "invalid");
        }

        [Fact]
        public async Task Test5_DialogueOptions_NoFabrication_NeverReturnsPlaceholder()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            var transport = new FixedResponseTransport("");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            try
            {
                var results = await adapter.GetDialogueOptionsAsync(context);
                foreach (var opt in results)
                {
                    Assert.DoesNotContain("Tell me more about you.", opt.IntendedText, StringComparison.OrdinalIgnoreCase);
                }
                Assert.Fail("The call succeeded with empty provider output, but it must throw a contract exception.");
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                AssertContractException(ex, "option", "empty");
            }
        }

        [Fact]
        public async Task Test6_DateeResponse_EmptyProviderOutput_ThrowsContractException()
        {
            var context = MakeDateeContext();
            var transport = new FixedResponseTransport("");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.GetDateeResponseAsync(context));
            AssertContractException(ex, "datee", "empty");
        }

        [Fact]
        public async Task Test7_DateeResponse_MalformedPresentSignals_ThrowsContractException()
        {
            var context = MakeDateeContext();
            var transport = new FixedResponseTransport(
                "This is a valid message.\n" +
                "[SIGNALS]\n" +
                "TELL: NotAStat (This is a malformed tell because NotAStat is not a stat)"
            );
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAnyAsync<Exception>(async () => await adapter.GetDateeResponseAsync(context));
            AssertContractException(ex, "datee", "malformed");
        }

        [Fact]
        public async Task Test7b_DateeResponse_SignalsWithoutResponseText_ThrowsContractException()
        {
            var context = MakeDateeContext();
            var transport = new FixedResponseTransport(
                "[SIGNALS]\n" +
                "TELL: Charm (she twirls her hair when nervous)"
            );
            LlmContractViolation? violation = null;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 1,
                OnLlmContractViolation = v => violation = v,
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var ex = await Assert.ThrowsAsync<LlmContractException>(async () => await adapter.GetDateeResponseAsync(context));

            Assert.Equal("datee_response", ex.Phase);
            Assert.Equal("malformed_signals", ex.Reason);
            Assert.Contains("missing_response_text", ex.Message);
            Assert.NotNull(violation);
            Assert.Equal("datee_response", violation!.Phase);
            Assert.Equal("malformed_signals", violation.Reason);
            Assert.Equal("StrictDateeResponseParser", violation.ParserName);
        }

        [Fact]
        public async Task Test8_DateeResponse_OptionalAbsentSignals_Succeeds()
        {
            var context = MakeDateeContext();
            const string expectedMessage = "This is a normal message without any signals block.";
            var transport = new FixedResponseTransport(expectedMessage);
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var result = await adapter.GetDateeResponseAsync(context);

            Assert.NotNull(result);
            Assert.Equal(expectedMessage, result.MessageText);
            Assert.Null(result.DetectedTell);
            Assert.Null(result.WeaknessWindow);
        }

        [Fact]
        public async Task Test9_DialogueOptions_ValidFullOutput_Succeeds()
        {
            var context = MakeDialogueContext(new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
            var transport = new FixedResponseTransport(
                "OPTION_1\n[STAT: CHARM]\n\"This is options 1 text.\"\n\n" +
                "OPTION_2\n[STAT: RIZZ]\n\"This is options 2 text.\"\n\n" +
                "OPTION_3\n[STAT: HONESTY]\n\"This is options 3 text.\""
            );
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var results = await adapter.GetDialogueOptionsAsync(context);

            Assert.NotNull(results);
            Assert.Equal(3, results.Length);

            Assert.Equal(StatType.Charm, results[0].Stat);
            Assert.Equal("This is options 1 text.", results[0].IntendedText);

            Assert.Equal(StatType.Rizz, results[1].Stat);
            Assert.Equal("This is options 2 text.", results[1].IntendedText);

            Assert.Equal(StatType.Honesty, results[2].Stat);
            Assert.Equal("This is options 3 text.", results[2].IntendedText);
        }
    }
}
