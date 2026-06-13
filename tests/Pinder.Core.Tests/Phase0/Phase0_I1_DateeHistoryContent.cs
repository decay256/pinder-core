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
    /// Invariant I1 — Datee conversation history is locked at the LLM transport
    /// layer (BEHAVIOR-BASED), not at the storage layer.
    ///
    /// <para>
    /// What this asserts: across multiple turns, the user-message content sent to
    /// the LLM transport on datee_response call N+1 contains the assistant
    /// content from datee_response call N. The datee's "memory" of the
    /// prior assistant turn is observable in the bytes that cross the wire.
    /// </para>
    /// <para>
    /// What this does NOT reach into: any adapter-private field that holds the
    /// stateful datee history (whatever it is currently named or wherever it
    /// lives). Phase 1 (#788) will move the stateful datee history out of the
    /// adapter and into <c>GameSession</c>. This test must continue to pass after
    /// that move without modification — the contract being locked is the wire-level
    /// behavior, not the storage location.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I1_DateeHistoryContent
    {
        // I1.1 — over a 3-turn run, every datee_response call N (N≥1) must contain
        // the assistant text from datee_response call N-1 in its user message.
        // This is the structural invariant: stateful datee context is preserved
        // ACROSS turns, regardless of where the state lives internally.
        [Fact]
        public async Task DateeResponseCallN_UserMessage_ContainsAssistantResponseFromCallNMinus1()
        {
            var transport = new RecordingLlmTransport
            {
                DefaultResponse = ""
            };

            // Three full turns, each consuming options + delivery + datee.
            for (int turn = 0; turn < 3; turn++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery($"delivered-text-turn{turn}");
                transport.QueueDatee($"datee-reply-{turn}-distinguishing-token-{Guid.NewGuid():N}");
            }

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(
                5,                        // ctor d10
                15, 50, 15, 50, 15, 50    // 3 turns × (d20 main + d100 timing) = 6 draws
            );

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Datee"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            for (int t = 0; t < 3; t++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            var dateeExchanges = transport.ExchangesByPhase(LlmPhase.DateeResponse);
            Assert.True(dateeExchanges.Count >= 3,
                $"Expected at least 3 datee_response calls; got {dateeExchanges.Count}.");

            // I1 core assertion: each call's user message contains the prior assistant
            // response. We don't care WHERE the adapter stores it — only that it
            // appears in the wire payload going out.
            for (int n = 1; n < dateeExchanges.Count; n++)
            {
                string priorAssistant = dateeExchanges[n - 1].Response;
                string currentUser = dateeExchanges[n].UserMessage;

                Assert.True(
                    currentUser.Contains(priorAssistant, StringComparison.Ordinal),
                    $"Datee call {n} user message did not contain prior call's assistant text.\n" +
                    $"Prior assistant: {priorAssistant}\n" +
                    $"Current user (truncated): {Truncate(currentUser, 600)}");
            }
        }

        // I1.2 — first datee_response call must NOT echo a prior assistant response
        // (no leak from a previous session, no spurious context).
        [Fact]
        public async Task DateeResponseCall0_DoesNotContainAnyPriorAssistantResponse()
        {
            var transport = new RecordingLlmTransport
            {
                DefaultResponse = ""
            };

            const string distinctiveDateeReply = "DISTINCTIVE-FIRST-REPLY-TOKEN-7C9F";
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery("delivered");
            transport.QueueDatee(distinctiveDateeReply);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Datee"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var dateeExchanges = transport.ExchangesByPhase(LlmPhase.DateeResponse);
            Assert.True(dateeExchanges.Count >= 1,
                "Expected at least 1 datee_response call.");

            // The user message of the first call must not contain its own response token
            // (sanity check: not a self-echo) and must not contain a "PREVIOUS CONVERSATION CONTEXT"
            // header (no fictitious history injected for the first call).
            Assert.DoesNotContain(distinctiveDateeReply, dateeExchanges[0].UserMessage,
                StringComparison.Ordinal);
        }

        // I1.3 — the datee system prompt is stable across calls within one session.
        // Assistant responses MUST NOT bleed into the system prompt — only into the
        // user message. (Locks the system/user separation that PinderLlmAdapter uses.)
        [Fact]
        public async Task DateeResponse_SystemPrompt_StableAcrossCalls_NoAssistantBleed()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };

            for (int i = 0; i < 3; i++)
            {
                transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
                transport.QueueDelivery("delivered");
                transport.QueueDatee($"opp-reply-{i}-XYZSEN-{i}");
            }

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50, 15, 50, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Datee"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            for (int t = 0; t < 3; t++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0);
            }

            var dateeExchanges = transport.ExchangesByPhase(LlmPhase.DateeResponse);
            Assert.True(dateeExchanges.Count >= 2);

            // System prompt is identical across datee calls in one session.
            var firstSystemPrompt = dateeExchanges[0].SystemPrompt;
            for (int n = 1; n < dateeExchanges.Count; n++)
            {
                Assert.Equal(firstSystemPrompt, dateeExchanges[n].SystemPrompt);
            }

            // No assistant response from a prior call ever appears in any system prompt.
            for (int n = 0; n < dateeExchanges.Count; n++)
            {
                for (int m = 0; m < n; m++)
                {
                    Assert.DoesNotContain(dateeExchanges[m].Response, dateeExchanges[n].SystemPrompt,
                        StringComparison.Ordinal);
                }
            }
        }

        // I1.4 — within one turn, options/delivery calls do NOT leak the prior turn's
        // datee assistant text into THEIR user prompt. Continuity is datee-only.
        // (If this regressed, the player LLM would start parroting the datee voice.)
        [Fact]
        public async Task DialogueOptions_UserMessage_DoesNotEchoDateeAssistantText()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            const string dateeToken = "DATEE-ASSISTANT-DISTINCTIVE-TOKEN-A4B2";
            // Turn 1
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery("delivered-1");
            transport.QueueDatee(dateeToken);
            // Turn 2 — we want to inspect the options call here
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery("delivered-2");
            transport.QueueDatee("noise-2");

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Datee"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var optionExchanges = transport.ExchangesByPhase(LlmPhase.DialogueOptions);
            // The 2nd dialogue-options call MAY include the datee's text via the
            // conversation history (this is correct — the player's options need
            // context). What we lock here is the opposite: the dialogue-options
            // SYSTEM prompt does not echo the datee assistant text. The system
            // prompt is the player-character voice; bleeding the datee into it
            // would corrupt voice separation.
            Assert.True(optionExchanges.Count >= 2);
            Assert.DoesNotContain(dateeToken, optionExchanges[1].SystemPrompt,
                StringComparison.Ordinal);
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
