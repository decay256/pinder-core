using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression: a freshly-activated trap must still be visible to the datee
    /// LLM context on the turn it activates. Before #692, AdvanceTurn was
    /// called before delivery, expiring short-duration traps immediately.
    /// #1125: the delivery LLM call (and DeliveryContext) were collapsed into the
    /// deterministic, non-LLM DeliveryOverlay commit step, so this now asserts
    /// trap visibility on the surviving DateeContext only.
    ///
    /// Per #371 (W2a) every trap is now fixed at 3 turns regardless of the
    /// definition's DurationTurns; these tests use definition.DurationTurns=1 to
    /// document the legacy data shape but TrapState.Activate clamps to 3 turns.
    /// </summary>
    [Trait("Category", "Core")]
    public sealed class TrapDuration1RegressionTests
    {
        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, allStats },
                    { StatType.Rizz, allStats },
                    { StatType.Honesty, allStats },
                    { StatType.Chaos, allStats },
                    { StatType.Wit, allStats },
                    { StatType.SelfAwareness, allStats }
                },
                new Dictionary<ShadowStatType, int>());
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: MakeStatBlock(),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public async Task Duration1Trap_VisibleInDateeContext()
        {
            // Duration=1 trap (like Cringe in production data)
            var trapDef = new TrapDefinition(
                "cringe_trap", StatType.Charm, TrapEffect.Disadvantage, 0, 1,
                "You become extremely awkward and self-undermining.", "SA vs DC 12", "");

            var trapRegistry = new TestTrapRegistry();
            trapRegistry.Register(trapDef);

            var capturingLlm = new CapturingLlmAdapter();

            // Roll d20=4: total=6, DC=15, miss by 9 => TropeTrap tier
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                4,  // Turn 1 roll: TropeTrap on Charm
                10  // Turn 1 timing delay
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                capturingLlm, dice, trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // option 0 = Charm

            // #1125: no delivery LLM call / DeliveryContext exists anymore — the
            // commit step is the deterministic, non-LLM DeliveryOverlay. Nothing
            // to assert here (no captured delivery contexts to inspect).

            // Datee context must see the trap
            var oppCtx = capturingLlm.DateeContexts[0];
            Assert.NotNull(oppCtx.ActiveTrapInstructions);
            Assert.Contains("You become extremely awkward and self-undermining.",
                oppCtx.ActiveTrapInstructions!);
        }

        // The previous Duration1Trap_ExpiresBeforeNextTurnDialogue test asserted
        // that a duration_turns=1 trap expired after one AdvanceTurn. Under
        // #371 (W2a) every trap is fixed at 3 turns regardless of the definition.
        // The 3-turn lifecycle is covered by
        // <see cref="TrapStateHasActiveTests.AdvanceTurn_ExpiresAfterThreeTurns"/>
        // and end-to-end by the persistence-path engine tests in
        // <see cref="TrapPipelineW2aTests"/>.
    }
}
