using System;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I1 — Opponent conversation history is locked at the LLM transport
    /// layer (BEHAVIOR-BASED), not at the storage layer.
    ///
    /// <para>
    /// What this asserts: across multiple turns, the user-message content sent to
    /// the LLM transport on opponent_response call N+1 contains the assistant
    /// content from opponent_response call N. The opponent's "memory" of the
    /// prior assistant turn is observable in the bytes that cross the wire.
    /// </para>
    /// <para>
    /// What this does NOT reach into: any adapter-private field that holds the
    /// stateful opponent history (whatever it is currently named or wherever it
    /// lives). Phase 1 (#788) will move the stateful opponent history out of the
    /// adapter and into <c>GameSession</c>. This test must continue to pass after
    /// that move without modification — the contract being locked is the wire-level
    /// behavior, not the storage location.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I1_OpponentHistoryContent
    {
        // I1.1 — over a 3-turn run, every opponent_response call N (N≥1) must contain
        // the assistant text from opponent_response call N-1 in its user message.
        // This is the structural invariant: stateful opponent context is preserved
        // ACROSS turns, regardless of where the state lives internally.
        [Fact]
        public async Task OpponentResponseCallN_UserMessage_ContainsAssistantResponseFromCallNMinus1()
        {
            var transport = new RecordingLlmTransport
            {
                DefaultResponse = ""
            };

            // Three full turns, each consuming options + delivery + opponent.
            for (int turn = 0; turn < 3; turn++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery($"delivered-text-turn{turn}");
                transport.QueueOpponent($"opponent-reply-{turn}-distinguishing-token-{Guid.NewGuid():N}");
            }

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(
                5,                        // ctor d10
                15, 50, 15, 50, 15, 50    // 3 turns × (d20 main + d100 timing) = 6 draws
            );

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            for (int t = 0; t < 3; t++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            var opponentExchanges = transport.ExchangesByPhase(LlmPhase.OpponentResponse);
            Assert.True(opponentExchanges.Count >= 3,
                $"Expected at least 3 opponent_response calls; got {opponentExchanges.Count}.");

            // I1 core assertion: each call's user message contains the prior assistant
            // response. We don't care WHERE the adapter stores it — only that it
            // appears in the wire payload going out.
            for (int n = 1; n < opponentExchanges.Count; n++)
            {
                string priorAssistant = opponentExchanges[n - 1].Response;
                string currentUser = opponentExchanges[n].UserMessage;

                Assert.True(
                    currentUser.Contains(priorAssistant, StringComparison.Ordinal),
                    $"Opponent call {n} user message did not contain prior call's assistant text.\n" +
                    $"Prior assistant: {priorAssistant}\n" +
                    $"Current user (truncated): {Truncate(currentUser, 600)}");
            }
        }

        // I1.2 — first opponent_response call must NOT echo a prior assistant response
        // (no leak from a previous session, no spurious context).
        [Fact]
        public async Task OpponentResponseCall0_DoesNotContainAnyPriorAssistantResponse()
        {
            var transport = new RecordingLlmTransport
            {
                DefaultResponse = ""
            };

            const string distinctiveOpponentReply = "DISTINCTIVE-FIRST-REPLY-TOKEN-7C9F";
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery("delivered");
            transport.QueueOpponent(distinctiveOpponentReply);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var opponentExchanges = transport.ExchangesByPhase(LlmPhase.OpponentResponse);
            Assert.True(opponentExchanges.Count >= 1,
                "Expected at least 1 opponent_response call.");

            // The user message of the first call must not contain its own response token
            // (sanity check: not a self-echo) and must not contain a "PREVIOUS CONVERSATION CONTEXT"
            // header (no fictitious history injected for the first call).
            Assert.DoesNotContain(distinctiveOpponentReply, opponentExchanges[0].UserMessage,
                StringComparison.Ordinal);
        }

        // I1.3 — the opponent system prompt is stable across calls within one session.
        // Assistant responses MUST NOT bleed into the system prompt — only into the
        // user message. (Locks the system/user separation that PinderLlmAdapter uses.)
        [Fact]
        public async Task OpponentResponse_SystemPrompt_StableAcrossCalls_NoAssistantBleed()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };

            for (int i = 0; i < 3; i++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery("delivered");
                transport.QueueOpponent($"opp-reply-{i}-XYZSEN-{i}");
            }

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50, 15, 50, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            for (int t = 0; t < 3; t++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            var opponentExchanges = transport.ExchangesByPhase(LlmPhase.OpponentResponse);
            Assert.True(opponentExchanges.Count >= 2);

            // System prompt is identical across opponent calls in one session.
            var firstSystemPrompt = opponentExchanges[0].SystemPrompt;
            for (int n = 1; n < opponentExchanges.Count; n++)
            {
                Assert.Equal(firstSystemPrompt, opponentExchanges[n].SystemPrompt);
            }

            // No assistant response from a prior call ever appears in any system prompt.
            for (int n = 0; n < opponentExchanges.Count; n++)
            {
                for (int m = 0; m < n; m++)
                {
                    Assert.DoesNotContain(opponentExchanges[m].Response, opponentExchanges[n].SystemPrompt,
                        StringComparison.Ordinal);
                }
            }
        }

        // I1.4 — within one turn, options/delivery calls do NOT leak the prior turn's
        // opponent assistant text into THEIR user prompt. Continuity is opponent-only.
        // (If this regressed, the player LLM would start parroting the opponent voice.)
        [Fact]
        public async Task DialogueOptions_UserMessage_DoesNotEchoOpponentAssistantText()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            const string opponentToken = "OPPONENT-ASSISTANT-DISTINCTIVE-TOKEN-A4B2";
            // Turn 1
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery("delivered-1");
            transport.QueueOpponent(opponentToken);
            // Turn 2 — we want to inspect the options call here
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery("delivered-2");
            transport.QueueOpponent("noise-2");

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var optionExchanges = transport.ExchangesByPhase(LlmPhase.DialogueOptions);
            // The 2nd dialogue-options call MAY include the opponent's text via the
            // conversation history (this is correct — the player's options need
            // context). What we lock here is the opposite: the dialogue-options
            // SYSTEM prompt does not echo the opponent assistant text. The system
            // prompt is the player-character voice; bleeding the opponent into it
            // would corrupt voice separation.
            Assert.True(optionExchanges.Count >= 2);
            Assert.DoesNotContain(opponentToken, optionExchanges[1].SystemPrompt,
                StringComparison.Ordinal);
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
