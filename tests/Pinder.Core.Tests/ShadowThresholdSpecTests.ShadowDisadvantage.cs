using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    public partial class ShadowThresholdSpecTests
    {
        // =====================================================================
        // AC2: T2 Disadvantage on paired stat rolls
        // =====================================================================

        // #755: T2 generic disadvantage removed — shadow check is now the mechanic.
        // These tests verify that T2 shadow no longer causes roll disadvantage.
        [Fact]
        public async Task AC2_DenialT2_HonestyNoLongerHasDisadvantage()
        {
            // What: Denial ≥12 → shadow check fires (not roll disadvantage) per #755
            // Dice: 18 → single roll used (no disadvantage)
            var shadows = TestHelpers.MakeShadowTracker(denial: 12);
            var session = MakeSession(
                diceValues: new[] { 18, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Truth time") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(18, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task AC2_DreadT2_WitNoLongerHasDisadvantage()
        {
            // What: Dread ≥12 → shadow check fires (not roll disadvantage) per #755
            var shadows = TestHelpers.MakeShadowTracker(dread: 14);
            var session = MakeSession(
                diceValues: new[] { 19, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Wit, "Witty") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(19, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task AC2_MadnessT2_CharmNoLongerHasDisadvantage()
        {
            // What: Madness ≥12 → shadow check fires (not roll disadvantage) per #755
            var shadows = TestHelpers.MakeShadowTracker(madness: 15);
            var session = MakeSession(
                diceValues: new[] { 17, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Hey") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(17, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task AC2_FixationT2_ChaosNoLongerHasDisadvantage()
        {
            // What: Fixation ≥12 → shadow check fires (not roll disadvantage) per #755
            var shadows = TestHelpers.MakeShadowTracker(fixation: 13);
            var session = MakeSession(
                diceValues: new[] { 16, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Chaos, "Wild!") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(16, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task AC2_OverthinkingT2_SANoLongerHasDisadvantage()
        {
            // What: Overthinking ≥12 → shadow check fires (not roll disadvantage) per #755
            var shadows = TestHelpers.MakeShadowTracker(overthinking: 12);
            var session = MakeSession(
                diceValues: new[] { 20, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.SelfAwareness, "I know") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(20, result.Roll.UsedDieRoll);
        }

        [Fact]
        public async Task AC2_DespairT2_RizzNoLongerHasDisadvantage()
        {
            // What: Despair ≥12 → shadow check fires (not roll disadvantage) per #755
            var shadows = TestHelpers.MakeShadowTracker(horniness: 14);
            var session = MakeSession(
                diceValues: new[] { 18, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Rizz, "Smooth") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(18, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if T1 (below T2) incorrectly triggers disadvantage
        [Fact]
        public async Task AC2_T1_NoDisadvantage()
        {
            // What: Denial=11 is T1 → NO disadvantage on Honesty
            var shadows = TestHelpers.MakeShadowTracker(denial: 11);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Honesty, "Honestly...") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            // Single roll, no disadvantage
            Assert.Equal(15, result.Roll.UsedDieRoll);
        }

        // Mutation: would catch if unpaired stat incorrectly gets disadvantage
        [Fact]
        public async Task AC2_UnpairedStat_NoDisadvantage()
        {
            // What: Denial T2 penalizes Honesty, NOT Charm (spec §3.2 pairing table)
            var shadows = TestHelpers.MakeShadowTracker(denial: 12);
            var session = MakeSession(
                diceValues: new[] { 15, 50 },
                shadows: shadows,
                llmOptions: new[] { new DialogueOption(StatType.Charm, "Charming!") });

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            Assert.Equal(15, result.Roll.UsedDieRoll);
        }
    }
}
