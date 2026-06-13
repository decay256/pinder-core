using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    // ---------------------------------------------------------------
    // GameSession trap taint wiring tests
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public class GameSessionTrapTaintTests
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

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// When a trap is active, GameSession should populate ActiveTrapInstructions
        /// on the DialogueContext during StartTurnAsync.
        /// </summary>
        [Fact]
        public async Task StartTurnAsync_WithActiveTrap_PassesInstructionsToDialogueContext()
        {
            // We need a trap to be active. To do this we need a roll that hits TropeTrap tier.
            // TropeTrap tier: miss by 6-9 from DC.
            // With allStats=2, DC=13+2=15. Effective mod=2, level bonus=0.
            // Roll of 4: total = 4+2+0=6, miss by 15-6=9 => TropeTrap tier.
            // Then next turn should have the trap active.

            var trapDef = new TrapDefinition(
                "charm_trap", StatType.Charm, TrapEffect.Disadvantage, 0, 3,
                "You become extremely awkward.", "Roll Charm DC 15", "Insult their mother.");

            var trapRegistry = new TestTrapRegistry();
            trapRegistry.Register(trapDef);

            var capturingLlm = new CapturingLlmAdapter();

            // Dice sequence:
            // Turn 1 StartTurnAsync: no ghost roll (interest=10, state=Interested)
            // Turn 1 ResolveTurnAsync: roll d20=4 (miss by 9 => TropeTrap tier activates trap)
            //   RollEngine rolls: d20=4
            //   TimingProfile delay: d20 for delay
            // Turn 2 StartTurnAsync: no ghost roll
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                4,   // Turn 1: roll d20 (miss by 9 = TropeTrap)
                10,  // Turn 1: timing delay
                20   // Turn 2: padding
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                capturingLlm, dice, trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // Turn 1: start + resolve (activates trap)
            await session.StartTurnAsync();

            // Choose option 0 (Charm) — this will miss and activate charm trap
            await session.ResolveTurnAsync(0);

            // Turn 2: start — should have active trap instructions
            // Need dice for potential ghost roll check
            dice.Enqueue(10); // non-ghost roll padding
            await session.StartTurnAsync();

            // The second StartTurnAsync call should have trap instructions
            Assert.Equal(2, capturingLlm.DialogueContexts.Count);
            var turn2Context = capturingLlm.DialogueContexts[1];

            Assert.NotNull(turn2Context.ActiveTrapInstructions);
            Assert.Single(turn2Context.ActiveTrapInstructions!);
            Assert.Equal("You become extremely awkward.", turn2Context.ActiveTrapInstructions![0]);
        }

        /// <summary>
        /// When no traps are active, ActiveTrapInstructions should be null.
        /// </summary>
        [Fact]
        public async Task StartTurnAsync_NoActiveTraps_InstructionsAreNull()
        {
            var capturingLlm = new CapturingLlmAdapter();
            // Interested state (interest=10), no ghost check needed.
            var dice = new FixedDice(5, 20); // just need one roll

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            var context = capturingLlm.DialogueContexts[0];
            Assert.Null(context.ActiveTrapInstructions);
        }

        /// <summary>
        /// The DateeContext should get trap instructions when a trap is active
        /// (from the same turn's resolution, after trap activation).
        /// #1125: the delivery LLM call (and DeliveryContext) were collapsed into
        /// the deterministic, non-LLM DeliveryOverlay commit step, so trap taint
        /// no longer flows through a DeliveryContext; the datee context (and the
        /// trap OVERLAY) remain the consumers. Assertion narrowed to DateeContext.
        /// </summary>
        [Fact]
        public async Task ResolveTurnAsync_WithPreExistingTrap_PassesInstructionsToDateeContext()
        {
            // Set up a trap that's already active by manually using TrapState
            // We'll use a trap registry that has a charm trap, and we need the trap to be
            // active from a prior TropeTrap roll.

            var trapDef = new TrapDefinition(
                "wit_trap", StatType.Wit, TrapEffect.StatPenalty, 2, 5,
                "Only speak in terrible puns.", "", "");

            var trapRegistry = new TestTrapRegistry();
            trapRegistry.Register(trapDef);

            var capturingLlm = new CapturingLlmAdapter();

            // Turn 1: We need Wit option chosen, roll that hits TropeTrap tier
            // With allStats=2: DC=15, roll of 4 => total=6, miss by 9 => TropeTrap
            // After trap activation, the trap is active for delivery and datee contexts
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                4,  // Turn 1 roll: TropeTrap on Wit
                10  // Turn 1 timing delay
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                capturingLlm, dice, trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            // Choose option 2 (Wit) — triggers TropeTrap tier, activates wit_trap
            await session.ResolveTurnAsync(2);

            // #1125: no DeliveryContext is built anymore.
            Assert.Empty(capturingLlm.DeliveryContexts);

            // Check datee context had trap instructions (trap was activated during
            // RollEngine.Resolve; AdvanceTurn runs AFTER the datee call (#692), so
            // the trap is still at full duration during this turn's datee context).
            Assert.Single(capturingLlm.DateeContexts);
            var oppCtx = capturingLlm.DateeContexts[0];
            Assert.NotNull(oppCtx.ActiveTrapInstructions);
            Assert.Contains("Only speak in terrible puns.", oppCtx.ActiveTrapInstructions!);
        }

        /// <summary>
        /// When no traps are active, delivery and datee contexts should have null instructions.
        /// </summary>
        [Fact]
        public async Task ResolveTurnAsync_NoTraps_InstructionsAreNull()
        {
            var capturingLlm = new CapturingLlmAdapter();
            // High roll = success, no trap activation
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                20, // Turn 1 roll: natural 20 = success
                10  // timing delay
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Datee"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // #1125: no DeliveryContext is built; assert on the surviving datee context.
            Assert.Empty(capturingLlm.DeliveryContexts);
            var oppCtx = capturingLlm.DateeContexts[0];
            Assert.Null(oppCtx.ActiveTrapInstructions);
        }
    }
}
