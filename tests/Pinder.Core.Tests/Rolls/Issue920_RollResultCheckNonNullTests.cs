using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Regression coverage for #920.
    ///
    /// After Phase 1 (#918) the <see cref="RollResult.Check"/> property became the
    /// canonical wire-DTO source for d20 mechanics, but a forced-fail constructed via
    /// <c>GameSession.CreateForcedFailResult</c> and ~12 test fixtures still passed
    /// null for <c>check</c>, leaving <see cref="RollResult.Check"/> null at
    /// construction time. The Phase 2 wire-DTO serializer would NRE on access.
    ///
    /// #920 fix:
    ///   1. Added <see cref="RollCheckResult.Synthesise"/> — builds a canonical
    ///      check from the bespoke fields a forced-fail <see cref="RollResult"/>
    ///      carries.
    ///   2. Tightened <see cref="RollResult"/>'s primary constructor: the
    ///      <c>check</c> parameter is now required and non-nullable (no more
    ///      <c>check!</c> null-suppression).
    ///   3. The legacy convenience constructor (bespoke fields only) now auto-
    ///      synthesises <see cref="RollCheckResult"/> so every code path
    ///      produces a non-null <see cref="RollResult.Check"/>.
    /// </summary>
    public class Issue920_RollResultCheckNonNullTests
    {
        // ----------------------------------------------------------------
        // Synthesise factory — bespoke→check field parity
        // ----------------------------------------------------------------

        [Fact]
        public void Synthesise_BuildsForcedFailCheck_WithConsistentFields()
        {
            // A typical forced-fail input shape (cf. GameSession.CreateForcedFailResult).
            int dc = 14;
            int fakeDie = dc - 1; // 13 — just below DC, miss margin 1 → Fumble (FailureTierLadder)

            RollCheckResult check = RollCheckResult.Synthesise(
                dieRoll:       fakeDie,
                secondDieRoll: null,
                usedDieRoll:   fakeDie,
                statModifier:  0,
                levelBonus:    0,
                dc:            dc);

            Assert.Equal(RollCheckKind.OptionRoll, check.Kind);
            Assert.Equal(fakeDie, check.DieRoll);
            Assert.Equal(fakeDie, check.UsedDieRoll);
            Assert.Null(check.SecondDieRoll);
            Assert.Equal(dc, check.Dc);
            Assert.Equal(fakeDie, check.Total);        // 13 + 0 + 0
            Assert.False(check.IsSuccess);
            Assert.False(check.IsNatOne);
            Assert.False(check.IsNatTwenty);
            Assert.Equal(1, check.MissMargin);          // 14 - 13
            Assert.Equal(FailureTier.Fumble, check.Tier);
            Assert.Equal(2, check.Modifiers.Count);     // [stat, level]
            Assert.Contains(check.Modifiers, m => m.Key == "stat");
            Assert.Contains(check.Modifiers, m => m.Key == "level");
        }

        [Fact]
        public void Synthesise_NatTwentyOverDc_ReportsSuccessAndFlag()
        {
            RollCheckResult check = RollCheckResult.Synthesise(
                dieRoll:       20,
                secondDieRoll: null,
                usedDieRoll:   20,
                statModifier:  0,
                levelBonus:    0,
                dc:            50);

            // RollCheckResult itself does not apply the nat-20 auto-success game rule;
            // it just reports the raw mechanics. Here total < dc so success is false.
            Assert.True(check.IsNatTwenty);
            Assert.False(check.IsSuccess);
            Assert.Equal(30, check.MissMargin);
        }

        [Fact]
        public void Synthesise_FoldsExternalBonusIntoTotal()
        {
            // 10 + statMod 2 + level 1 + external 3 = 16 vs DC 14 → success
            RollCheckResult check = RollCheckResult.Synthesise(
                dieRoll:       10,
                secondDieRoll: null,
                usedDieRoll:   10,
                statModifier:  2,
                levelBonus:    1,
                dc:            14,
                externalBonus: 3);

            Assert.Equal(16, check.Total);
            Assert.True(check.IsSuccess);
            Assert.Equal(0, check.MissMargin);
            Assert.Equal(FailureTier.Success, check.Tier);
        }

        // ----------------------------------------------------------------
        // RollResult — Check is non-null at construction time
        // ----------------------------------------------------------------

        [Fact]
        public void ConvenienceCtor_AutoSynthesisesCheck_AndFieldsAgreeWithBespoke()
        {
            // Old-style call (no `check` argument) — must still produce a non-null Check.
            var result = new RollResult(
                dieRoll: 13,
                secondDieRoll: null,
                usedDieRoll: 13,
                stat: StatType.Charm,
                statModifier: 0,
                levelBonus: 0,
                dc: 14,
                tier: FailureTier.Misfire);

            Assert.NotNull(result.Check);
            Assert.Equal(result.DieRoll,       result.Check.DieRoll);
            Assert.Equal(result.UsedDieRoll,   result.Check.UsedDieRoll);
            Assert.Equal(result.SecondDieRoll, result.Check.SecondDieRoll);
            Assert.Equal(result.DC,            result.Check.Dc);
            // Bespoke Total = die + statMod + level (no external) = 13.
            // Check.Total folds externalBonus (0 here) — same value.
            Assert.Equal(result.Total + result.ExternalBonus, result.Check.Total);
            Assert.Equal(result.IsNatOne,      result.Check.IsNatOne);
            Assert.Equal(result.IsNatTwenty,   result.Check.IsNatTwenty);
        }

        [Fact]
        public void PrimaryCtor_RequiresNonNullCheck()
        {
            // The tightened primary ctor must throw ArgumentNullException on null.
            Assert.Throws<ArgumentNullException>(() => new RollResult(
                dieRoll:       10,
                secondDieRoll: null,
                usedDieRoll:   10,
                stat:          StatType.Charm,
                statModifier:  0,
                levelBonus:    0,
                dc:            12,
                tier:          FailureTier.Misfire,
                activatedTrap: null,
                externalBonus: 0,
                check:         null!));
        }

        [Fact]
        public void PrimaryCtor_PreservesProvidedCheckVerbatim()
        {
            // When the caller supplies a Check (RollEngine path), it must be stored as-is.
            var modifiers = new[] { new NamedModifier("stat", 3), new NamedModifier("level", 1) };
            var supplied = new RollCheckResult(
                RollCheckKind.OptionRoll,
                dieRoll: 15, secondDieRoll: null, usedDieRoll: 15,
                modifiers: modifiers,
                modifierSum: 4, total: 19, dc: 12,
                isSuccess: true, isNatOne: false, isNatTwenty: false,
                tier: FailureTier.Success, missMargin: 0);

            var result = new RollResult(
                dieRoll:       15,
                secondDieRoll: null,
                usedDieRoll:   15,
                stat:          StatType.Charm,
                statModifier:  3,
                levelBonus:    1,
                dc:            12,
                tier:          FailureTier.Success,
                activatedTrap: null,
                externalBonus: 0,
                check:         supplied);

            Assert.Same(supplied, result.Check);
        }

        // ----------------------------------------------------------------
        // GameSession.CreateForcedFailResult — observed via the public flow
        // ----------------------------------------------------------------

        /// <summary>
        /// CreateForcedFailResult is private; we mirror the exact synthesis call here and
        /// assert the Check property is non-null + consistent. The mirror in
        /// Issue399_HorninessShadowOrderingTests already documents the construction
        /// pattern — this test guards the #920 invariant separately.
        /// </summary>
        [Fact]
        public void CreateForcedFailResult_Mirror_ProducesNonNullConsistentCheck()
        {
            int dc = 14;
            int fakeDie = dc - 1;

            var check = RollCheckResult.Synthesise(
                dieRoll: fakeDie, secondDieRoll: null, usedDieRoll: fakeDie,
                statModifier: 0, levelBonus: 0, dc: dc);

            var forced = new RollResult(
                dieRoll:        fakeDie,
                secondDieRoll:  null,
                usedDieRoll:    fakeDie,
                stat:           StatType.Charm,
                statModifier:   0,
                levelBonus:     0,
                dc:             dc,
                tier:           FailureTier.Catastrophe,
                activatedTrap:  null,
                externalBonus:  0,
                check:          check,
                defendingStat:  StatBlock.DefenceTable[StatType.Charm]);

            Assert.NotNull(forced.Check);
            Assert.Equal(forced.DieRoll,     forced.Check.DieRoll);
            Assert.Equal(forced.UsedDieRoll, forced.Check.UsedDieRoll);
            Assert.Equal(forced.DC,          forced.Check.Dc);
            Assert.Equal(StatType.Charm,     forced.Stat);
            Assert.False(forced.IsSuccess);
            Assert.False(forced.Check.IsSuccess);

            // bespoke vs Check.Tier divergence is expected here: bespoke Tier was
            // passed in as Catastrophe (the shadow-tier override). Check.Tier is
            // pure FailureTierLadder.FromMissMargin output → Fumble for miss-margin 1.
            // This divergence is documented on RollResult.Check.
            Assert.Equal(FailureTier.Catastrophe, forced.Tier);
            Assert.Equal(FailureTier.Fumble,      forced.Check.Tier);
        }
    }
}
