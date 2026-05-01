using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Rolls;

namespace Pinder.Core.Tests.Conversation
{
    /// <summary>
    /// Issue #788 — fast-gameplay-readiness check.
    ///
    /// <para>
    /// Locks the contract that the (now-stateless) <see cref="IStatefulLlmAdapter"/>
    /// can serve concurrent calls without context bleed: three simultaneous
    /// calls into the same adapter instance, each carrying their own history
    /// list, return responses whose context never interleaves with the others.
    /// </para>
    ///
    /// <para>
    /// Before #788 this test would have failed: the adapter held
    /// <c>_opponentHistory</c> as a private field, so two concurrent calls
    /// would race-mutate it and each would see the other's appended
    /// user/assistant turns. After #788 the adapter is stateless and the
    /// engine owns the history list \u2014 calls are independent.
    /// </para>
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue788_StatelessAdapterConcurrencyTests
    {
        /// <summary>
        /// Stateless test adapter: echoes back the history it received as the
        /// response text, so the test can prove which history the adapter
        /// observed on each concurrent call.
        /// </summary>
        private sealed class EchoStatelessAdapter : IStatefulLlmAdapter
        {
            // Counts concurrent in-flight calls — proves they actually overlap.
            private int _inFlight;
            public int MaxInFlightObserved { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
                => Task.FromResult(System.Array.Empty<DialogueOption>());
            public Task<string> DeliverMessageAsync(DeliveryContext context, CancellationToken ct = default)
                => Task.FromResult(string.Empty);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context, CancellationToken ct = default)
                => Task.FromResult(new OpponentResponse(string.Empty));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult(string.Empty);
            public Task<string> ApplyHorninessOverlayAsync(string m, string i, string? oc = null, string? ad = null, CancellationToken ct = default) => Task.FromResult(m);
            public Task<string> ApplyShadowCorruptionAsync(string m, string i, ShadowStatType s, string? ad = null, CancellationToken ct = default) => Task.FromResult(m);
            public Task<string> ApplyTrapOverlayAsync(string m, string i, string n, string? oc = null, string? ad = null, CancellationToken ct = default) => Task.FromResult(m);

            public async Task<StatefulOpponentResult> GetOpponentResponseAsync(
                OpponentContext context,
                IReadOnlyList<ConversationMessage> history,
                CancellationToken cancellationToken = default)
            {
                int now = Interlocked.Increment(ref _inFlight);
                lock (this)
                {
                    if (now > MaxInFlightObserved) MaxInFlightObserved = now;
                }
                try
                {
                    // Yield to encourage actual interleaving across calls.
                    await Task.Yield();
                    await Task.Delay(5, cancellationToken).ConfigureAwait(false);

                    // Echo the history we observed, joined with markers so the
                    // test can grep what each call saw.
                    string echoed = string.Join(
                        " | ",
                        history.Select(h => $"{h.Role}:{h.Content}"));
                    var response = new OpponentResponse($"ECHO[{echoed}]");
                    var entries = new ConversationMessage[]
                    {
                        ConversationMessage.User($"call-{context.PlayerName}"),
                        ConversationMessage.Assistant(response.MessageText),
                    };
                    return new StatefulOpponentResult(response, entries);
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }
        }

        private static OpponentContext MakeContext(string playerLabel) =>
            new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: System.Array.Empty<(string, string)>(),
                opponentLastMessage: string.Empty,
                activeTraps: System.Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: string.Empty,
                interestBefore: 12,
                interestAfter: 12,
                responseDelayMinutes: 0.0,
                playerName: playerLabel);

        [Fact]
        public async Task ThreeConcurrentCalls_EachWithOwnHistory_ReturnNonInterleavedContext()
        {
            var adapter = new EchoStatelessAdapter();

            // Three callers, each with their OWN distinct history list.
            // If the adapter were storing history internally, the calls would
            // see each other's history mixed in. Because it doesn't, each
            // call's response should reflect only its own input history.
            var historyA = new[] { ConversationMessage.User("uA"), ConversationMessage.Assistant("aA") };
            var historyB = new[] { ConversationMessage.User("uB"), ConversationMessage.Assistant("aB") };
            var historyC = new[] { ConversationMessage.User("uC"), ConversationMessage.Assistant("aC") };

            // Fire all three concurrently.
            var taskA = adapter.GetOpponentResponseAsync(MakeContext("A"), historyA);
            var taskB = adapter.GetOpponentResponseAsync(MakeContext("B"), historyB);
            var taskC = adapter.GetOpponentResponseAsync(MakeContext("C"), historyC);

            var results = await Task.WhenAll(taskA, taskB, taskC).ConfigureAwait(false);

            // Sanity: the three calls genuinely overlapped (so this isn't a
            // serial-execution false-positive).
            Assert.True(adapter.MaxInFlightObserved >= 2,
                $"Expected concurrent execution; max in-flight was {adapter.MaxInFlightObserved}.");

            // Each call's response echoes ONLY its own history — no bleed.
            Assert.Contains("user:uA | assistant:aA", results[0].Response.MessageText);
            Assert.DoesNotContain("uB", results[0].Response.MessageText);
            Assert.DoesNotContain("uC", results[0].Response.MessageText);

            Assert.Contains("user:uB | assistant:aB", results[1].Response.MessageText);
            Assert.DoesNotContain("uA", results[1].Response.MessageText);
            Assert.DoesNotContain("uC", results[1].Response.MessageText);

            Assert.Contains("user:uC | assistant:aC", results[2].Response.MessageText);
            Assert.DoesNotContain("uA", results[2].Response.MessageText);
            Assert.DoesNotContain("uB", results[2].Response.MessageText);
        }

        // Static-shape lock: no public mutable instance field on PinderLlmAdapter
        // (or its sibling adapters) should match the old opponent-session names
        // (_opponentHistory, _opponentSession, _opponentSystemPrompt). If a
        // regression re-introduces them, this catches it before runtime tests.
        [Fact]
        public void PinderLlmAdapter_HasNoOpponentSessionFields()
        {
            var adapterType = typeof(Pinder.LlmAdapters.PinderLlmAdapter);
            var anthropicType = typeof(Pinder.LlmAdapters.Anthropic.AnthropicLlmAdapter);
            var openAiType = typeof(Pinder.LlmAdapters.OpenAi.OpenAiLlmAdapter);

            string[] forbidden = { "_opponentHistory", "_opponentSession", "_opponentSystemPrompt" };

            foreach (var t in new[] { adapterType, anthropicType, openAiType })
            {
                foreach (var name in forbidden)
                {
                    var field = t.GetField(name,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);
                    Assert.True(field == null,
                        $"{t.FullName} still has forbidden opponent-session field '{name}'. " +
                        "Per #788, opponent conversation state lives on GameSession, not the adapter.");
                }
            }
        }
    }
}
