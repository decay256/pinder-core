using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Data;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    // ---------------------------------------------------------------
    // Capturing LLM adapter: records all contexts passed to it
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public sealed class CapturingLlmAdapter : ILlmAdapter
    {
        public List<DialogueContext> DialogueContexts { get; } = new List<DialogueContext>();
        public List<DeliveryContext> DeliveryContexts { get; } = new List<DeliveryContext>();
        public List<OpponentContext> OpponentContexts { get; } = new List<OpponentContext>();

        public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
        {
            DialogueContexts.Add(context);
            var options = new[]
            {
                new DialogueOption(StatType.Charm, "Hey, you come here often?"),
                new DialogueOption(StatType.Honesty, "I have to be real with you..."),
                new DialogueOption(StatType.Wit, "Did you know that penguins propose with pebbles?"),
                new DialogueOption(StatType.Chaos, "I once ate a whole pizza in a bouncy castle.")
            };
            return Task.FromResult(options);
        }

        public Task<string> DeliverMessageAsync(DeliveryContext context)
        {
            DeliveryContexts.Add(context);
            string message = context.Outcome == FailureTier.None
                ? context.ChosenOption.IntendedText
                : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
            return Task.FromResult(message);
        }

        public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
        {
            OpponentContexts.Add(context);
            return Task.FromResult(new OpponentResponse("..."));
        }

        public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
        {
            return Task.FromResult<string?>(null);
        }
        public System.Threading.Tasks.Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
            public System.Threading.Tasks.Task<string> ApplyShadowCorruptionAsync(string message, string instruction, Pinder.Core.Stats.ShadowStatType shadow, string? archetypeDirective = null) => System.Threading.Tasks.Task.FromResult(message);
    }

    // ---------------------------------------------------------------
    // Trap registry that returns a specific trap with LLM instruction
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public sealed class TestTrapRegistry : ITrapRegistry
    {
        private readonly Dictionary<StatType, TrapDefinition> _traps =
            new Dictionary<StatType, TrapDefinition>();

        public void Register(TrapDefinition trap) => _traps[trap.Stat] = trap;

        public TrapDefinition? GetTrap(StatType stat)
        {
            _traps.TryGetValue(stat, out var trap);
            return trap;
        }

        public string? GetLlmInstruction(StatType stat)
        {
            _traps.TryGetValue(stat, out var trap);
            return trap?.LlmInstruction;
        }
    }

    // ---------------------------------------------------------------
    // JsonTrapRepository tests
    // ---------------------------------------------------------------
    [Trait("Category", "Core")]
    public class JsonTrapRepositoryTests
    {
        private const string SampleJson = @"[
            {
                ""id"": ""charm_trap"",
                ""stat"": ""charm"",
                ""effect"": ""disadvantage"",
                ""effect_value"": 0,
                ""duration_turns"": 3,
                ""llm_instruction"": ""Your character becomes extremely awkward and stutters uncontrollably."",
                ""clear_method"": ""Roll Charm DC 15"",
                ""nat1_bonus"": ""You accidentally insult their mother.""
            },
            {
                ""id"": ""wit_trap"",
                ""stat"": ""wit"",
                ""effect"": ""stat_penalty"",
                ""effect_value"": 2,
                ""duration_turns"": 2,
                ""llm_instruction"": ""Your character can only speak in terrible puns."",
                ""clear_method"": """",
                ""nat1_bonus"": """"
            }
        ]";

        [Fact]
        public void Constructor_ParsesTrapsCorrectly()
        {
            var repo = new JsonTrapRepository(SampleJson);

            var charmTrap = repo.GetTrap(StatType.Charm);
            Assert.NotNull(charmTrap);
            Assert.Equal("charm_trap", charmTrap!.Id);
            Assert.Equal(StatType.Charm, charmTrap.Stat);
            Assert.Equal(TrapEffect.Disadvantage, charmTrap.Effect);
            Assert.Equal(0, charmTrap.EffectValue);
            Assert.Equal(3, charmTrap.DurationTurns);
            Assert.Equal("Your character becomes extremely awkward and stutters uncontrollably.", charmTrap.LlmInstruction);
            Assert.Equal("Roll Charm DC 15", charmTrap.ClearMethod);
        }

        [Fact]
        public void GetLlmInstruction_ReturnsCorrectInstruction()
        {
            var repo = new JsonTrapRepository(SampleJson);

            Assert.Equal(
                "Your character can only speak in terrible puns.",
                repo.GetLlmInstruction(StatType.Wit));
        }

        [Fact]
        public void GetLlmInstruction_ReturnsNull_ForMissingStat()
        {
            var repo = new JsonTrapRepository(SampleJson);
            Assert.Null(repo.GetLlmInstruction(StatType.Rizz));
        }

        [Fact]
        public void GetTrap_ReturnsNull_ForMissingStat()
        {
            var repo = new JsonTrapRepository(SampleJson);
            Assert.Null(repo.GetTrap(StatType.Honesty));
        }

        [Fact]
        public void GetAll_ReturnsAllTraps()
        {
            var repo = new JsonTrapRepository(SampleJson);
            var all = repo.GetAll().ToList();
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public void Constructor_WithCustomFiles_MergesTraps()
        {
            var customJson = @"[
                {
                    ""id"": ""custom_rizz_trap"",
                    ""stat"": ""rizz"",
                    ""effect"": ""opponent_dc_increase"",
                    ""effect_value"": 3,
                    ""duration_turns"": 4,
                    ""llm_instruction"": ""Your rizz has been tainted by custom trap."",
                    ""clear_method"": """",
                    ""nat1_bonus"": """"
                }
            ]";

            var repo = new JsonTrapRepository(SampleJson, new[] { customJson });

            Assert.NotNull(repo.GetTrap(StatType.Charm));
            Assert.NotNull(repo.GetTrap(StatType.Wit));
            Assert.NotNull(repo.GetTrap(StatType.Rizz));
            Assert.Equal("Your rizz has been tainted by custom trap.", repo.GetLlmInstruction(StatType.Rizz));
        }

        [Fact]
        public void Constructor_CustomOverridesPrimary()
        {
            var customJson = @"[
                {
                    ""id"": ""custom_charm_trap"",
                    ""stat"": ""charm"",
                    ""effect"": ""stat_penalty"",
                    ""effect_value"": 1,
                    ""duration_turns"": 5,
                    ""llm_instruction"": ""Custom charm instruction overrides default."",
                    ""clear_method"": """",
                    ""nat1_bonus"": """"
                }
            ]";

            var repo = new JsonTrapRepository(SampleJson, new[] { customJson });

            var charmTrap = repo.GetTrap(StatType.Charm);
            Assert.NotNull(charmTrap);
            Assert.Equal("custom_charm_trap", charmTrap!.Id);
            Assert.Equal("Custom charm instruction overrides default.", charmTrap.LlmInstruction);
        }

        [Fact]
        public void Constructor_ThrowsOnMissingId()
        {
            var badJson = @"[{ ""stat"": ""charm"", ""effect"": ""disadvantage"", ""llm_instruction"": ""test"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnUnknownStat()
        {
            var badJson = @"[{ ""id"": ""bad"", ""stat"": ""unknown"", ""effect"": ""disadvantage"", ""llm_instruction"": ""test"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnUnknownEffect()
        {
            var badJson = @"[{ ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""unknown"", ""llm_instruction"": ""test"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnMissingLlmInstruction()
        {
            var badJson = @"[{ ""id"": ""bad"", ""stat"": ""charm"", ""effect"": ""disadvantage"" }]";
            Assert.Throws<FormatException>(() => new JsonTrapRepository(badJson));
        }

        [Fact]
        public void Constructor_ThrowsOnNullJson()
        {
            Assert.Throws<ArgumentNullException>(() => new JsonTrapRepository(null!));
        }

        [Fact]
        public void Constructor_ThrowsOnNonArrayJson()
        {
            Assert.Throws<FormatException>(() => new JsonTrapRepository(@"{ ""key"": ""value"" }"));
        }

        [Fact]
        public void Constructor_ParsesAllEffectTypes()
        {
            var json = @"[
                { ""id"": ""t1"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""effect_value"": 0, ""duration_turns"": 1, ""llm_instruction"": ""i1"" },
                { ""id"": ""t2"", ""stat"": ""rizz"", ""effect"": ""stat_penalty"", ""effect_value"": 2, ""duration_turns"": 1, ""llm_instruction"": ""i2"" },
                { ""id"": ""t3"", ""stat"": ""wit"", ""effect"": ""opponent_dc_increase"", ""effect_value"": 3, ""duration_turns"": 1, ""llm_instruction"": ""i3"" }
            ]";

            var repo = new JsonTrapRepository(json);
            Assert.Equal(TrapEffect.Disadvantage, repo.GetTrap(StatType.Charm)!.Effect);
            Assert.Equal(TrapEffect.StatPenalty, repo.GetTrap(StatType.Rizz)!.Effect);
            Assert.Equal(TrapEffect.OpponentDCIncrease, repo.GetTrap(StatType.Wit)!.Effect);
        }

        [Fact]
        public void Constructor_ParsesAllStatTypes()
        {
            var json = @"[
                { ""id"": ""t1"", ""stat"": ""charm"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i1"" },
                { ""id"": ""t2"", ""stat"": ""rizz"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i2"" },
                { ""id"": ""t3"", ""stat"": ""honesty"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i3"" },
                { ""id"": ""t4"", ""stat"": ""chaos"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i4"" },
                { ""id"": ""t5"", ""stat"": ""wit"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i5"" },
                { ""id"": ""t6"", ""stat"": ""self_awareness"", ""effect"": ""disadvantage"", ""llm_instruction"": ""i6"" }
            ]";

            var repo = new JsonTrapRepository(json);
            Assert.NotNull(repo.GetTrap(StatType.Charm));
            Assert.NotNull(repo.GetTrap(StatType.Rizz));
            Assert.NotNull(repo.GetTrap(StatType.Honesty));
            Assert.NotNull(repo.GetTrap(StatType.Chaos));
            Assert.NotNull(repo.GetTrap(StatType.Wit));
            Assert.NotNull(repo.GetTrap(StatType.SelfAwareness));
        }
    }

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
                MakeProfile("Player"), MakeProfile("Opponent"),
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
                MakeProfile("Player"), MakeProfile("Opponent"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            var context = capturingLlm.DialogueContexts[0];
            Assert.Null(context.ActiveTrapInstructions);
        }

        /// <summary>
        /// DeliveryContext and OpponentContext should also get trap instructions
        /// when a trap is active (from the same turn's resolution, after trap activation
        /// but before delivery).
        /// </summary>
        [Fact]
        public async Task ResolveTurnAsync_WithPreExistingTrap_PassesInstructionsToDeliveryAndOpponentContexts()
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
            // After trap activation, the trap is active for delivery and opponent contexts
            var dice = new FixedDice(
                5,  // Constructor: horniness roll (1d10)
                4,  // Turn 1 roll: TropeTrap on Wit
                10  // Turn 1 timing delay
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                capturingLlm, dice, trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();

            // Choose option 2 (Wit) — triggers TropeTrap tier, activates wit_trap
            await session.ResolveTurnAsync(2);

            // Check delivery context had trap instructions
            // Note: trap was activated during RollEngine.Resolve. AdvanceTurn is now called
            // AFTER delivery + opponent LLM calls (#692), so the trap is still at full
            // duration (5) during this turn's delivery and opponent contexts.
            Assert.Single(capturingLlm.DeliveryContexts);
            var delivCtx = capturingLlm.DeliveryContexts[0];
            Assert.NotNull(delivCtx.ActiveTrapInstructions);
            Assert.Contains("Only speak in terrible puns.", delivCtx.ActiveTrapInstructions!);

            // Check opponent context had trap instructions
            Assert.Single(capturingLlm.OpponentContexts);
            var oppCtx = capturingLlm.OpponentContexts[0];
            Assert.NotNull(oppCtx.ActiveTrapInstructions);
            Assert.Contains("Only speak in terrible puns.", oppCtx.ActiveTrapInstructions!);
        }

        /// <summary>
        /// When no traps are active, delivery and opponent contexts should have null instructions.
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
                MakeProfile("Player"), MakeProfile("Opponent"),
                capturingLlm, dice, new NullTrapRegistry(), new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var delivCtx = capturingLlm.DeliveryContexts[0];
            Assert.Null(delivCtx.ActiveTrapInstructions);

            var oppCtx = capturingLlm.OpponentContexts[0];
            Assert.Null(oppCtx.ActiveTrapInstructions);
        }
    }

    /// <summary>
    /// Regression: duration-1 traps must still be visible to delivery and opponent
    /// LLM contexts on the turn they activate. Before #692, AdvanceTurn was called
    /// before delivery, expiring duration-1 traps immediately.
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
            return new CharacterProfile(
                stats: MakeStatBlock(),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        [Fact]
        public async Task Duration1Trap_VisibleInDeliveryAndOpponentContexts()
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
                MakeProfile("Player"), MakeProfile("Opponent"),
                capturingLlm, dice, trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // option 0 = Charm

            // Delivery context must see the trap
            var delivCtx = capturingLlm.DeliveryContexts[0];
            Assert.NotNull(delivCtx.ActiveTrapInstructions);
            Assert.Contains("You become extremely awkward and self-undermining.",
                delivCtx.ActiveTrapInstructions!);

            // Opponent context must see the trap
            var oppCtx = capturingLlm.OpponentContexts[0];
            Assert.NotNull(oppCtx.ActiveTrapInstructions);
            Assert.Contains("You become extremely awkward and self-undermining.",
                oppCtx.ActiveTrapInstructions!);
        }

        [Fact]
        public async Task Duration1Trap_ExpiresBeforeNextTurnDialogue()
        {
            // Duration=1: should be gone by next StartTurnAsync
            var trapDef = new TrapDefinition(
                "cringe_trap", StatType.Charm, TrapEffect.Disadvantage, 0, 1,
                "You become extremely awkward.", "SA vs DC 12", "");

            var trapRegistry = new TestTrapRegistry();
            trapRegistry.Register(trapDef);

            var capturingLlm = new CapturingLlmAdapter();

            var dice = new FixedDice(
                5,   // Constructor: horniness roll
                4,   // Turn 1: TropeTrap
                10,  // Turn 1: timing delay
                20   // Turn 2: padding
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                capturingLlm, dice, trapRegistry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // Turn 2
            dice.Enqueue(10);
            await session.StartTurnAsync();

            // Duration-1 trap should have expired after AdvanceTurn at end of turn 1
            var turn2Context = capturingLlm.DialogueContexts[1];
            Assert.Null(turn2Context.ActiveTrapInstructions);
        }
    }
}
