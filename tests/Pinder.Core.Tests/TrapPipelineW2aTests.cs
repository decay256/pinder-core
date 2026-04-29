using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// W2a (#371) trap-redesign engine tests.
    ///
    /// Mandatory acceptance scenarios:
    ///  1. Trap activates on turn N. Turn N+1 player picks NON-SA. Trap LLM call
    ///     fires (turn 2 of 3); `Trap (X)` text-diff layer added.
    ///  2. Trap activates on turn N. Turn N+1 player picks SA. Trap cleared at
    ///     start of ResolveTurnAsync; no Trap diff this turn.
    ///  3. SA picked, SA roll fails (Misfire). Old trap cleared. Spiral activated
    ///     as the new trap. This turn's diff is the failure-tier label, not
    ///     `Trap (Spiral)`. Spiral's TurnsRemaining == 3 going into turn N+1.
    ///  4. Turn N+1 (Spiral persistence): non-SA picked, roll succeeds. Trap LLM
    ///     call fires for Spiral; `Trap (Spiral)` diff added; TurnsRemaining=1
    ///     after AdvanceTurn.
    ///  5. Turn N+2 (Spiral persistence): non-SA, success. Trap LLM call fires;
    ///     TurnsRemaining=0 after AdvanceTurn; Spiral removed at end.
    ///  6. Turn N+1 fresh roll-failure activates a NEW trap while old trap is
    ///     mid-cycle. Old trap replaced. Roll-modification IS the new trap's
    ///     turn-1 taint. NO separate trap LLM call this turn.
    ///  7. Resume: trap state survives RestoreState (per now-folded #369).
    /// </summary>
    [Trait("Category", "Core")]
    public sealed class TrapPipelineW2aTests
    {
        // ──────────────────────────────────────────────────────────────────
        // Test plumbing
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// LLM adapter that:
        ///  * always returns four options spanning Charm / Honesty / Wit / SelfAwareness
        ///    (so SA disarm scenarios can be exercised).
        ///  * tags the delivered message with the failure tier so we can detect
        ///    whether a trap overlay actually rewrote the text.
        ///  * tags the trap-overlay output so the text-diff layer fires.
        /// </summary>
        private sealed class W2aTrapLlmAdapter : ILlmAdapter
        {
            public List<DialogueContext> DialogueContexts { get; } = new();
            public List<DeliveryContext> DeliveryContexts { get; } = new();
            public List<OpponentContext> OpponentContexts { get; } = new();
            public int TrapOverlayCalls { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                DialogueContexts.Add(context);
                var options = new[]
                {
                    new DialogueOption(StatType.Charm,         "Charm option"),
                    new DialogueOption(StatType.Honesty,       "Honesty option"),
                    new DialogueOption(StatType.Wit,           "Wit option"),
                    new DialogueOption(StatType.SelfAwareness, "SA option"),
                };
                return Task.FromResult(options);
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                DeliveryContexts.Add(context);
                // Tag the message with the tier so it is observably different
                // from the IntendedText (so a TextDiff is emitted on failure).
                string text = context.Outcome == FailureTier.None
                    ? context.ChosenOption.IntendedText
                    : $"[{context.Outcome}] {context.ChosenOption.IntendedText}";
                return Task.FromResult(text);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                OpponentContexts.Add(context);
                return Task.FromResult(new OpponentResponse("..."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);

            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null, string? archetypeDirective = null)
                => Task.FromResult(message);

            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow, string? archetypeDirective = null)
                => Task.FromResult(message);

            public Task<string> ApplyTrapOverlayAsync(string message, string trapInstruction, string trapName, string? opponentContext = null, string? archetypeDirective = null)
            {
                TrapOverlayCalls++;
                // Mark the message so a Trap (X) text-diff is emitted.
                return Task.FromResult($"[Trap:{trapName}] {message}");
            }
        }

        /// <summary>
        /// Trap registry that exposes one trap per stat. Stat → trap-name map matches
        /// the production data shape (Charm → Cringe, SA → Spiral, etc.).
        /// </summary>
        private sealed class StubTrapRegistry : ITrapRegistry
        {
            private readonly Dictionary<StatType, TrapDefinition> _traps = new();
            public StubTrapRegistry()
            {
                Register(StatType.Charm,         "cringe",      "Cringe");
                Register(StatType.Rizz,          "creep",       "Creep");
                Register(StatType.Honesty,       "overshare",   "Overshare");
                Register(StatType.Chaos,         "unhinged",    "Unhinged");
                Register(StatType.Wit,           "pretentious", "Pretentious");
                Register(StatType.SelfAwareness, "spiral",      "Spiral");
            }
            private void Register(StatType stat, string id, string name)
            {
                _traps[stat] = new TrapDefinition(
                    id, stat, TrapEffect.Disadvantage, 0, 3,
                    llmInstruction: $"{name} taint instruction",
                    clearMethod: "Pick any Self-Awareness option (selection disarms; SA fail triggers Spiral)",
                    nat1Bonus: "",
                    displayName: name,
                    summary: $"{name} trap summary.");
            }
            public TrapDefinition? GetTrap(StatType stat)
            {
                _traps.TryGetValue(stat, out var t);
                return t;
            }
            public string? GetLlmInstruction(StatType stat)
            {
                _traps.TryGetValue(stat, out var t);
                return t?.LlmInstruction;
            }
        }

        private static StatBlock MakeStatBlock(int allStats = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm,         allStats },
                    { StatType.Rizz,          allStats },
                    { StatType.Honesty,       allStats },
                    { StatType.Chaos,         allStats },
                    { StatType.Wit,           allStats },
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

        // The LLM adapter places SA at index 3 and the failing-charm trap option at 0.
        private const int CharmIndex = 0;
        private const int SaIndex = 3;
        private const int WitIndex = 2;

        /// <summary>
        /// Dice roller that yields a queued sequence and falls back to a default
        /// when the queue is empty. Avoids brittle dice-counting in tests where
        /// downstream paths (ghost rolls, advantage/disadvantage, ComputeDelay)
        /// vary by interest state.
        /// </summary>
        private sealed class ScriptedDice : IDiceRoller
        {
            private readonly Queue<int> _q;
            private readonly int _default;
            public ScriptedDice(int defaultRoll, params int[] script)
            {
                _default = defaultRoll;
                _q = new Queue<int>(script);
            }
            public int Roll(int sides)
            {
                int v = _q.Count > 0 ? _q.Dequeue() : _default;
                if (v < 1) v = 1;
                if (v > sides) v = sides;
                return v;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Tests
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 1. Trap activates on turn N. Turn N+1: player picks NON-SA. Trap LLM
        ///    overlay fires; the `Trap (X)` diff layer is appended; TurnsRemaining
        ///    decrements from 2 → 1 after AdvanceTurn at end of turn N+1.
        /// </summary>
        [Fact]
        public async Task PersistenceTurn_NonSaPick_FiresTrapOverlay_AndAddsTrapDiff()
        {
            var llm = new W2aTrapLlmAdapter();
            var registry = new StubTrapRegistry();

            // DC for stat=2 attacker, defender stats=2: stat-mod +2, defender-defence DC=14+2=16.
            // Roll d20=4 → 4+2+0 = 6 → miss by 9 → TropeTrap → activates Cringe (Charm trap).
            // T2 picks Charm again — trap on Charm = Disadvantage, so 2 d20s are rolled
            // and the LOWER value is used. Both d20=18 → lower=18, total=20 ≥ DC → success.
            var dice = new FixedDice(
                5,            // ctor horniness
                4,  10,       // T1: trip TropeTrap on Charm (activates Cringe)
                18, 18, 10    // T2: 2 d20s under disadvantage — success on Charm
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, registry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // T1 — activate trap (turn 1 of 3, taint via roll-modification)
            await session.StartTurnAsync();
            var t1 = await session.ResolveTurnAsync(CharmIndex);
            Assert.NotNull(t1.Roll.ActivatedTrap);
            Assert.Equal("cringe", t1.Roll.ActivatedTrap!.Id);
            // No Trap (Cringe) diff on activation turn (taint is the failure-tier rewrite).
            Assert.DoesNotContain(t1.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName.StartsWith("Trap ("));
            Assert.Equal(0, llm.TrapOverlayCalls);

            // T2 — persistence; non-SA pick → Trap LLM call fires.
            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(CharmIndex);
            Assert.True(t2.Roll.IsSuccess, "T2 should succeed with d20=18");
            Assert.Equal(1, llm.TrapOverlayCalls);
            Assert.Contains(t2.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName == "Trap (Cringe)");
        }

        /// <summary>
        /// 2. Trap activates on turn N. Turn N+1 player picks SA. Trap is cleared
        ///    at start of ResolveTurnAsync, BEFORE the SA roll resolves. No Trap
        ///    (X) diff layer on this turn even if the SA roll later succeeds.
        /// </summary>
        [Fact]
        public async Task SaPick_DisarmsTrap_NoTrapDiffThisTurn_OnSuccess()
        {
            var llm = new W2aTrapLlmAdapter();
            var registry = new StubTrapRegistry();

            var dice = new FixedDice(
                5,           // ctor horniness
                4,  10,      // T1: TropeTrap on Charm → Cringe activates
                18, 10       // T2: SA pick, roll d20=18 → success → Cringe cleared, no Spiral
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, registry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(CharmIndex);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(SaIndex);

            Assert.True(t2.Roll.IsSuccess);
            Assert.Equal("Cringe", t2.TrapClearedDisplayName);
            // No trap-overlay LLM call this turn — disarm fired before pipeline.
            Assert.Equal(0, llm.TrapOverlayCalls);
            Assert.DoesNotContain(t2.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName.StartsWith("Trap ("));
            // No active trap by end of turn (no Spiral activated since SA succeeded).
            Assert.False(t2.StateAfter.ActiveTrapNames.Any());
        }

        /// <summary>
        /// 3. Trap active. SA picked. SA roll fails into Misfire-or-worse. Old trap
        ///    is cleared at start of ResolveTurnAsync. Spiral activates on the SA
        ///    failure. The diff label for THIS turn is the failure tier (NOT
        ///    Trap (Spiral)). Spiral's TurnsRemaining starts at 3, decremented to
        ///    2 by end-of-turn AdvanceTurn.
        /// </summary>
        [Fact]
        public async Task SaPick_OldTrapCleared_FailingSaActivatesSpiral_NoSeparateTrapOverlayThisTurn()
        {
            var llm = new W2aTrapLlmAdapter();
            var registry = new StubTrapRegistry();

            // T1 charm-trap (TropeTrap = miss by 6-9): roll 4 → miss by 9 → Cringe activates.
            // T2 SA pick: need SA failure that activates a trap. Use d20=4 → SA total 6, DC 16,
            // miss 10 → Catastrophe → Spiral activates (replacing Cringe).
            var dice = new FixedDice(
                5,         // ctor horniness
                4, 10,     // T1: TropeTrap Charm
                4, 10      // T2: SA → Catastrophe (also a trap-tier)
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, registry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(CharmIndex); // Cringe active

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(SaIndex); // disarms Cringe; SA fails → Spiral

            Assert.False(t2.Roll.IsSuccess);
            Assert.Equal(StatType.SelfAwareness, t2.Roll.Stat);
            Assert.Equal("Cringe", t2.TrapClearedDisplayName);
            // Spiral activated on this turn's roll
            Assert.NotNull(t2.Roll.ActivatedTrap);
            Assert.Equal("spiral", t2.Roll.ActivatedTrap!.Id);
            // No separate trap LLM overlay on Spiral's activation turn.
            Assert.Equal(0, llm.TrapOverlayCalls);
            Assert.DoesNotContain(t2.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName.StartsWith("Trap ("));
            // Active trap at end of turn = Spiral with TurnsRemaining=2 (3 - 1).
            Assert.Single(t2.StateAfter.ActiveTrapNames);
            Assert.Equal("spiral", t2.StateAfter.ActiveTrapNames[0]);
            Assert.Equal(2, t2.StateAfter.ActiveTrapDetails[0].TurnsRemaining);
        }

        /// <summary>
        /// 4 + 5. Spiral persistence over two non-SA turns: trap overlay fires on
        /// each, TurnsRemaining decrements 2 → 1 → 0; Spiral removed after turn 3.
        /// </summary>
        [Fact]
        public async Task SpiralPersistence_TwoTurns_OverlayFires_AndExpires()
        {
            var llm = new W2aTrapLlmAdapter();
            var registry = new StubTrapRegistry();

            // T1: SA pick, Catastrophe → activates Spiral on turn N (turn 1 of 3).
            // T2/T3/T4: non-SA Wit picks. Default to high d20 (18) so success is
            // achieved even when interest-state side-effects (advantage / disadvantage /
            // ghost-d4) consume extra dice. Script the T1 catastrophe roll explicitly.
            var dice = new ScriptedDice(
                defaultRoll: 18,
                5,           // ctor horniness
                4            // T1 d20 → 4+2 = 6 → miss by 10 → Catastrophe (Spiral activates)
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, registry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            // T1
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(SaIndex);

            // T2: Wit pick — Spiral persists, overlay fires.
            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(WitIndex);
            Assert.True(t2.Roll.IsSuccess);
            Assert.Equal(1, llm.TrapOverlayCalls);
            Assert.Contains(t2.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName == "Trap (Spiral)");
            Assert.Single(t2.StateAfter.ActiveTrapNames);
            Assert.Equal(1, t2.StateAfter.ActiveTrapDetails[0].TurnsRemaining);

            // T3: Wit pick — overlay fires; trap removed at end of turn (3 → 0).
            await session.StartTurnAsync();
            var t3 = await session.ResolveTurnAsync(WitIndex);
            Assert.Equal(2, llm.TrapOverlayCalls);
            Assert.Contains(t3.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName == "Trap (Spiral)");
            Assert.Empty(t3.StateAfter.ActiveTrapNames);

            // T4: no trap, no overlay.
            await session.StartTurnAsync();
            var t4 = await session.ResolveTurnAsync(WitIndex);
            Assert.Equal(2, llm.TrapOverlayCalls);
            Assert.DoesNotContain(t4.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName.StartsWith("Trap ("));
        }

        /// <summary>
        /// 6. New trap activates while another is mid-cycle (non-SA pick).
        /// New trap REPLACES the old one; this turn's roll-modification is the
        /// new trap's turn-1 taint; NO separate trap LLM call this turn.
        /// </summary>
        [Fact]
        public async Task PersistenceTurn_FreshFailureActivatesNewTrap_ReplacesOld_NoOverlay()
        {
            var llm = new W2aTrapLlmAdapter();
            var registry = new StubTrapRegistry();

            // T1: TropeTrap on Charm → Cringe activates.
            // T2: pick Wit, roll d20=4 → miss by 9 → TropeTrap on Wit → Pretentious replaces Cringe.
            //     Because a NEW trap activated this turn, the Trap-overlay step is SKIPPED.
            var dice = new FixedDice(
                5,
                4, 10,    // T1: Charm TropeTrap
                4, 10     // T2: Wit TropeTrap
            );

            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, dice, registry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(CharmIndex);

            await session.StartTurnAsync();
            var t2 = await session.ResolveTurnAsync(WitIndex);

            Assert.NotNull(t2.Roll.ActivatedTrap);
            Assert.Equal("pretentious", t2.Roll.ActivatedTrap!.Id);
            // NO trap LLM call on this turn — activation turns skip the overlay.
            Assert.Equal(0, llm.TrapOverlayCalls);
            Assert.DoesNotContain(t2.TextDiffs ?? (IReadOnlyList<TextDiff>)System.Array.Empty<TextDiff>(),
                d => d.LayerName.StartsWith("Trap ("));
            // Active trap at end of turn = Pretentious with TurnsRemaining=2 (3 - 1).
            Assert.Single(t2.StateAfter.ActiveTrapNames);
            Assert.Equal("pretentious", t2.StateAfter.ActiveTrapNames[0]);
            Assert.Equal(2, t2.StateAfter.ActiveTrapDetails[0].TurnsRemaining);
        }

        /// <summary>
        /// 7. Resume path (#369 case-mismatch fold-in). RestoreState round-trips
        /// trap state regardless of stat-string casing in the snapshot data.
        /// </summary>
        [Theory]
        [InlineData("SelfAwareness")]
        [InlineData("selfawareness")]
        [InlineData("SELFAWARENESS")]
        public async Task RestoreState_PreservesActiveTrap_CaseInsensitiveStat(string snapshotStatCase)
        {
            var llm = new W2aTrapLlmAdapter();
            var registry = new StubTrapRegistry();
            var session = new GameSession(
                MakeProfile("Player"), MakeProfile("Opponent"),
                llm, new FixedDice(5, 18, 18), registry,
                new GameSessionConfig(clock: TestHelpers.MakeClock()));

            var data = new ResimulateData
            {
                TargetInterest = 12,
                TurnNumber = 5,
                MomentumStreak = 0,
                ActiveTraps = new List<(string, int)> { (snapshotStatCase, 2) }
            };

            session.RestoreState(data, registry);

            var start = await session.StartTurnAsync();
            // After restore, the trap state should expose the spiral trap on the
            // game-state snapshot regardless of the snapshot's stat-string casing.
            Assert.Single(start.State.ActiveTrapNames);
            Assert.Equal("spiral", start.State.ActiveTrapNames[0]);
            Assert.Equal(2, start.State.ActiveTrapDetails[0].TurnsRemaining);
        }
    }
}
