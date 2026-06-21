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
    /// <summary>
    /// Regression coverage for #1125 — "Collapse delivery into a commit step;
    /// options become full sendable lines".
    ///
    /// <para>
    /// The delivery LLM call (<c>DeliverMessageAsync</c> / <c>BuildDeliveryPrompt</c>)
    /// was removed as a creative-generation surface. Options now carry the FULL
    /// sendable line, and "delivery" is a deterministic, non-LLM commit/overlay
    /// step (<see cref="DeliveryOverlay"/>). These tests pin the behaviour that
    /// must survive the refactor:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     After a degrade/corrupt roll the COMMITTED line differs from the
    ///     picked line by exactly the deterministic overlay transform
    ///     (corruption parity).
    ///   </description></item>
    ///   <item><description>
    ///     Ephemeral artifacts (raw option text, the pre-overlay picked line)
    ///     never persist into <see cref="GameSession.ConversationHistory"/> —
    ///     only the committed line does (clean-history rule).
    ///   </description></item>
    ///   <item><description>
    ///     No <c>delivery</c>/<c>DeliverMessageAsync</c> LLM call fires during a
    ///     full turn (and no <c>"delivery"</c> prompt-trace is compiled).
    ///   </description></item>
    /// </list>
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue1125_CollapseDeliveryTests
    {
        private const string PickedLine =
            "Honestly, your taste in obscure synthpop is the most attractive thing I have seen all week.";

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
        /// LLM adapter that emits a single FULL-text option (so we control the
        /// picked line). Overlays (trap/shadow/horniness) and steering are no-ops
        /// here so the only transform that can touch the committed line is the
        /// deterministic <see cref="DeliveryOverlay"/>.
        ///
        /// <para>
        /// #1125/#1137: the old delivery LLM surface (a <c>DeliverMessageAsync</c>
        /// overload) was removed from the adapter contract entirely, so there is
        /// no longer a method this adapter could implement to "forbid" — the
        /// guarantee that no creative delivery call fires is now a COMPILE-TIME
        /// property of <see cref="ILlmAdapter"/>/<see cref="IStatefulLlmAdapter"/>,
        /// not a runtime throw. The remaining regression coverage asserts that no
        /// <c>"delivery"</c> prompt-trace is compiled during a full turn.
        /// </para>
        /// </summary>
        private sealed class DeliveryForbiddenAdapter : ILlmAdapter, IStatefulLlmAdapter
        {
            private readonly string _optionText;

            public DeliveryForbiddenAdapter(string optionText)
            {
                _optionText = optionText;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context, CancellationToken ct = default)
                => Task.FromResult(new[] { new DialogueOption(StatType.Charm, _optionText) });

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

        public Task<string> GetSuccessImprovementAsync(SuccessImprovementContext context, CancellationToken ct = default) => Task.FromResult(context.DeliveredMessage);

            public Task<string> GetSteeringQuestionAsync(SteeringContext context, CancellationToken ct = default)
                => Task.FromResult("so... when are we actually doing this?");

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? dateeContext = null, string? archetypeDirective = null, CancellationToken ct = default)
                => Task.FromResult(message);
        }

        private static GameSession NewSession(ILlmAdapter llm, IDiceRoller dice)
            => new GameSession(
                MakeProfile("Gerald"), MakeProfile("Velvet"),
                llm, dice, new NullTrapRegistry(),
                // #1125: pin the steering RNG to a guaranteed MISS (die roll 1)
                // so the optional steering question never appends to the picked
                // line — keeping these commit-step assertions deterministic.
                new GameSessionConfig(clock: TestHelpers.MakeClock(), steeringRng: new AlwaysMinRandom()));

        // Steering uses a 1d20 via RandomDiceRollerAdapter(_steeringRng); returning
        // the minimum guarantees the steering roll misses its DC, so no question
        // is appended and the committed line is purely the overlay of the pick.
        private sealed class AlwaysMinRandom : Random
        {
            public override int Next(int minValue, int maxValue) => minValue;
            public override int Next(int maxValue) => 0;
            public override int Next() => 0;
        }

        // ── Regression 1: corruption parity ─────────────────────────────────

        // After a degrade/corrupt roll, the COMMITTED line equals
        // DeliveryOverlay.Apply(pickedLine, tier, margin) and differs from the
        // raw picked line — proving the deterministic overlay still mutates the
        // committed line now that the delivery LLM call is gone.
        [Fact]
        public async Task DegradeRoll_CommittedLine_IsDeterministicOverlayOfPickedLine()
        {
            // stat mod +2, DC = 13 + 2 = 15. d20 = 3 → total 5, miss by 10 → Catastrophe.
            var dice = new FixedDice(
                5,   // ctor: horniness roll
                3,   // main d20 → fail, miss 10 → Catastrophe
                50); // d100 timing
            var llm = new DeliveryForbiddenAdapter(PickedLine);
            var session = NewSession(llm, dice);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // The picked line is the full option text (no steering/overlay fired).
            // The expected committed line is the deterministic overlay output.
            string expected = DeliveryOverlay.Apply(PickedLine, FailureTier.Catastrophe, missMargin: 10);

            Assert.False(result.Roll.IsSuccess);
            Assert.Equal(FailureTier.Catastrophe, result.Roll.Tier);
            Assert.Equal(expected, result.DeliveredMessage);
            // Parity: the committed line must actually differ from the raw pick.
            Assert.NotEqual(PickedLine, result.DeliveredMessage);
        }

        // Success commits the picked line verbatim (no creative rewrite).
        [Fact]
        public async Task SuccessRoll_CommitsPickedLineVerbatim()
        {
            // stat mod +2, DC = 15. d20 = 18 → total 20 ≥ 15 → success.
            var dice = new FixedDice(5, 18, 50);
            var llm = new DeliveryForbiddenAdapter(PickedLine);
            var session = NewSession(llm, dice);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.True(result.Roll.IsSuccess);
            Assert.Equal(PickedLine, result.DeliveredMessage);
        }

        // ── Regression 2: ephemeral artifacts never persist ─────────────────

        // The persisted ConversationHistory holds ONLY committed lines. The raw
        // picked option text (pre-overlay) must NOT appear as its own entry.
        [Fact]
        public async Task PersistedHistory_ContainsOnlyCommittedLine_NotRawPickedOption()
        {
            var dice = new FixedDice(5, 3, 50); // Catastrophe (as above)
            var llm = new DeliveryForbiddenAdapter(PickedLine);
            var session = NewSession(llm, dice);

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            var history = session.ConversationHistory;

            // Exactly one player (Gerald) entry was committed this turn, and it is
            // the overlaid/committed line — never the raw pre-overlay pick.
            var geraldEntries = history.Where(e => e.Sender == "Gerald").ToList();
            Assert.Single(geraldEntries);
            Assert.Equal(result.DeliveredMessage, geraldEntries[0].Text);

            // The raw picked option text was degraded, so it must be absent from
            // EVERY persisted entry (no ephemeral pre-overlay line leaked in).
            Assert.DoesNotContain(history, e => e.Text == PickedLine);

            // And the avatar/option-generation session is ephemeral: nothing from
            // option generation is written back to the persisted avatar history.
            Assert.Empty(session.AvatarHistory);
        }

        // ── Regression 3: no delivery LLM call / prompt-trace fires ──────────

        // A full turn must not compile a "delivery" creative prompt-trace. (The
        // delivery LLM call itself no longer exists on the adapter contract, so
        // "no delivery call fires" is enforced at compile time — #1137.)
        [Fact]
        public async Task FullTurn_FiresNoDeliveryLlmCall_NorDeliveryPromptTrace()
        {
            InMemoryPromptTraceService.Instance.Clear();

            var dice = new FixedDice(5, 18, 50); // success path
            var llm = new DeliveryForbiddenAdapter(PickedLine);
            var session = NewSession(llm, dice);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // No "delivery" creative prompt is compiled — the delivery LLM call
            // was collapsed into the deterministic DeliveryOverlay commit step.
            Assert.Null(InMemoryPromptTraceService.Instance.GetLastTrace("delivery"));
        }
    }
}
