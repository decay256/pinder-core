using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I3 — for a fixture turn with deterministic dice and deterministic
    /// canned LLM responses, the (system_prompt, user_prompt, raw_response, phase)
    /// tuples produced by the engine are byte-deterministic across runs.
    ///
    /// <para>
    /// Captures what the production audit log (in pinder-web's
    /// <c>SnapshotRecordingLlmTransport</c>) would record as the per-turn
    /// LLM-exchange envelope. The pinder-core engine never references the
    /// recorder type by name; the contract being locked is "the engine's
    /// <c>ILlmTransport</c> traffic for a given fixture is byte-stable".
    /// </para>
    ///
    /// <para>
    /// Why two-run determinism is the right oracle: a snapshot file in this
    /// suite would couple the test to incidental prompt-builder formatting
    /// that other PRs may legitimately tune. Two-run equivalence locks the
    /// invariant we actually care about — the engine is a pure function of
    /// (dice seed, transport responses, character profiles, turn input) —
    /// without freezing prompt copy.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I3_AuditDeterminism
    {
        // I3.1 — repeated runs of the SAME fixture produce byte-identical exchanges.
        [Fact]
        public async Task RepeatedRuns_ProduceByteIdenticalExchangeSequence()
        {
            var run1 = await ExecuteFixtureRunAsync();
            var run2 = await ExecuteFixtureRunAsync();

            Assert.Equal(run1.Count, run2.Count);
            for (int i = 0; i < run1.Count; i++)
            {
                Assert.Equal(run1[i].Phase, run2[i].Phase);
                Assert.Equal(run1[i].SystemPrompt, run2[i].SystemPrompt);
                Assert.Equal(run1[i].UserMessage, run2[i].UserMessage);
                Assert.Equal(run1[i].Response, run2[i].Response);
                Assert.Equal(run1[i].Temperature, run2[i].Temperature);
                Assert.Equal(run1[i].MaxTokens, run2[i].MaxTokens);
            }
        }

        // I3.2 — phase ordering on a single happy-path turn is exactly the canonical
        // sequence: dialogue_options → delivery → opponent_response.
        [Fact]
        public async Task SingleTurn_PhaseOrder_IsExactlyOptionsThenDeliveryThenOpponent()
        {
            var exchanges = await ExecuteFixtureRunAsync();
            var phases = exchanges.Select(e => e.Phase).ToArray();

            // The fixture (no shadow ≥1, no horniness check fires, no traps,
            // steering rng seeded to fail) collapses to the minimal three-call
            // sequence. Any new always-on phase would land here and fail this
            // test, prompting an explicit decision instead of a silent leak.
            Assert.Equal(
                new[] { LlmPhase.DialogueOptions, LlmPhase.Delivery, LlmPhase.OpponentResponse },
                phases);
        }

        // I3.3 — the SHA-256 hash of the per-exchange (phase, system, user, response)
        // serialization is identical across runs. This is a stricter, byte-level
        // version of I3.1 and gives us a one-line forensic signature when something
        // drifts: the hash will change and the test failure message includes both.
        [Fact]
        public async Task ExchangeSequence_Sha256Signature_IsStableAcrossRuns()
        {
            var run1Sig = HashExchanges(await ExecuteFixtureRunAsync());
            var run2Sig = HashExchanges(await ExecuteFixtureRunAsync());

            Assert.Equal(run1Sig, run2Sig);
        }

        // I3.4 — the recorded sequence has non-empty audit content for every call:
        // every exchange has a non-null system prompt, a non-empty user message
        // (we don't allow silent empty payloads to slip through into the log).
        [Fact]
        public async Task EveryRecordedExchange_HasNonEmptySystemAndUserPayload()
        {
            var exchanges = await ExecuteFixtureRunAsync();
            Assert.NotEmpty(exchanges);
            for (int i = 0; i < exchanges.Count; i++)
            {
                Assert.False(string.IsNullOrEmpty(exchanges[i].SystemPrompt),
                    $"Exchange {i} ({exchanges[i].Phase}) has empty system prompt.");
                Assert.False(string.IsNullOrEmpty(exchanges[i].UserMessage),
                    $"Exchange {i} ({exchanges[i].Phase}) has empty user message.");
                Assert.NotNull(exchanges[i].Phase);
            }
        }

        // ── Fixture executor ──────────────────────────────────────────────

        private static async Task<IReadOnlyList<RecordingLlmTransport.LlmExchange>> ExecuteFixtureRunAsync()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport.QueueOpponent(Phase0Fixtures.CannedOpponent);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig(steeringSeed: 12345));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            return transport.Exchanges;
        }

        private static string HashExchanges(IReadOnlyList<RecordingLlmTransport.LlmExchange> exchanges)
        {
            var sb = new StringBuilder();
            foreach (var ex in exchanges)
            {
                sb.Append(ex.Phase).Append('\u0001');
                sb.Append(ex.SystemPrompt).Append('\u0001');
                sb.Append(ex.UserMessage).Append('\u0001');
                sb.Append(ex.Response).Append('\u0001');
                sb.Append(ex.Temperature.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\u0001');
                sb.Append(ex.MaxTokens).Append('\u0002');
            }
            using var sha = SHA256.Create();
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hashBytes);
        }
    }
}
