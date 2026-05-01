using System;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// Invariant I2 — every random draw site reachable from <c>ResolveTurnAsync</c>
    /// is statically bounded for a given option choice. We assert this by running
    /// a fixture turn against three different option choices and verifying that the
    /// <see cref="PlaybackDiceRoller"/> is exactly drained — no leftover values, no
    /// exhaustion exception.
    ///
    /// <para>
    /// This is the architectural canary for Phase 2 (#789, "Refactor D: pre-roll
    /// dice / deterministic RNG"). If a future PR introduces a new draw site
    /// whose count depends on intermediate LLM output, the static budget here
    /// will fall short (engine throws inside RollEngine) or overshoot
    /// (PlaybackDiceRoller still has values in queue post-resolve). Either
    /// failure mode is loud.
    /// </para>
    ///
    /// <para>
    /// Enumerated draw sites (file:line refs are documented in the PR body
    /// and in <c>docs/development/regression-pins-787.md</c>):
    /// <list type="bullet">
    ///   <item>Constructor — <c>GameSession.cs:165</c> — <c>dice.Roll(10)</c> for the
    ///         session horniness roll.</item>
    ///   <item><c>StartTurnAsync</c> — <c>GameSession.cs:417</c> —
    ///         <c>dice.Roll(4)</c> for ghost trigger, ONLY when interest state
    ///         is <c>Bored</c>. Not present in this fixture (Lukewarm interest).</item>
    ///   <item><c>RollEngine.Resolve</c> — <c>RollEngine.cs:52</c> —
    ///         <c>dice.Roll(20)</c> main d20.</item>
    ///   <item><c>RollEngine.Resolve</c> — <c>RollEngine.cs:53</c> —
    ///         <c>dice.Roll(20)</c> second d20 only when
    ///         <c>hasAdvantage \|\| hasDisadvantage</c>. Not present in
    ///         this fixture (Lukewarm = no advantage, no disadvantage).</item>
    ///   <item><c>TimingProfile.ComputeDelay</c> — <c>TimingProfile.cs:53</c> —
    ///         <c>dice.Roll(100)</c> for opponent reply variance.</item>
    /// </list>
    /// Steering / shadow / horniness rolls use a SEPARATE <c>Random</c>
    /// instance owned by <c>SteeringEngine</c> / <c>HorninessEngine</c> and do
    /// NOT consume game dice; they're explicitly out of scope for this budget.
    /// </para>
    ///
    /// <para>
    /// Three option choices = three <c>StatType</c> picks against the same fresh
    /// fixture. The expected per-turn budget is:
    /// <c>1 d10 (ctor) + 1 d20 (main roll) + 1 d100 (timing) = 3 draws</c>
    /// for a Lukewarm-interest, no-advantage turn against a Null trap registry.
    /// </para>
    /// </summary>
    [Trait("Category", "Phase0")]
    public class Phase0_I2_DiceBudget
    {
        // I2.1 — happy-path turn against option index 0. Budget exactly drained.
        [Fact]
        public async Task ResolveTurn_OptionIndex0_ConsumesExactlyExpectedDraws()
        {
            await AssertExactBudgetForOptionIndex(optionIndex: 0);
        }

        // I2.2 — option index 1. Budget exactly drained.
        [Fact]
        public async Task ResolveTurn_OptionIndex1_ConsumesExactlyExpectedDraws()
        {
            await AssertExactBudgetForOptionIndex(optionIndex: 1);
        }

        // I2.3 — option index 2. Budget exactly drained.
        [Fact]
        public async Task ResolveTurn_OptionIndex2_ConsumesExactlyExpectedDraws()
        {
            await AssertExactBudgetForOptionIndex(optionIndex: 2);
        }

        // I2.4 — over-allocation surfaces as PlaybackDiceRoller NOT drained
        // (i.e. our oracle is sensitive: if we prepare 4 draws when 3 are needed,
        // IsDrained must return false). Demonstrates the test's diagnostic value.
        [Fact]
        public async Task PlaybackDiceRoller_OverAllocated_IsNotDrained()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport.QueueOpponent(Phase0Fixtures.CannedOpponent);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            // Over-allocate: prepare 4 draws for a turn that only consumes 3.
            var dice = new PlaybackDiceRoller(5, 15, 50, 99 /* extra */);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.False(dice.IsDrained,
                "Expected PlaybackDiceRoller to report NOT drained when over-allocated; oracle is unsound otherwise.");
            Assert.Equal(1, dice.Remaining);
        }

        // I2.5 — under-allocation surfaces loudly as InvalidOperationException
        // from PlaybackDiceRoller.Roll. Demonstrates the test's failure mode.
        [Fact]
        public async Task PlaybackDiceRoller_UnderAllocated_ThrowsExhaustionException()
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport.QueueOpponent(Phase0Fixtures.CannedOpponent);

            var adapter = Phase0Fixtures.MakeAdapter(transport);
            // Under-allocate: prepare only 2 draws. Constructor consumes 1; the d20
            // main roll consumes another; the d100 timing call will throw.
            var dice = new PlaybackDiceRoller(5, 15);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.ResolveTurnAsync(0));
        }

        // ── Shared assertion ──────────────────────────────────────────────

        private static async Task AssertExactBudgetForOptionIndex(int optionIndex)
        {
            var transport = new RecordingLlmTransport { DefaultResponse = "" };
            transport.QueueDialogueOptions(Phase0Fixtures.CannedDialogueOptions);
            transport.QueueDelivery(Phase0Fixtures.CannedDelivery);
            transport.QueueOpponent(Phase0Fixtures.CannedOpponent);

            var adapter = Phase0Fixtures.MakeAdapter(transport);

            // Exactly the bounded budget for one Lukewarm-interest, no-advantage,
            // no-disadvantage, no-trap turn:
            //   1× d10  ctor (horniness)   = 1 draw
            //   1× d20  main roll          = 1 draw
            //   1× d100 timing variance    = 1 draw
            //   ──────────────────────────────────
            //   total                      = 3 draws
            var dice = new PlaybackDiceRoller(5, 15, 50);

            var session = new GameSession(
                Phase0Fixtures.MakeProfile("Player"),
                Phase0Fixtures.MakeProfile("Opponent"),
                adapter, dice, new NullTrapRegistry(), Phase0Fixtures.MakeConfig());

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(optionIndex);

            Assert.True(dice.IsDrained,
                $"PlaybackDiceRoller not exactly drained for optionIndex={optionIndex}: " +
                $"prepared={dice.Prepared}, consumed={dice.Consumed}, remaining={dice.Remaining}.");
            Assert.Equal(3, dice.Consumed);
            // Diagnostic: assert the three draws hit the expected sides in the expected order.
            Assert.Equal(10, dice.Trace[0].Sides);  // horniness
            Assert.Equal(20, dice.Trace[1].Sides);  // main d20
            Assert.Equal(100, dice.Trace[2].Sides); // timing d100
        }
    }
}
